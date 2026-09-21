using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Flyback.App;

/// <summary>Which monitor a window is on, and putting it back on the one it was left on.</summary>
internal static class MonitorPlacement
{
    /// <summary>A monitor as the layout file keeps it, or null for none.</summary>
    internal static WindowLayout.MonitorSpot? Describe(Screen? screen) => screen is null
        ? null
        : new()
        {
            Name = screen.DisplayName,
            X = screen.Bounds.X,
            Y = screen.Bounds.Y,
            Width = screen.Bounds.Width,
            Height = screen.Bounds.Height,
        };

    /// <summary>
    /// Moves the window to the monitor it was left on, if the platform put it
    /// somewhere else, and keeps it inside that monitor's usable area.
    /// </summary>
    /// <remarks>
    /// The offset from the corner of its monitor is kept rather than the window
    /// being centered, so copies the platform cascaded stay cascaded.
    /// </remarks>
    internal static void Return(Window window, WindowLayout.MonitorSpot? wanted)
    {
        if (window.Screens.ScreenFromWindow(window) is not { } here) return;

        var target = (wanted is null ? null : Find(wanted, window.Screens.All)) ?? here;

        var state = window.WindowState;

        if (Same(target, here))
        {
            if (state == WindowState.Normal) Fit(window, target, window.Position);
            return;
        }

        // A maximized window does not move between monitors.
        if (state != WindowState.Normal) window.WindowState = WindowState.Normal;

        Fit(window, target, target.WorkingArea.Position + (window.Position - here.WorkingArea.Position));

        if (state != WindowState.Normal) window.WindowState = state;
    }

    /// <summary>Puts the window at <paramref name="at"/>, shrunk and shifted to fit inside <paramref name="screen"/>.</summary>
    private static void Fit(Window window, Screen screen, PixelPoint at)
    {
        var area = screen.WorkingArea;
        var scale = screen.Scaling;

        var width = Math.Min(window.Width, area.Width / scale);
        var height = Math.Min(window.Height, area.Height / scale);

        if (width < window.Width) window.Width = Math.Max(width, window.MinWidth);
        if (height < window.Height) window.Height = Math.Max(height, window.MinHeight);

        var wide = (int)Math.Ceiling(window.Width * scale);
        var tall = (int)Math.Ceiling(window.Height * scale);

        window.Position = new PixelPoint(
            Math.Clamp(at.X, area.X, Math.Max(area.X, area.Right - wide)),
            Math.Clamp(at.Y, area.Y, Math.Max(area.Y, area.Bottom - tall)));
    }

    /// <summary>
    /// The monitor <paramref name="wanted"/> describes: the same name and place if
    /// there is one, else the same place, else the same name when it is the only one.
    /// </summary>
    private static Screen? Find(WindowLayout.MonitorSpot wanted, IReadOnlyList<Screen> all)
    {
        var name = string.IsNullOrEmpty(wanted.Name) ? null : wanted.Name;

        bool Place(Screen s) =>
            s.Bounds.X == wanted.X && s.Bounds.Y == wanted.Y
            && s.Bounds.Width == wanted.Width && s.Bounds.Height == wanted.Height;

        var both = all.Where(s => name is not null && s.DisplayName == name && Place(s)).ToList();
        if (both.Count == 1) return both[0];

        var places = all.Where(Place).ToList();
        if (places.Count == 1) return places[0];

        var names = all.Where(s => name is not null && s.DisplayName == name).ToList();
        return names.Count == 1 ? names[0] : null;
    }

    private static bool Same(Screen a, Screen b) => a.Bounds == b.Bounds && a.DisplayName == b.DisplayName;
}
