namespace Flyback.App.Notices;

/// <summary>A module was asked for from the bar, to be put in the middle of the view.</summary>
/// <param name="ByFinger">Whether the button was tapped on a touch screen.</param>
internal sealed record ModuleAsked(bool ByFinger = false);
