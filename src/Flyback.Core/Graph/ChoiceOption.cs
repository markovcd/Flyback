namespace Flyback.Core.Graph;

/// <summary>
/// One entry of a <see cref="ExtraField.Choice"/>: the id a patch stores, and the
/// name a person reads. Kept apart so a saved patch goes on meaning the same
/// thing when a device is renamed or moved to another port.
/// </summary>
/// <param name="Id">Stable, and what ends up in the file.</param>
/// <param name="Name">What the picker shows.</param>
public readonly record struct ChoiceOption(string Id, string Name);