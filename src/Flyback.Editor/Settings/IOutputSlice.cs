namespace Flyback.App.Settings;

/// <summary>
/// The rows of a settings section kept in <see cref="OutputSettings"/>, which
/// <see cref="OutputSettingsUse"/> shows and reads with every other section's
/// (ADR-0148).
/// </summary>
/// <remarks>
/// Picking a row is a draft until Save, and the controls are built once and kept
/// (ADR-0082).
/// </remarks>
internal interface IOutputSlice
{
    /// <summary>Puts the controls to <paramref name="settings"/>, and nothing else.</summary>
    void Show(OutputSettings settings);

    /// <summary>Writes what the controls hold into <paramref name="into"/>.</summary>
    /// <param name="before">What was last saved, which keeps whatever a control cannot say.</param>
    void Read(OutputSettings into, OutputSettings before);
}
