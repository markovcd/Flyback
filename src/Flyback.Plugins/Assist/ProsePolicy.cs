using Flyback.Core.Graph;

namespace Flyback.Plugins.Assist;

/// <summary>
/// How much of the module catalogue's prose the assistant is told, and whose
/// prose it keeps when that is not all of it.
/// </summary>
/// <remarks>
/// Every module's description goes into the briefing while the whole briefing fits
/// in <paramref name="Budget"/> characters. Past that, the modules in
/// <paramref name="Priority"/> keep theirs whatever it costs, the rest keep theirs
/// in catalogue order for as long as they fit in what is left, and the assistant is
/// handed <c>describe_module</c> to look up the ones that did not. Filled rather
/// than cut off, so one plugin too many costs the descriptions it does not have
/// room for and no others.
/// <para>
/// Decided by the catalogue and nothing else, so the canvas can mark the same
/// modules the briefing leaves out without a conversation to ask, and the briefing
/// stays the same bytes from one request to the next (see <see cref="Handbook"/>).
/// </para>
/// <para>
/// The list of presets has room of its own, set aside before any of this, and takes
/// whatever the modules leave: descriptions while they fit, then names while they do.
/// </para>
/// <para>
/// The briefing runs over <paramref name="Budget"/> only by what cannot be cut: every
/// module's header and sockets, the priority descriptions and the presets' notes.
/// </para>
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
