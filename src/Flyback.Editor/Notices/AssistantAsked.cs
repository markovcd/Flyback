namespace Flyback.Editor.Notices;

/// <summary>The assistant button was pressed: its column is wanted, or wanted away.</summary>
internal sealed record AssistantAsked(bool Shown) : INotice;
