using System.Buffers.Binary;
using System.Text;

namespace Flyback.Core.Render;

/// <summary>
/// Writes a RIFF AVI holding a Motion JPEG video stream and, optionally, a
/// 16-bit PCM audio stream interleaved with it.
/// </summary>
/// <remarks>
/// AVI rather than MP4 for the same reason <see cref="WavWriter"/> is a WAV: it
/// is a container simple enough to write correctly by hand, and Motion JPEG is
/// the only compression that fits inside one without an inter-frame codec. MP4
/// with H.264 would be a smaller file and is not a thing anyone writes in four
/// hundred lines.
///
/// The cost is a header that has to be revisited. How many frames there turned
/// out to be, how large the largest chunk was and where every chunk landed are
/// all written at the front and none of them are known until the end, so this
/// needs a stream it can seek back through. That also means <see cref="Dispose"/>
/// is not a formality: a file whose header was never patched is not a video.
/// </remarks>
public sealed class AviWriter : IDisposable
{
    /// <summary>
    /// RIFF counts everything in unsigned 32-bit, so an AVI cannot pass 4 GB.
    /// Stopping short of it leaves room for the index, which is written last and
    /// is the one part whose size is known only then.
    /// </summary>
    private const long MaximumBytes = 3_900_000_000L;

    private const int VideoChunkId = 0x63643030;    // '00dc' — stream 0, compressed video
    private const int AudioChunkId = 0x62773130;    // '01wb' — stream 1, wave bytes

    private const int HasIndex = 0x10;
    private const int IsInterleaved = 0x100;
    private const int KeyFrame = 0x10;

    private readonly Stream output;
    private readonly int width;
    private readonly int height;
    private readonly int channels;
    private readonly int sampleRate;
    private readonly List<(int ChunkId, uint Offset, uint Size)> index = [];

    /// <summary>Reused across frames, since a movie is thousands of these.</summary>
    private byte[] pcm = [];

    private long moviPosition;
    private long headerPosition;
    private long videoStreamPosition;
    private long audioStreamPosition;

    private int frames;
    private long audioSamples;
    private int largestChunk;
    private bool closed;

    /// <param name="sampleRate">Ignored when <paramref name="channels"/> is zero.</param>
    /// <param name="channels">
    /// Zero writes a video-only file. A patch with no Audio Output has nothing
    /// to say, and a silent track claiming otherwise is worse than no track.
    /// </param>
    public AviWriter(Stream output, int width, int height, double framesPerSecond, int sampleRate = 0, int channels = 0)
    {
        if (!output.CanSeek) throw new ArgumentException("An AVI header is patched after the fact, so this has to seek.", nameof(output));
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "A frame needs both dimensions.");
        if (framesPerSecond <= 0d) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        if (channels < 0) throw new ArgumentOutOfRangeException(nameof(channels));
        if (channels > 0 && sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

        this.output = output;
        this.width = width;
        this.height = height;
        this.channels = channels;
        this.sampleRate = channels > 0 ? sampleRate : 0;

        FramesPerSecond = framesPerSecond;

        WriteHeaders();
    }

    public double FramesPerSecond { get; }

    /// <summary>Frames written so far.</summary>
    public int FrameCount => frames;

    /// <summary>Appends one encoded JPEG as the next frame.</summary>
    public void WriteFrame(ReadOnlySpan<byte> jpeg)
    {
        WriteChunk(VideoChunkId, jpeg);
        frames++;
    }

    /// <summary>
    /// Appends interleaved float samples as 16-bit PCM, converted the way
    /// <see cref="WavWriter"/> converts them.
    /// </summary>
    public void WriteAudio(ReadOnlySpan<float> interleaved)
    {
        if (channels == 0) throw new InvalidOperationException("This file was opened without an audio stream.");
        if (interleaved.Length == 0) return;

        var wanted = interleaved.Length * sizeof(short);
        if (pcm.Length < wanted) pcm = new byte[wanted];

        for (var i = 0; i < interleaved.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * sizeof(short)), WavWriter.ToPcm16(interleaved[i]));

