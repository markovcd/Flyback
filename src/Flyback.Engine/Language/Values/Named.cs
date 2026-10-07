namespace Flyback.Engine.Language.Values;

/// <summary>A file a module names rather than carries (ADR-0052).</summary>
internal sealed record Named(string Path) : Value;
