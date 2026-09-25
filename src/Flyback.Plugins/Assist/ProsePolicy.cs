using Flyback.Core.Graph;

namespace Flyback.Plugins.Assist;

/// <summary>
/// How much of the module catalog's prose the assistant is told, and whose
/// prose it keeps when that is not all of it.
/// </summary>
/// <remarks>
/// Every description goes in while the briefing fits <paramref name="Budget"/>
/// characters. Past that, <paramref name="Priority"/> keeps its descriptions, the
/// rest fill what is left in catalog order, and <c>describe_module</c> covers the
/// ones left out. It depends on the catalog alone, so the canvas can mark the
/// same modules and the briefing stays byte-stable (<see cref="Handbook"/>).
/// Presets have their own reserved room.
/// </remarks>
/// <param name="Budget">The most characters the whole briefing may run to.</param>
/// <param name="Priority">Type ids whose descriptions are never left out.</param>
public sealed record ProsePolicy(int Budget, IReadOnlySet<string> Priority)
{
    /// <summary>The shipped budget and the shipped list, for anything that has no settings.</summary>
    public static ProsePolicy Default { get; } =
        new(AssistantSettings.DefaultProseBudget, PriorityModules.Parse(PriorityModules.Shipped));

    /// <summary>
    /// The type ids whose descriptions the briefing leaves out, which is none at all
    /// while everything fits. A module with no description is never among them:
    /// there was nothing to leave out.
    /// </summary>
    public IReadOnlySet<string> Undescribed(ModuleCatalog modules) => Handbook.Undescribed(modules, this);
}
