using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;

namespace Flyback.App.Windows;

/// <summary>
/// Leaving the window as it was left: its size, state and monitor read from a file
/// and written back to it (ADR-0121). What the panels are set to is the window's own
/// to apply and to capture.
/// </summary>
internal sealed class WindowLayoutKeeper(EditorFolders folders)
{
    /// <summary>The layout read by <see cref="Load"/>, then the one last written. Null where none has been kept.</summary>
    public WindowLayout? Saved { get; private set; }

    /// <summary>The last client size the window had while it was neither maximized nor full screen.</summary>
    public Size? NormalSize { get; private set; }

    /// <summary>Reads the file, if there is one to read.</summary>
    public void Load()
    {
        if (folders.LayoutPath is not null) Saved = WindowLayout.Load(folders.LayoutPath);
    }

    /// <summary>Follows the size <paramref name="window"/> is dragged to.</summary>
    public void Track(Avalonia.Controls.Window window) =>
        // Only a drag of the frame: maximizing resizes the window too, and that is
        // not a size to come back to.
        window.Resized += (_, e) =>
        {
            if (e.Reason == WindowResizeReason.User && window.WindowState == WindowState.Normal) NormalSize = e.ClientSize;
        };

    /// <summary>Puts <paramref name="window"/>'s size, state and monitor back. Before it is shown.</summary>
    public void Apply(Avalonia.Controls.Window window)
    {
        if (Saved is not { } saved) return;

        if (saved.Width > 0 && saved.Height > 0)
        {
            window.Width = Math.Max(saved.Width, window.MinWidth);
            window.Height = Math.Max(saved.Height, window.MinHeight);
        }

        NormalSize = new Size(window.Width, window.Height);

        if (saved.Maximized) window.WindowState = WindowState.Maximized;

        // The platform places the window, so which monitor it chose is only known
        // once it is up.
        window.Opened += (_, _) => MonitorPlacement.Return(window, saved.Monitor);
    }

    /// <summary>Writes down what <paramref name="capture"/> says. A settings file is not worth a failure to close.</summary>
    public void Remember(Func<WindowLayout> capture)
    {
        if (folders.LayoutPath is not { } path) return;

        try
        {
            Saved = capture();
            Saved.Save(path);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not save the window layout: {ex.Message}");
        }
    }
}
