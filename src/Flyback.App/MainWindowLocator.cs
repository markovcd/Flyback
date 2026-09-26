using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Flyback.App.Controls;

internal static class MainWindowLocator
{
    private static readonly List<Window> HeadlessWindows = [];

    public static Window Owner =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
        ?? HeadlessWindows.LastOrDefault()
        ?? throw new InvalidOperationException("The main window is not available.");

    internal static IDisposable RegisterForHeadlessTests(Window window)
    {
        HeadlessWindows.Add(window);
        return new HeadlessRegistration(window);
    }

    private sealed class HeadlessRegistration : IDisposable
    {
        private Window? registered;
        private readonly EventHandler closed;

        public HeadlessRegistration(Window window)
        {
            registered = window;
            closed = (_, _) => Dispose();
            window.Closed += closed;
        }

        public void Dispose()
        {
            if (registered is not { } current) return;

            HeadlessWindows.Remove(current);
            current.Closed -= closed;
            registered = null;
        }
    }
}