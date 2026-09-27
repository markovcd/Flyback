namespace Flyback.Plugins.Assist;

/// <summary>A versioned local model package and the files needed to use it.</summary>
public sealed class LayaModelDefinition(
    string id,
    string name,
    string version,
    string onnxPath,
    IReadOnlyList<LayaModelArtifact> artifacts)
{
    /// <summary>Stable model identifier.</summary>
    public string Id { get; } = id;

    /// <summary>Human-readable model name.</summary>
    public string Name { get; } = name;

    /// <summary>Version or immutable revision.</summary>
    public string Version { get; } = version;

    /// <summary>Relative path of the ONNX graph within the package.</summary>
    public string OnnxPath { get; } = onnxPath;

    /// <summary>All files required by the checkpoint, including tokenizer/configuration files.</summary>
    public IReadOnlyList<LayaModelArtifact> Artifacts { get; } = artifacts;
}
