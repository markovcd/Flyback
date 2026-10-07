namespace Flyback.Plugins.Decide;

/// <summary>A decision model that needs files the host downloads for it before it can answer.</summary>
/// <remarks>
/// The plugin says what it needs; the host asks somebody, downloads and checks each
/// file's hash, so the plugin itself never reaches the network.
/// </remarks>
public interface IPreparedModel
{
    /// <summary>Every file the model reads, each pinned by its hash.</summary>
    IReadOnlyList<ModelFile> Needs { get; }

    /// <summary>Whether <paramref name="folder"/> holds everything in <see cref="Needs"/>. Only looks for the files.</summary>
    bool Prepared(string folder);
}
