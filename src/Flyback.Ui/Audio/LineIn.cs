using Flyback.Core.Graph;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Flyback.Engine.Render;
using Flyback.Host;

namespace Flyback.Ui.Audio;

/// <summary>
/// The microphone, held open for exactly as long as the sound that is playing reads it.
/// </summary>
/// <remarks>
/// A Line In is the one module that listens to the person, so nothing is opened until a
/// running program holds one, and the device is let go when the sound stops, is paused
/// or loses the module (ADR-0178). What it hears goes into a <see cref="LineInFeed"/>
/// the sound's renderer drains. A device that will not open, or that stops on its own,
/// is said once and left alone until the settings change.
/// </remarks>
internal sealed class LineIn : IDisposable
{
    private readonly PluginCatalog plugins;
    private readonly IAudioEngine audio;
    private readonly Func<OutputSettings> settings;
    private readonly IAudioInput? own;
    private readonly LineInFeed feed = new();

    private IAudioCapture? capture;

    /// <summary>Set once a device has refused or stopped, so an edit does not retry it every frame of a drag.</summary>
    private bool refused;

    /// <param name="input">The sound input to listen through, where the host has one of its own; otherwise the plugins' preferred.</param>
    public LineIn(PluginCatalog plugins, IAudioEngine audio, Func<OutputSettings> settings, IAudioInput? input = null)
    {
        this.plugins = plugins;
        this.audio = audio;
        this.settings = settings;
        own = input;

        audio.Input = feed;
    }

    /// <summary>The device would not open, or stopped. Said out loud because the patch goes on naming a Line In that is now silent.</summary>
    public event Action<string>? Trouble;

    /// <summary>Whether a device is open and listening.</summary>
    public bool IsListening => capture is { IsRunning: true };

    /// <summary>Opens or closes the device to match what the playing sound reads.</summary>
    public void Follow()
    {
        if (capture is { IsRunning: false })
        {
            Close();
            Refuse("A Line In is silent: the sound input stopped listening.");
        }

        if (Wanted()) Open();
        else Close();
    }

    /// <summary>Takes up the Sound settings just saved: the device is opened anew if it is wanted.</summary>
    public void Reconfigure()
    {
        Close();
        refused = false;
        Follow();
    }

    public void Dispose() => Close();

    private bool Wanted() =>
        audio.IsRunning && (audio.Live.Reads(LineInSignal.Left) || audio.Live.Reads(LineInSignal.Right));

    private void Open()
    {
        if (capture is not null || refused) return;

        if ((own ?? plugins.PreferredAudioInput) is not { } input)
        {
            Refuse("A Line In is silent: no sound input is installed.");
            return;
        }

        try
        {
            var format = AudioFormat.Default with
            {
                SampleRate = audio.SampleRate,
                LatencyMilliseconds = settings().LatencyMilliseconds,
            };

            var opened = input.Create(format, settings().SoundInOf(input.Id));

            feed.Clear();
            opened.Start(heard => feed.Write(heard, opened.Channels));
            capture = opened;
        }
        catch (Exception ex)
        {
            Refuse($"A Line In is silent: {input.Name} could not listen — {ex.Message}");
        }
    }

    private void Close()
    {
        var closing = capture;
        capture = null;

        if (closing is null) return;

        try
        {
            closing.Dispose();
        }
        catch
        {
            // A device that will not close is gone either way.
        }

        feed.Clear();
    }

    private void Refuse(string message)
    {
        refused = true;
        Trouble?.Invoke(message);
    }
}
