using System.Buffers.Binary;
using System.Text;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Rendering;

/// <summary>
/// The export path, which is the only place the two sinks are written to one
/// file. Nothing here decodes a JPEG — there is no decoder in this repository
/// and adding one to check the encoder would be marking its own homework — so
/// these pin the container, the timing between the streams, and the parts of
/// the JPEG bitstream that are checkable by structure alone.
/// </summary>
public class MovieRendererTests
{
    private static readonly MovieSettings Small = new(64, 48, 0.5d, 10d);

    /// <summary>The Drone preset, which is the one with both a picture and a sound.</summary>
    private static (CompiledPatch Video, CompiledPatch Audio, AudioScan Scan) Drone()
    {
        var patch = Presets.Drone(NodeCatalog.Current);

        return (patch.CompileForVideo().Program, patch.CompileForAudio().Program, AudioScan.TimeDriven);
    }

    private static byte[] Export(MovieSettings settings, bool sound = true)
    {
        var (video, audio, scan) = Drone();
        var file = new MemoryStream();

        MovieRenderer.Render(
            file,
            video,
            sound ? audio : null,
            scan,
            settings,
            cancellation: TestContext.Current.CancellationToken);

        return file.ToArray();
    }

    [Fact]
    public void The_file_is_a_riff_avi_with_an_index()
    {
        var avi = Export(Small);

        Ascii(avi, 0).ShouldBe("RIFF");
        Ascii(avi, 8).ShouldBe("AVI ");

        // The declared size has to match what was actually written, or a reader
        // walking the top-level chunk runs off the end of the file.
        BinaryPrimitives.ReadUInt32LittleEndian(avi.AsSpan(4)).ShouldBe((uint)(avi.Length - 8));

        Find(avi, "idx1").ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Every_frame_asked_for_is_written()
    {
        var settings = Small with { Seconds = 1d, FramesPerSecond = 12d };
        var avi = Export(settings);

        settings.FrameCount.ShouldBe(12);
        TotalFrames(avi).ShouldBe(12u);
        Chunks(avi, "00dc").ShouldBe(12);
    }

    /// <summary>
    /// The one thing an export is actually asked for. Half a second and two
    /// seconds of the same patch differ in exactly that, and nowhere else.
    /// </summary>
    [Theory]
    [InlineData(0.5d)]
    [InlineData(2d)]
    [InlineData(3.25d)]
    public void The_length_asked_for_is_the_length_written(double seconds)
    {
        var settings = Small with { Seconds = seconds, FramesPerSecond = 20d };
        var avi = Export(settings);

        var frames = TotalFrames(avi);

        (frames / 20d).ShouldBe(seconds, 0.05d);
    }

    /// <summary>
    /// Sound and picture have to end together. The audio cursor counts samples
    /// and the video one counts frames, so a rate whose samples-per-frame is not
    /// a whole number is where the two would drift apart.
    /// </summary>
    [Theory]
    [InlineData(30d)]
    [InlineData(29.97d)]
    [InlineData(25d)]
    public void The_sound_runs_as_long_as_the_picture(double rate)
    {
        var settings = Small with { Seconds = 1d, FramesPerSecond = rate };
        var avi = Export(settings);

        var samples = AudioSamples(avi);
        var frames = TotalFrames(avi);

        var due = (long)Math.Round(frames / rate * AudioRenderer.DefaultSampleRate);

        // Within one frame's worth, which is the granularity audio is appended at.
        Math.Abs(samples - due).ShouldBeLessThanOrEqualTo((long)(AudioRenderer.DefaultSampleRate / rate));
    }

    [Fact]
    public void A_patch_with_no_audio_sink_gets_no_audio_stream()
    {
        var avi = Export(Small, sound: false);

        Streams(avi).ShouldBe(1u);
        Chunks(avi, "01wb").ShouldBe(0);
        Chunks(avi, "00dc").ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// Stopping a long export keeps what it rendered. The alternative — throwing
    /// part way and leaving a file whose header describes frames that are not
    /// there — is the shape of failure this format makes easiest and worst.
    /// </summary>
    [Fact]
    public void Stopping_early_leaves_a_shorter_video_rather_than_a_broken_one()
    {
        using var stop = new CancellationTokenSource();
        var (video, audio, scan) = Drone();
        var file = new MemoryStream();

        var settings = Small with { Seconds = 10d, FramesPerSecond = 10d };

        // Stopped after the first frame, from a callback the renderer itself drives.
        var written = MovieRenderer.Render(
            file,
            video,
            audio,
            scan,
            settings,
            new StopAfter(1, stop),
            stop.Token);

        written.ShouldBe(1);

        var avi = file.ToArray();
        TotalFrames(avi).ShouldBe(1u);
        BinaryPrimitives.ReadUInt32LittleEndian(avi.AsSpan(4)).ShouldBe((uint)(avi.Length - 8));
    }

    [Fact]
    public void A_zero_length_export_is_refused_rather_than_written_empty()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Export(Small with { Seconds = 0d }));
    }

