namespace Flyback.Plugins.Assist;

/// <summary>Finds, installs, lists and removes local Laya model packages.</summary>
/// <remarks>
/// Implementations store packages in Flyback's per-user application data
/// directory (<see cref="Environment.SpecialFolder.LocalApplicationData"/>),
/// at <c>Flyback/Laya</c> beneath that directory, rather than beside the
/// executable or in a patch bundle. They verify every artifact's digest and
/// reject artifact paths that escape the package directory before making the
/// package available.
/// </remarks>
public interface ILayaModelManager
{
    /// <summary>Models that can be installed by this manager.</summary>
    IReadOnlyList<LayaModelDefinition> AvailableModels { get; }

    /// <summary>Models currently installed for this user.</summary>
    IReadOnlyList<InstalledLayaModel> InstalledModels { get; }

    /// <summary>Downloads and verifies every artifact before making the model available.</summary>
    Task<InstalledLayaModel> InstallAsync(
        string modelId,
        IProgress<LayaModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes an installed model and its files.</summary>
    Task RemoveAsync(string modelId, CancellationToken cancellationToken = default);
}
