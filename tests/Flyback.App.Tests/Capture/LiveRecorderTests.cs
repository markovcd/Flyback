using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Avalonia;
using Flyback.App.Capture;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Capture;

/// <summary>
/// The recorder driven the way the render thread and the sound callback drive
/// it, writing a real file. What matters is that the two streams agree about how
/// long the take was and that the file is finished rather than merely stopped.
/// </summary>
public class LiveRecorderTests : IDisposable
{
    private const int SampleRate = 48_000;
    private const int Channels = 2;
    private const double Rate = 30d;

    private static readonly PixelSize Size = new(32, 18);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly string path = Path.Combine(
        Path.GetTempPath(),
        $"flyback-take-{Guid.NewGuid():N}");

    public void Dispose()
    {
        foreach (var kind in new[] { ".avi", ".wav" })
            if (File.Exists(path + kind))
                File.Delete(path + kind);

        GC.SuppressFinalize(this);
    }

    private RecordingSettings Video(bool withSound = true) => new(
        path + ".avi",
        Size,
        Rate,
        Quality: 60,
        withSound ? SampleRate : 0,
        withSound ? Channels : 0);

    private RecordingSettings Audio() => new(path + ".wav", default, Rate, 60, SampleRate, Channels);

    /// <summary>
    /// One frame's worth of picture and sound, offered the way the two threads
    /// offer them, until the take has the frames asked for or patience runs out.
    /// </summary>
    private static void Drive(LiveRecorder recorder, int untilFrames, bool picture = true, bool sound = true)
    {
        var frame = new byte[Size.Width * Size.Height * 4];
        var callback = new float[SampleRate * Channels / 120];   // half a frame at 30 fps
        var clock = Stopwatch.StartNew();
        var tint = 0;

        while (recorder.Status.Frames < untilFrames && clock.Elapsed < Patience)
        {
            if (picture)
            {
                Array.Fill(frame, (byte)(++tint % 251));
                recorder.Accept(frame, Size.Width, Size.Height);
            }

            if (sound) recorder.WriteAudio(callback);

            Thread.Sleep(1);
        }
    }

    /// <summary>
    /// Counts chunks of one fourcc by walking the movi list, which is the file's
    /// own account of itself.
    /// </summary>
    /// <remarks>
    /// Walked rather than scanned for. A JPEG's bytes are arbitrary and will
    /// eventually contain any four characters you care to look for, and the idx1
    /// table repeats every chunk id besides — so counting matches would count
    /// each frame at least twice and some pixels as well.
    /// </remarks>
    private static int Chunks(byte[] file, string fourcc)
    {
        var movi = Find(file, "movi");

        movi.ShouldBeGreaterThan(0, "the file should have a movi list");

        var at = movi + 4;
        var found = 0;

        while (at + 8 <= file.Length)
        {
            var id = Encoding.ASCII.GetString(file, at, 4);

            // The index sits immediately after the list and ends the walk.
            if (id is not ("00dc" or "01wb")) break;

            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 4, 4));

            if (id == fourcc) found++;

