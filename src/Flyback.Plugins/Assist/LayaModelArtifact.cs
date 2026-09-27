namespace Flyback.Plugins.Assist;

/// <summary>A file in a model package, pinned to a SHA-256 digest.</summary>
public sealed class LayaModelArtifact(string path, Uri source, string sha256)
{
    /// <summary>Safe relative path beneath the model package directory.</summary>
    public string Path { get; } = path;

    /// <summary>HTTPS location of this artifact.</summary>
    public Uri Source { get; } = source;

    /// <summary>Expected SHA-256 digest, as 64 hexadecimal characters.</summary>
    public string Sha256 { get; } = sha256;
}
