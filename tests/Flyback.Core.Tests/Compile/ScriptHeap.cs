using System.Runtime.InteropServices;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// A heap laid out for an emitted script to run on, standing in for a browser's: each
/// array placed at an aligned offset, and only the ones holding something written out.
/// </summary>
/// <remarks>
/// A program's memory starts at zero, and a Scope's ring alone is megabytes, so what
/// is saved is the size and the stretches that are not zero.
/// </remarks>
internal sealed class ScriptHeap
{
    private readonly List<(long At, byte[] Bytes)> filled = [];

    public long Size { get; private set; } = 16;

    /// <summary>Makes room for <paramref name="array"/> and answers its byte address, keeping its contents where there are any.</summary>
    public long Place(Array array)
    {
        var size = Buffer.ByteLength(array);
        var at = Size;

        Size = (at + size + 7) / 8 * 8;

        var bytes = new byte[size];
        Buffer.BlockCopy(array, 0, bytes, 0, size);

        if (bytes.Any(b => b != 0)) filled.Add((at, bytes));

        return at;
    }

    /// <summary>Makes room for <paramref name="count"/> floats and answers where they start, as an element index.</summary>
    public long Floats(int count)
    {
        var at = Size;
        Size = (at + count * sizeof(float) + 7) / 8 * 8;
        return at / sizeof(float);
    }

    /// <summary>Writes the size and the filled stretches: an eight-byte size, then offset, length and bytes for each.</summary>
    public void Save(string path)
    {
        using var file = new BinaryWriter(File.Create(path));

        file.Write(Size);

        foreach (var (at, bytes) in filled)
        {
            file.Write(at);
            file.Write(bytes.Length);
            file.Write(bytes);
        }
    }

    /// <summary>Reads <paramref name="count"/> floats back out of a file the script wrote.</summary>
    public static float[] ReadFloats(string path, int count) =>
        MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(path)).Slice(0, count).ToArray();
}
