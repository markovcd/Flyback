namespace Flyback.Core.Graph;

/// <summary>
/// A catalog with a provider folded in, and anything that provider was
/// refused. Refusals are values rather than exceptions: one bad module in a
/// plugin should cost that module, not the plugin and not the program.
/// </summary>
internal sealed record ModuleAddition(ModuleCatalog Catalog, IReadOnlyList<string> Rejected);