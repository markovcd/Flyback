using System.Text.Json;
using Flyback.App.Midi;
using Flyback.Core;
using Flyback.Core.Render;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.App;

/// <summary>
/// What comes out of the program and how: the picture, a recorded take, and the
/// speakers — the Graphics, Recording and Sound sections of the settings window.
/// </summary>
/// <remarks>
/// Properties of the machine rather than of the instrument, which is why none of it
/// is saved with a patch (ADR-0037) and all of it is saved here, beside
/// <c>assistant.json</c>. Nothing here is load-bearing, for the same reason that
/// file is not (ADR-0034): an unreadable file means the defaults.
/// </remarks>
public sealed class OutputSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// The preview's size in pixels. Kept as the numbers rather than as a row of
    /// the size list, so that a list with a row added or taken away still reads a
    /// file written against the old one — a size it no longer offers is the default.
    /// </summary>
    public int Width { get; set; } = 960;

    /// <inheritdoc cref="Width"/>
    public int Height { get; set; } = 540;

    /// <summary>Whether the picture is drawn by a shader. Asked for, not promised: a machine with no usable GPU draws on the processor whatever this says.</summary>
    public bool Gpu { get; set; } = true;

    /// <summary>Frames a second in a recorded take — the Recording section.</summary>
    public double FrameRate { get; set; } = MovieRenderer.DefaultFrameRate;

    /// <summary>
    /// Frames a second the preview redraws at, or 0 to draw as fast as the
    /// renderer allows — the Graphics section. Independent of
    /// <see cref="FrameRate"/>: what is on screen and what a take writes are
    /// two different things, and a take reads whatever the preview last drew
    /// regardless of this.
    /// </summary>
    public double PreviewFrameRate { get; set; }

    /// <summary>How a recorded take's frames are compressed, from 1 to 100 — the Recording section.</summary>
    /// <remarks>
    /// Named for the encoder it was written for, and kept that way so a file
    /// saved before <see cref="VideoFormat"/> existed still reads. Every format
    /// reads it: a JPEG quality to the one written here, a rate factor to the
    /// rest — see <see cref="ClipFormat.Crf"/>.
    /// </remarks>
    public int JpegQuality { get; set; } = JpegWriter.DefaultQuality;

    /// <summary>
    /// Which of <see cref="ClipFormats.Pictures"/> a recorded take with a picture
    /// is written as — the Recording section.
    /// </summary>
    /// <remarks>
    /// The id rather than the row of the list, for the reason <see cref="Width"/>
    /// is the number rather than the row: a list that gains or loses a format
    /// still reads a file written against the old one.
    /// </remarks>
    public string VideoFormat { get; set; } = ClipFormats.MotionJpegAvi.Id;

    /// <inheritdoc cref="VideoFormat"/>
    public string SoundFormat { get; set; } = ClipFormats.Wav.Id;

    /// <summary>
    /// How many seconds the status bar counts a take in for once its file has been
    /// named, or 0 to start it at once — the Recording section (ADR-0091).
    /// </summary>
    public int CountInSeconds { get; set; } = DefaultCountIn;

    /// <summary>
    /// Whether a take takes the patch back to zero seconds before its first frame,
    /// as the Rewind button does — the Recording section (ADR-0091).
    /// </summary>
    /// <remarks>
    /// On by default, since a take that starts where the patch does is the one
    /// worth having twice. Off for recording something a session has already
    /// arrived at — an envelope halfway down, a loop full of what came before —
    /// which a rewind would be the end of.
    /// </remarks>
    public bool RewindBeforeTake { get; set; } = true;

    /// <summary>
    /// The ffmpeg to encode with, or empty to use whatever is on <c>PATH</c> —
    /// the Recording section.
    /// </summary>
    /// <remarks>
    /// Kept even while the formats chosen need no ffmpeg, so that picking one
    /// that does is one click rather than two.
    /// </remarks>
    public string FfmpegPath { get; set; } = string.Empty;

    /// <summary>
    /// How far behind the patch the speakers may run, in milliseconds — the Sound
    /// section. Asked of the device when it is opened, which is once a launch.
    /// </summary>
    public int LatencyMilliseconds { get; set; } = AudioFormat.Default.LatencyMilliseconds;

    /// <summary>
    /// The preset the window opens on at the next launch, by name — the Graphics
    /// section. Empty for the one written here, which is the first of the list
    /// (ADR-0093).
    /// </summary>
    /// <remarks>
    /// Kept as the name rather than the row: a plugin's preset list is not known
    /// here, so a name this build does not offer is read as though nothing were
    /// chosen rather than refused — the same courtesy <see cref="VideoFormat"/>
    /// gets from a format list that is.
    /// </remarks>
    public string DefaultPreset { get; set; } = string.Empty;

    /// <summary>
    /// What each sound backend's own form was last set to, filed under the backend's
    /// id — the rest of the Sound section, which the backend declares (ADR-0085).
    /// </summary>
    /// <remarks>
    /// Plain strings that nothing here reads, for the reason the assistant's are
    /// (ADR-0069): which device plays is a question only the backend knows how to ask.
    /// Kept per backend so a second one installed for an afternoon does not cost the
    /// device picked on the first. Public setter for the serialiser; everything else
    /// goes through <see cref="SoundOf"/> and <see cref="RememberSound"/>.
    /// </remarks>
    public Dictionary<string, Dictionary<string, string>> Sound { get; set; } = new(StringComparer.Ordinal);

    /// <summary>What a MIDI controller does to a knob sitting somewhere else — the MIDI section.</summary>
    public Takeover Takeover { get; set; }

    /// <summary>
    /// How the computer keyboard is laid out on a patch when its first MIDI In is
    /// added — the MIDI section. A patch that already has one, or has none, is not touched.
    /// </summary>
    public KeyboardLayout Keyboard { get; set; }

    /// <summary>Which monitor double-clicking the preview fills — the Graphics section.</summary>
    public FullScreenOn FullScreen { get; set; }

    /// <summary>
    /// The monitor <see cref="FullScreenOn.ChosenMonitor"/> means. Kept while another
    /// choice is in force, and while that monitor is unplugged.
    /// </summary>
    public MonitorSpot? FullScreenMonitor { get; set; }

    /// <summary>What is set for one backend, and nothing for one nobody has configured.</summary>
    public SettingValues SoundOf(string backend) =>
        Sound.TryGetValue(backend, out var held) ? new SettingValues(held) : SettingValues.None;

    /// <summary>Takes one backend's answers, leaving every other backend's alone.</summary>
    public void RememberSound(string backend, SettingValues values) =>
        Sound[backend] = new Dictionary<string, string>(values.All, StringComparer.Ordinal);

    public const int LowestQuality = 1, HighestQuality = 100;

    public const double SlowestFrameRate = 1, FastestFrameRate = 120;

    public const int ShortestLatency = 5, LongestLatency = 500;

    /// <summary>
    /// The count-in a machine with no settings file counts at: the three a
    /// sequencer counts a bar in at, and long enough to let go of the mouse.
    /// </summary>
    public const int DefaultCountIn = 3;

    /// <summary>Nought is no count at all, and the longest is a count nobody stands through twice.</summary>
    public const int NoCountIn = 0, LongestCountIn = 10;

    public static string File => Path.Combine(GlobalConstants.DataFolder, "output.json");

    /// <summary>
    /// What a machine with no settings file yet starts on: the properties' own
    /// defaults, except that the video format is H.264 wherever there is an
    /// ffmpeg to write it with.
    /// </summary>
    /// <remarks>
    /// The one setting whose default is a question about the machine rather than
    /// a number. Motion JPEG is written here and so is always available, but it
    /// is twenty-five times the size and reaches AVI's 4 GB ceiling in about half
    /// an hour — a fallback rather than a preference (ADR-0089). The sound default stays
    /// the WAV, which is exact; an MP3 is a choice somebody makes, not one to
    /// make for them.
    /// <para>
    /// Only for a file that does not exist. A saved format is never second-guessed
    /// — see <see cref="ClipFormats.Wanted"/> — so this cannot overwrite what
    /// anybody chose, and a machine that gains an ffmpeg after the first launch
    /// is one settings window away from using it.
    /// </para>
    /// </remarks>
    private static OutputSettings Fresh() => new()
    {
        VideoFormat = ClipFormats.Preferred(Ffmpeg.Resolve(null) is not null).Id,
    };

    /// <summary>Never throws. A settings file is not worth a failure to start.</summary>
    public static OutputSettings Load(string path)
    {
        try
        {
            var settings = System.IO.File.Exists(path)
                ? JsonSerializer.Deserialize<OutputSettings>(System.IO.File.ReadAllText(path), Options) ?? Fresh()
                : Fresh();

            // Brought into range rather than refused, since the file is one
            // somebody may have edited by hand: a frame rate of nought or a
            // latency of an hour would each break something far from here.
            settings.FrameRate = Math.Clamp(settings.FrameRate, SlowestFrameRate, FastestFrameRate);

            // Nought is a real choice here — uncapped — rather than the "nobody
            // set this" that FrameRate above takes it for; only a value someone
            // actually gave is brought into range.
            settings.PreviewFrameRate = settings.PreviewFrameRate <= 0
                ? 0
                : Math.Clamp(settings.PreviewFrameRate, SlowestFrameRate, FastestFrameRate);

            settings.JpegQuality = Math.Clamp(settings.JpegQuality, LowestQuality, HighestQuality);

            // Nought is a real choice here, as it is for the preview rate: no
            // count at all, rather than nobody having set one.
            settings.CountInSeconds = Math.Clamp(settings.CountInSeconds, NoCountIn, LongestCountIn);

            // A format this build does not define reads as the one written here.
            // Whether ffmpeg is on this machine is not asked: that is a question
            // about this launch, and the answer to it must not overwrite what
            // somebody chose.
            settings.VideoFormat = ClipFormats.Wanted(settings.VideoFormat, picture: true).Id;
            settings.SoundFormat = ClipFormats.Wanted(settings.SoundFormat, picture: false).Id;
            settings.FfmpegPath ??= string.Empty;
            settings.LatencyMilliseconds = Math.Clamp(settings.LatencyMilliseconds, ShortestLatency, LongestLatency);

            // A "defaultPreset": null typed by hand is the one written here, not a fault.
            settings.DefaultPreset ??= string.Empty;

            // A "sound": null typed by hand is nothing chosen, not a fault.
            settings.Sound ??= new(StringComparer.Ordinal);

            if (!Enum.IsDefined(settings.Takeover)) settings.Takeover = Takeover.Jump;
            if (!Enum.IsDefined(settings.Keyboard)) settings.Keyboard = KeyboardLayout.Piano;
            if (!Enum.IsDefined(settings.FullScreen)) settings.FullScreen = FullScreenOn.SameMonitor;

            return settings;
        }
        catch
        {
            return new OutputSettings();
        }
    }

    /// <summary>Throws if it cannot write, so the caller can say so.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? GlobalConstants.DataFolder);
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }
}