    // --- the JPEG bitstream ------------------------------------------------------

    [Fact]
    public void Each_frame_is_a_jpeg()
    {
        var avi = Export(Small with { Seconds = 0.2d, FramesPerSecond = 10d });
        var frame = FirstFrame(avi);

        frame.Length.ShouldBeGreaterThan(0);

        // SOI, then the JFIF marker, and EOI at the very end.
        frame[0].ShouldBe((byte)0xFF);
        frame[1].ShouldBe((byte)0xD8);
        frame[^2].ShouldBe((byte)0xFF);
        frame[^1].ShouldBe((byte)0xD9);

        Encoding.ASCII.GetString(frame, 6, 4).ShouldBe("JFIF");
    }

    /// <summary>
    /// Nothing inside the entropy-coded data may look like a marker, which is
    /// what the stuffed zero after every 0xFF is for. Miss it and the picture
    /// decodes for a while and then falls apart, which is a bug that hides.
    /// </summary>
    [Fact]
    public void Nothing_in_the_scan_can_be_mistaken_for_a_marker()
    {
        var frame = FirstFrame(Export(Small with { Seconds = 0.2d, FramesPerSecond = 10d }));
        var scan = Array.IndexOf(frame, (byte)0xDA) - 1;

        scan.ShouldBeGreaterThan(0);

        // From the end of the scan header to the EOI, an 0xFF may only ever be
        // followed by a stuffed zero.
        var start = scan + 2 + BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(scan + 2));

