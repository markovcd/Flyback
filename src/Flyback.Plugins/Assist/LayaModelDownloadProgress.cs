namespace Flyback.Plugins.Assist;

/// <summary>Progress for one model artifact download.</summary>
public sealed class LayaModelDownloadProgress(
    string modelId,
    string artifactPath,
    long bytesReceived,
    long? totalBytes)
{
    /// <summary>Model being installed.</summary>
    public string ModelId { get; } = modelId;

    /// <summary>Relative path of the artifact being downloaded.</summary>
    public string ArtifactPath { get; } = artifactPath;

    /// <summary>Bytes received for this artifact.</summary>
    public long BytesReceived { get; } = bytesReceived;

    /// <summary>Expected artifact size, when the server reports it.</summary>
    public long? TotalBytes { get; } = totalBytes;
}