            // RIFF is word-aligned, so an odd chunk carries a pad byte.
            at += 8 + size + (size & 1);
        }

        return found;
    }

    private static int Find(byte[] file, string fourcc)
    {
        var wanted = Encoding.ASCII.GetBytes(fourcc);

        for (var at = 0; at + 4 <= file.Length; at++)
            if (file.AsSpan(at, 4).SequenceEqual(wanted))
                return at;

        return -1;
    }

    [Fact]
    public void A_take_is_a_playable_avi()
    {
        var recorder = new LiveRecorder(Video());

        Drive(recorder, untilFrames: 30);
        recorder.Dispose();

        var file = File.ReadAllBytes(path + ".avi");

        Encoding.ASCII.GetString(file, 0, 4).ShouldBe("RIFF");
        Encoding.ASCII.GetString(file, 8, 4).ShouldBe("AVI ");

        // The size RIFF claims has to be the size the file actually is, which is
        // the one thing a header that was never patched gets wrong.
        BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4, 4)).ShouldBe((uint)(file.Length - 8));
    }

    /// <summary>
    /// Every frame the recorder claims is a chunk in the file. A count that
    /// drifts from this is a take whose length is a guess.
    /// </summary>
    [Fact]
    public void Every_frame_counted_is_a_frame_written()
    {
        var recorder = new LiveRecorder(Video());

        Drive(recorder, untilFrames: 30);

        var claimed = recorder.Status.Frames;
        recorder.Dispose();

        claimed.ShouldBeGreaterThanOrEqualTo(30);
        Chunks(File.ReadAllBytes(path + ".avi"), "00dc").ShouldBe((int)claimed);
    }

    /// <summary>
    /// The picture is paced against the sound, so a second of samples is a second
    /// of video whatever the render thread managed. Loose on the upper side only
    /// because the last drain can carry the take slightly past where it was
    /// stopped.
    /// </summary>
    [Fact]
    public void The_picture_keeps_pace_with_the_sound()
    {
        var recorder = new LiveRecorder(Video());

        Drive(recorder, untilFrames: 60);

        var status = recorder.Status;
        recorder.Dispose();

        var expected = status.Seconds * Rate;

        status.Frames.ShouldBeInRange((long)(expected - 2), (long)(expected + 2));
    }

    /// <summary>
    /// A renderer that stops producing does not stop the file: the last frame is
    /// repeated so the sound does not run away from the picture.
    /// </summary>
    [Fact]
    public void A_picture_that_stalls_is_held_rather_than_skipped()
    {
        var recorder = new LiveRecorder(Video());

        // One frame, then nothing but sound.
        Drive(recorder, untilFrames: 1);
        Drive(recorder, untilFrames: 30, picture: false);

        var status = recorder.Status;
        recorder.Dispose();

        status.Frames.ShouldBeGreaterThanOrEqualTo(30);
        status.Duplicated.ShouldBeGreaterThan(0);

        Chunks(File.ReadAllBytes(path + ".avi"), "00dc").ShouldBe((int)status.Frames);
    }

    /// <summary>A patch that makes no sound records a silent video, not no video.</summary>
    [Fact]
    public void A_silent_take_is_still_a_take()
    {
        var recorder = new LiveRecorder(Video(withSound: false));

        Drive(recorder, untilFrames: 10, sound: false);
        recorder.Dispose();

        var file = File.ReadAllBytes(path + ".avi");

        Chunks(file, "00dc").ShouldBeGreaterThanOrEqualTo(10);
        Chunks(file, "01wb").ShouldBe(0);
    }

    /// <summary>Sound on its own needs no frames and does not wait for one.</summary>
    [Fact]
    public void A_sound_take_is_a_wav()
    {
        var recorder = new LiveRecorder(Audio());
        var callback = new float[SampleRate * Channels / 100];

        for (var i = 0; i < 20; i++)
        {
            recorder.WriteAudio(callback);
            Thread.Sleep(2);
        }

        recorder.Dispose();

        var file = File.ReadAllBytes(path + ".wav");

        Encoding.ASCII.GetString(file, 0, 4).ShouldBe("RIFF");
        Encoding.ASCII.GetString(file, 8, 4).ShouldBe("WAVE");

        BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(40, 4)).ShouldBe((uint)(file.Length - 44));
    }

    /// <summary>
    /// A recording of nothing is a mistake worth catching where it is made rather
    /// than after a file has been created for it.
    /// </summary>
    [Fact]
    public void A_take_of_neither_is_refused() =>
        Should.Throw<ArgumentException>(() =>
            new LiveRecorder(new RecordingSettings(path + ".avi", default, Rate, 60, 0, 0)));

    /// <summary>Stopping twice is what closing the window during a take does.</summary>
    [Fact]
    public void Stopping_twice_is_harmless()
    {
        var recorder = new LiveRecorder(Video());

        Drive(recorder, untilFrames: 2);

        recorder.Stop();
        recorder.Dispose();
        recorder.Dispose();

        File.ReadAllBytes(path + ".avi").Length.ShouldBeGreaterThan(0);
    }
}
