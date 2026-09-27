namespace Flyback.Cli.Models;

/// <summary>
/// What to ask, and where the answer goes.
/// </summary>
/// <param name="Provider">Which assistant, or null for whichever the settings or the catalog prefer.</param>
/// <param name="Only">Models to ask about by name, or empty for the provider's own shortlist.</param>
/// <param name="Dry">Print what was found and write none of it down.</param>
/// <param name="Keys">
/// Say where each provider's key would come from and ask nothing. The one thing here
/// that costs nothing, which is the point: whether a key is found is the question
/// everything else depends on.
/// </param>
/// <param name="Yes">
/// Start without asking first. For a script, which has nobody to answer the question
/// — and which is the only reason a command that spends money on being run has a way
/// past it.
/// </param>
internal sealed record ProbeOptions(
    string? Provider,
    IReadOnlyList<string> Only,
    bool All,
    bool Bounds,
    bool Dry,
    bool Json,
    bool Keys = false,
    bool Yes = false);