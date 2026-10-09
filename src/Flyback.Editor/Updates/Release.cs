namespace Flyback.Editor.Updates;

/// <summary>A published release, as much of it as installing one needs.</summary>
/// <param name="Version">What the release's tag names, as major.minor.patch.</param>
/// <param name="PackageName">The package for this platform, as the signed checksums name it.</param>
internal sealed record Release(Version Version, string PackageName, Uri Package, Uri Checksums, Uri Signature);