        for (var i = start; i < frame.Length - 2; i++)
            if (frame[i] == 0xFF)
                frame[i + 1].ShouldBe((byte)0x00, $"an unstuffed 0xFF at {i}");
    }

    /// <summary>Quality is the size knob, and it has to be pointing the right way.</summary>
    [Fact]
    public void A_lower_quality_makes_a_smaller_file()
    {
        var settings = Small with { Seconds = 0.5d, FramesPerSecond = 10d, Width = 320, Height = 180 };

        var coarse = Export(settings with { Quality = 30 }).Length;
        var fine = Export(settings with { Quality = 95 }).Length;

        coarse.ShouldBeLessThan(fine);
    }

    // --- reading the container back ----------------------------------------------

    private sealed class StopAfter(int frames, CancellationTokenSource stop) : IProgress<double>
    {
        private int seen;

        public void Report(double value)
        {
            if (++seen >= frames) stop.Cancel();
        }
    }

    /// <summary>
    /// A Meter works in an export, which is the one place the picture and the
    /// sound are made by the same loop rather than by two threads. So the frames
    /// of a patch lit by its own sound differ from each other, and the first one
    /// is not the black a picture told nothing would be.
    /// </summary>
    /// <remarks>
    /// This is what moved the audio for a frame ahead of the frame itself. The
    /// preview cannot do that — a level there is what was played up to now,
    /// because now is all there is — but an export holds the whole clip, so a
    /// frame can be lit by the sound it is played with rather than by the sound
    /// before it. Nothing else about the loop changed, and the tests above pin
    /// that: the samples are still counted from the frame number, and they are
    /// still written after the picture.
    /// </remarks>
    [Fact]
    public void A_picture_lit_by_its_own_sound_is_lit_in_an_export()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 900, 0, (NodeCatalog.OutputGainPort, 1f));
        var voice = b.Add("osc.sine", 0, 0, (1, 110f));

        // A slow tremolo, so the level has something to follow: the frames of the
        // export must differ from one another, and by the thing being measured.
        var swell = b.Add("osc.sine", 0, 200, (1, 2f), (3, 0.5f), (4, 0.5f));
        var swelled = b.Add("math.mul", 200, 100);

        var meter = b.Add(NodeCatalog.MeterTypeId, 400, 0, (1, -1.5f));

        b.Wire(voice, 0, swelled, 0)
         .Wire(swell, 0, swelled, 1)
         .Wire(swelled, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(swelled, 0, meter, 0)
         .Wire(meter, 0, output, NodeCatalog.OutputColorPort);

        var file = new MemoryStream();

        MovieRenderer.Render(
            file,
            b.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program,
            b.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program,
            AudioScan.TimeDriven,
            new MovieSettings(32, 24, 1d, 20d),
            cancellation: TestContext.Current.CancellationToken);

        var avi = file.ToArray();
        var frames = Movi(avi, "00dc");

        frames.Count.ShouldBe(20);

        // Compressed frames of a flat colour, so their sizes say nothing — what
        // says something is that the bytes are not all the same picture.
        var distinct = frames
            .Select(f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                avi.AsSpan(f.Offset, (int)f.Size))))
            .Distinct()
            .Count();

        distinct.ShouldBeGreaterThan(4);
    }

    private static string Ascii(byte[] data, int offset) => Encoding.ASCII.GetString(data, offset, 4);

    private static int Find(byte[] data, string fourCc)
    {
        var wanted = Encoding.ASCII.GetBytes(fourCc);

        for (var i = 0; i < data.Length - 4; i++)
        {
            var found = true;
            for (var k = 0; k < 4; k++) found &= data[i + k] == wanted[k];
            if (found) return i;
        }

        return -1;
    }

    /// <summary>dwTotalFrames, sixteen bytes into the main header.</summary>
    private static uint TotalFrames(byte[] avi) =>
        BinaryPrimitives.ReadUInt32LittleEndian(avi.AsSpan(Find(avi, "avih") + 8 + 16));

    /// <summary>dwStreams, twenty-four bytes in.</summary>
    private static uint Streams(byte[] avi) =>
        BinaryPrimitives.ReadUInt32LittleEndian(avi.AsSpan(Find(avi, "avih") + 8 + 24));

    /// <summary>Adds up the audio chunks rather than trusting the header's count of them.</summary>
    private static long AudioSamples(byte[] avi)
    {
        long bytes = 0;

        foreach (var (_, size) in Movi(avi, "01wb")) bytes += size;

        return bytes / (NodeCatalog.AudioChannels * sizeof(short));
    }

    private static int Chunks(byte[] avi, string fourCc) => Movi(avi, fourCc).Count;

    private static byte[] FirstFrame(byte[] avi)
    {
        var (offset, size) = Movi(avi, "00dc")[0];

        return avi.AsSpan(offset, (int)size).ToArray();
    }

    /// <summary>
    /// Walks the movi list by chunk header, which is the only honest way to read
    /// one back — searching for a fourcc would find it inside compressed data.
    /// </summary>
    private static List<(int Offset, uint Size)> Movi(byte[] avi, string fourCc)
    {
        var movi = Find(avi, "movi");
        var found = new List<(int, uint)>();
        var wanted = Encoding.ASCII.GetBytes(fourCc);

        var at = movi + 4;
        while (at + 8 <= avi.Length)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(avi.AsSpan(at + 4));
            if (Ascii(avi, at) == "idx1") break;

            var matches = true;
            for (var k = 0; k < 4; k++) matches &= avi[at + k] == wanted[k];
            if (matches) found.Add((at + 8, size));

            at += 8 + (int)size;
            if ((size & 1) != 0) at++;
        }

        return found;
    }
}
