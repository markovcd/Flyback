using Flyback.Engine.Graph;

namespace Flyback.Editor.Notices;

/// <summary>
/// A patch was refused for plugins it names and this run lacks. Whatever can offer
/// them from the site reacts, and opens <paramref name="Reopen"/> again once they are in.
/// </summary>
internal sealed record PluginsMissing(PatchLoad Load, Reopen? Reopen) : INotice;
