namespace Flyback.Editor.Notices;

/// <summary>The tutorials button was pressed: their column is wanted, or wanted away.</summary>
internal sealed record TutorialAsked(bool Shown) : INotice;
