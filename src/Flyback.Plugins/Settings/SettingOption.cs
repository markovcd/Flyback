namespace Flyback.Plugins.Settings;

/// <summary>
/// One entry of an <see cref="SettingField.Pick"/>: the id that is stored, and
/// the name a person reads. Kept apart for the reason a patch keeps them apart —
/// what is written down has to go on meaning the same thing after somebody
/// rewords the label.
/// </summary>
/// <param name="Id">Stable, and what ends up in the settings file.</param>
/// <param name="Name">What the picker shows.</param>
public readonly record struct SettingOption(string Id, string Name);