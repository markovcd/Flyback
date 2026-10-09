namespace Flyback.Editor.Notices;

/// <summary>The transport button was pressed: the row that plays the patch is wanted, or wanted away.</summary>
internal sealed record TransportAsked(bool Shown) : INotice;
