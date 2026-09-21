using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Flyback.App;

/// <summary>Which monitor a window is on, and putting it back on the one it was left on.</summary>
internal static class MonitorPlacement
{
    /// <summary>A monitor as the layout file keeps it, or null for none.</summary>
    internal static MonitorSpot? Describe(Screen? screen) => screen is null
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
    internal static void Return(Window window, MonitorSpot? wanted)
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
    /// The monitor the full-screen picture goes to, or null for the window's own,
    /// which is also the answer when the one asked for is not plugged in.
    /// </summary>
    internal static Screen? FullScreenTarget(Window window, FullScreenOn on, MonitorSpot? chosen)
    {
        if (window.Screens.ScreenFromWindow(window) is not { } here) return null;

        var all = window.Screens.All;
        var at = Pick(on, chosen, [.. all.Select(s => Describe(s)!)], Index(all, here));

        return at is { } row && !Same(all[row], here) ? all[row] : null;
    }

    /// <summary>
    /// Which of <paramref name="all"/> the picture goes to, given the window is on
    /// row <paramref name="here"/>. Another monitor is the leftmost that is not the
    /// window's; the chosen one is found as the layout's is.
    /// </summary>
    internal static int? Pick(FullScreenOn on, MonitorSpot? chosen, IReadOnlyList<MonitorSpot> all, int here) => on switch
    {
        FullScreenOn.OtherMonitor => Enumerable.Range(0, all.Count)
            .Where(row => row != here)
            .OrderBy(row => all[row].X)
            .ThenBy(row => all[row].Y)
            .Cast<int?>()
            .FirstOrDefault() ?? here,
        FullScreenOn.ChosenMonitor => (chosen is null ? null : Find(chosen, all)) ?? here,
        _ => here,
    };

    /// <summary>
    /// The row of <paramref name="all"/> <paramref name="wanted"/> describes: the same
    /// name and place if there is one, else the same place, else the same name when
    /// it is the only one.
    /// </summary>
    internal static int? Find(MonitorSpot wanted, IReadOnlyList<MonitorSpot> all)
    {
        var name = string.IsNullOrEmpty(wanted.Name) ? null : wanted.Name;

        bool Place(MonitorSpot s) =>
            s.X == wanted.X && s.Y == wanted.Y && s.Width == wanted.Width && s.Height == wanted.Height;

        bool Named(MonitorSpot s) => name is not null && s.Name == name;

        return Only(s => Named(s) && Place(s)) ?? Only(Place) ?? Only(Named);

        int? Only(Func<MonitorSpot, bool> test)
        {
            var rows = Enumerable.Range(0, all.Count).Where(row => test(all[row])).Take(2).ToList();
            return rows.Count == 1 ? rows[0] : null;
        }
    }

    private static Screen? Find(MonitorSpot wanted, IReadOnlyList<Screen> all) =>
        Find(wanted, [.. all.Select(s => Describe(s)!)]) is { } row ? all[row] : null;

    private static int Index(IReadOnlyList<Screen> all, Screen screen)
    {
        for (var row = 0; row < all.Count; row++)
            if (Same(all[row], screen)) return row;

        return 0;
    }

    internal static bool Same(Screen a, Screen b) => a.Bounds == b.Bounds && a.DisplayName == b.DisplayName;
}