        WriteChunk(AudioChunkId, pcm.AsSpan(0, wanted));
        audioSamples += interleaved.Length / channels;
    }

    /// <summary>Writes the index and fills in everything the header could not know up front.</summary>
    public void Dispose()
    {
        if (closed) return;
        closed = true;

        var moviEnd = output.Position;
        WriteIndex();

        // Read before the first Patch, because patching seeks and every length
        // below is measured against where the file actually ended.
        var end = output.Position;
        var seconds = frames / FramesPerSecond;

        Patch(4, (uint)(end - 8));                                     // RIFF size
        Patch(moviPosition - 4, (uint)(moviEnd - moviPosition + 4));   // 'movi' LIST size

        Patch(headerPosition + 4, seconds > 0d ? (uint)Math.Round(end / seconds) : 0u);
        Patch(headerPosition + 16, (uint)frames);
        Patch(headerPosition + 28, (uint)largestChunk);

        Patch(videoStreamPosition + 32, (uint)frames);
        Patch(videoStreamPosition + 36, (uint)largestChunk);

        if (channels > 0)
        {
            Patch(audioStreamPosition + 32, (uint)audioSamples);
            Patch(audioStreamPosition + 36, (uint)largestChunk);
        }

        output.Seek(end, SeekOrigin.Begin);
        output.Flush();
    }

    // --- chunks ------------------------------------------------------------------

    /// <summary>
    /// One chunk in the movi list, padded to an even length — RIFF is a
    /// word-aligned format and a decoder walking it by chunk header will read
    /// the pad byte as the start of the next fourcc if it is not there.
    /// </summary>
    private void WriteChunk(int chunkId, ReadOnlySpan<byte> data)
    {
        if (closed) throw new InvalidOperationException("This file has already been finished.");

        var position = output.Position;
        if (position + data.Length > MaximumBytes)
            throw new InvalidOperationException("An AVI cannot exceed 4 GB. Export a shorter clip, a smaller frame or a lower quality.");

        WriteInt32(chunkId);
        WriteUInt32((uint)data.Length);
        output.Write(data);

        if ((data.Length & 1) != 0) output.WriteByte(0);

        index.Add((chunkId, (uint)(position - moviPosition), (uint)data.Length));
        largestChunk = Math.Max(largestChunk, data.Length);
    }

    /// <summary>
    /// The idx1 table: where every chunk starts, measured from the 'movi' fourcc
    /// rather than from the file, which is the convention every reader assumes
    /// and nothing in the format states.
    /// </summary>
    private void WriteIndex()
    {
        WriteFourCc("idx1");
        WriteUInt32((uint)(index.Count * 16));

        foreach (var (chunkId, offset, size) in index)
        {
            WriteInt32(chunkId);

            // Every JPEG stands alone and audio has no such notion, so all of
            // them are key frames — which is the property that makes an MJPEG
            // file scrub instantly and is most of why it is still used.
            WriteUInt32(KeyFrame);
            WriteUInt32(offset);
            WriteUInt32(size);
        }
    }

    // --- headers -----------------------------------------------------------------

    /// <summary>A chunk's fourcc and its length field, ahead of the payload.</summary>
    private const int ChunkHeader = 8;

    /// <summary>The fixed size of an AVIStreamHeader.</summary>
    private const int StreamHeader = 56;

    // A stream's LIST holds its fourcc, a strh and a strf; the strf is a
    // BITMAPINFOHEADER for video and a WAVEFORMATEX for audio.
    private const int VideoList = 4 + ChunkHeader + StreamHeader + ChunkHeader + 40;
    private const int AudioList = 4 + ChunkHeader + StreamHeader + ChunkHeader + 18;

    private void WriteHeaders()
    {
        var streams = channels > 0 ? 2 : 1;

        WriteFourCc("RIFF");
        WriteUInt32(0);                                     // patched in Dispose
        WriteFourCc("AVI ");

        WriteFourCc("LIST");
        WriteUInt32((uint)(4
            + ChunkHeader + StreamHeader
            + ChunkHeader + VideoList
            + (channels > 0 ? ChunkHeader + AudioList : 0)));
        WriteFourCc("hdrl");

        WriteFourCc("avih");
        WriteUInt32(56);
        headerPosition = output.Position;

        WriteUInt32((uint)Math.Round(1_000_000d / FramesPerSecond));
        WriteUInt32(0);                                     // bytes per second, patched
        WriteUInt32(0);                                     // padding granularity
        WriteUInt32(HasIndex | IsInterleaved);
        WriteUInt32(0);                                     // total frames, patched
        WriteUInt32(0);                                     // initial frames
        WriteUInt32((uint)streams);
        WriteUInt32(0);                                     // suggested buffer, patched
        WriteUInt32((uint)width);
        WriteUInt32((uint)height);
        WriteUInt32(0);
        WriteUInt32(0);
        WriteUInt32(0);
        WriteUInt32(0);

        WriteVideoStream();
        if (channels > 0) WriteAudioStream();

        WriteFourCc("LIST");
        WriteUInt32(0);                                     // patched in Dispose
        moviPosition = output.Position;
        WriteFourCc("movi");
    }

    private void WriteVideoStream()
    {
        WriteFourCc("LIST");
        WriteUInt32(VideoList);
        WriteFourCc("strl");

        WriteFourCc("strh");
        WriteUInt32(StreamHeader);
        videoStreamPosition = output.Position;

        WriteFourCc("vids");
        WriteFourCc("MJPG");
        WriteUInt32(0);                                     // flags
        WriteUInt32(0);                                     // priority and language
        WriteUInt32(0);                                     // initial frames

        // A rate over a scale rather than a single number, so that the rates
        // that are not whole — 24000/1001 and its relatives — stay exact.
        WriteUInt32(1000);
        WriteUInt32((uint)Math.Round(FramesPerSecond * 1000d));

        WriteUInt32(0);                                     // start
        WriteUInt32(0);                                     // length, patched
        WriteUInt32(0);                                     // suggested buffer, patched
        WriteUInt32(0xFFFFFFFF);                            // default quality
        WriteUInt32(0);                                     // sample size: frames vary
        WriteInt16(0); WriteInt16(0); WriteInt16((short)width); WriteInt16((short)height);

        WriteFourCc("strf");
        WriteUInt32(40);

        WriteUInt32(40);                                    // BITMAPINFOHEADER size
        WriteUInt32((uint)width);
        WriteUInt32((uint)height);
        WriteInt16(1);                                      // planes
        WriteInt16(24);                                     // bits per pixel
        WriteFourCc("MJPG");
        WriteUInt32((uint)(width * height * 3));
        WriteUInt32(0); WriteUInt32(0);                     // pixels per metre
        WriteUInt32(0); WriteUInt32(0);                     // palette
    }

    private void WriteAudioStream()
    {
        var blockAlign = channels * sizeof(short);

        WriteFourCc("LIST");
        WriteUInt32(AudioList);
        WriteFourCc("strl");

        WriteFourCc("strh");
        WriteUInt32(StreamHeader);
        audioStreamPosition = output.Position;

        WriteFourCc("auds");
        WriteUInt32(1);                                     // PCM
        WriteUInt32(0);                                     // flags
        WriteUInt32(0);                                     // priority and language
        WriteUInt32(0);                                     // initial frames
        WriteUInt32(1);                                     // scale
        WriteUInt32((uint)sampleRate);                      // rate
        WriteUInt32(0);                                     // start
        WriteUInt32(0);                                     // length in samples, patched
        WriteUInt32(0);                                     // suggested buffer, patched
        WriteUInt32(0xFFFFFFFF);                            // default quality
        WriteUInt32((uint)blockAlign);
        WriteInt16(0); WriteInt16(0); WriteInt16(0); WriteInt16(0);

        WriteFourCc("strf");
        WriteUInt32(18);

        WriteInt16(1);                                      // WAVE_FORMAT_PCM
        WriteInt16((short)channels);
        WriteUInt32((uint)sampleRate);
        WriteUInt32((uint)(sampleRate * blockAlign));
        WriteInt16((short)blockAlign);
        WriteInt16(16);                                     // bits per sample
        WriteInt16(0);                                      // no extra format bytes
    }

    // --- primitives --------------------------------------------------------------

    private void Patch(long position, uint value)
    {
        output.Seek(position, SeekOrigin.Begin);
        WriteUInt32(value);
    }

    private void WriteFourCc(string code)
    {
        Span<byte> bytes = stackalloc byte[4];
        Encoding.ASCII.GetBytes(code, bytes);
        output.Write(bytes);
    }

    private void WriteInt32(int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        output.Write(bytes);
    }

    private void WriteUInt32(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        output.Write(bytes);
    }

    private void WriteInt16(short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
        output.Write(bytes);
    }
}
