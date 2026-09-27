namespace Flyback.Plugins.Assist;

/// <summary>A model package present in Flyback's per-user model directory.</summary>
public sealed class InstalledLayaModel(LayaModelDefinition definition, string directory)
{
    /// <summary>Model metadata describing the installed package.</summary>
    public LayaModelDefinition Definition { get; } = definition;

    /// <summary>Absolute path to the package directory.</summary>
    public string Directory { get; } = directory;

    /// <summary>The ONNX graph to pass to ONNX Runtime.</summary>
    public string OnnxPath => Path.Combine(
        Directory,
        Definition.OnnxPath.Replace('/', Path.DirectorySeparatorChar));
}
