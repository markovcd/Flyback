namespace Flyback.Plugins.Decide;

/// <summary>One file a model needs, and what it must hash to.</summary>
/// <param name="Address">Where the host downloads it from.</param>
/// <param name="Name">What it is called in the model's folder: a plain file name.</param>
/// <param name="Sha256">Its SHA-256, in hex. A file that hashes otherwise is refused.</param>
/// <param name="Size">Its length in bytes. A download longer than this is stopped.</param>
public sealed record ModelFile(Uri Address, string Name, string Sha256, long Size);
