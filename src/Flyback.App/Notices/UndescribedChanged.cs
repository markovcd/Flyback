namespace Flyback.App.Notices;

/// <summary>The modules the assistant's briefing leaves out are a different set.</summary>
internal sealed record UndescribedChanged(IReadOnlySet<string> Types);
