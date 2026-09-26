namespace Flyback.Plugins.Hosting;

/// <summary>How much a package may ask of the disk and the memory reading it.</summary>
/// <param name="Packed">The package itself, which is held in memory whole.</param>
/// <param name="Unpacked">Every file in it together, counted as it is written rather than as the zip says.</param>
internal sealed record PackageLimits(long Packed, long Unpacked, int Entries)
{
    public static PackageLimits Default { get; } = new(128L << 20, 512L << 20, 4096);
}