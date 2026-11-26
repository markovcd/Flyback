namespace Flyback.Engine.Packs;

/// <summary>A pack module's stencils, one for each way its choices combine, built when the pack loads.</summary>
/// <param name="Choices">The settings' names, in order.</param>
/// <param name="Defaults">What each setting holds on a module placed fresh.</param>
internal sealed record PackStencils(IReadOnlyList<string> Choices, IReadOnlyList<string> Defaults, IReadOnlyDictionary<string, PackStencil> Built)
{
    /// <summary>The stencil for <paramref name="chosen"/>; a value that is none of a setting's choices takes its default.</summary>
    public PackStencil At(IReadOnlyList<string> chosen) =>
        Built.TryGetValue(Key(chosen), out var stencil) ? stencil : Built[Key(Defaults)];

    /// <summary>One setting's values joined into a key no choice can be confused with.</summary>
    public static string Key(IReadOnlyList<string> chosen) => string.Join('\u0001', chosen);
}
