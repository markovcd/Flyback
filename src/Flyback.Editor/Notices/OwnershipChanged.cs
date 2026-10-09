namespace Flyback.Editor.Notices;

/// <summary>Who owns the patch has changed, or which view is showing has.</summary>
internal sealed record OwnershipChanged(bool Owned, bool ShowingCode) : INotice;
