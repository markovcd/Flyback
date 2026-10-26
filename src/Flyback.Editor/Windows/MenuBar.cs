using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Flyback.Core;
using Flyback.Editor.Bars;
using Flyback.Editor.Canvas;
using Flyback.Editor.Controls;
using Flyback.Editor.Notices;

namespace Flyback.Editor.Windows;

/// <summary>
/// The editor's menus: File, Edit, View, Play, Window and Help in the macOS menu bar,
/// and on Windows and Linux a bar along the top of the window, while the Canvas settings
/// ask for it.
/// </summary>
/// <remarks>
/// Off macOS the program's own items (About, Settings, Plugins) join File and Help, and
/// Window is left to the title bar. The bar shows a key without claiming it, so the
/// window still answers the key itself.
/// <para>
/// macOS hands a key with a menu shortcut to the menu and never to the window, so an
/// item with a key the editor already answers presses that key on whatever has the
/// focus: a text box copies its own text, the canvas its modules, exactly as the key
/// would have done. Such an item stays enabled, or macOS would swallow the key. A tick
/// or a greyed item follows the toolbar button it stands for.
/// </para>
/// </remarks>
internal sealed class MenuBar
{
    /// <summary>The key a menu shortcut is held with: Command on a Mac, Control elsewhere.</summary>
    internal static readonly KeyModifiers Command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    private static bool Mac => OperatingSystem.IsMacOS();

    private readonly WindowHolder holder;
    private readonly IDialog dialog;
    private readonly CanvasSection settings;

    private Window? window;
    private bool shown;

    /// <summary>What last had the focus outside the menus, which an item pressing a key presses it on.</summary>
    private IInputElement? worked;

    public NativeMenu Menu { get; } = new();

    /// <summary>The bar along the top of the window on Windows and Linux.</summary>
    public NativeMenuBar Bar { get; } = new() { Name = "menuBar", IsVisible = false };

    public MenuBar(Toolbar toolbar, StatusBar status, FullScreenPreview fullScreen, LivePatching live, Reactions reactions, WindowHolder holder, IDialog dialog, CanvasSection settings)
    {
        this.holder = holder;
        this.dialog = dialog;
        this.settings = settings;

        shown = settings.MenuBar;
        settings.MenuBarShown += value => Shown = value;

        Menu.Add(Titled("File",
            [
                Pressing("Open…", Key.O),
                Pressing("Save…", Key.S),
                .. Mac ? [] : (NativeMenuItemBase[])
                [
                    new NativeMenuItemSeparator(),
                    Asking("Settings…", () => reactions.Raise(new SettingsAsked())),
                    Asking("Plugins…", () => reactions.Raise(new PluginsAsked())),
                    new NativeMenuItemSeparator(),
                    Doing(OperatingSystem.IsWindows() ? "Exit" : "Quit", () => holder.Window.Close()),
                ],
            ]));

        Menu.Add(Titled("Edit",
            Pressing("Undo", Key.Z),
            Pressing("Redo", Key.Z, KeyModifiers.Shift),
            new NativeMenuItemSeparator(),
            Pressing("Cut", Key.X),
            Pressing("Copy", Key.C),
            Pressing("Paste", Key.V),
            Pressing("Duplicate", Key.D),
            Pressing("Select All", Key.A),
            new NativeMenuItemSeparator(),
            Doing("Add a Module…", () => reactions.Raise(new ModuleAsked())),
            Pressing("Group", Key.G),
            Pressing("Ungroup", Key.G, KeyModifiers.Shift),
            Pressing("Switch Off or On", Key.B),
            new NativeMenuItemSeparator(),
            Pressing("Lay Out the Modules", Key.L),
            Pressing("Lay Out the Selected Modules", Key.L, KeyModifiers.Shift)));

        Menu.Add(Titled("View",
            Following(toolbar.Code, Pressing("Patch as Text", Key.F2, KeyModifiers.None)),
            Following(toolbar.Knobs, Pressing("Knob Panel", Key.K)),
            Following(toolbar.Keys, Toggling("Keys on the Screen", toolbar.Keys)),
            Following(toolbar.Transport, Toggling("Transport Row", toolbar.Transport)),
            Following(toolbar.Side, Toggling("Preview and Inspector Column", toolbar.Side)),
            Following(toolbar.Swap, Toggling("Swap the Preview and the Canvas", toolbar.Swap)),
            Following(toolbar.Assistant, Toggling("Assistant", toolbar.Assistant)),
            new NativeMenuItemSeparator(),
            Pressing("Bring the Whole Patch into View", Key.F),
            Doing("Full-Screen Picture", fullScreen.Toggle, Mac ? new KeyGesture(Key.F, KeyModifiers.Meta | KeyModifiers.Control) : null),
            Doing("Live Patching", live.Toggle)));

        Menu.Add(Titled("Play",
            Pressing("Pause or Play", Key.P),
            Doing("Back to the Start", () => reactions.Raise(new RewindAsked())),
            new NativeMenuItemSeparator(),
            Pressing("Record", Key.R),
            Pressing("Measure", Key.M, KeyModifiers.Shift),
            new NativeMenuItemSeparator(),
            Pressing("Roll the Panel's Knobs", Key.K, KeyModifiers.Shift),
            Pressing("Roll the Selected Modules' Knobs", Key.D, KeyModifiers.Shift)));

        if (Mac)
        {
            Menu.Add(Titled("Window",
                Doing("Minimize", () => holder.Window.WindowState = WindowState.Minimized, new KeyGesture(Key.M, KeyModifiers.Meta)),
                Doing("Zoom", () => holder.Window.WindowState =
                    holder.Window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized)));
        }

        Menu.Add(Titled("Help",
            [
                Following(status.Tutorial, Toggling("Tutorials", status.Tutorial)),
                new NativeMenuItemSeparator(),
                Doing("Flyback Website", () => Launch(About.Website)),
                Doing("Write to the Author", () => Launch($"mailto:{About.Email}")),
                .. Mac ? [] : (NativeMenuItemBase[])
                [
                    new NativeMenuItemSeparator(),
                    Asking($"About {GlobalConstants.ApplicationName}", () => _ = About.Show(dialog)),
                ],
            ]));
    }

    /// <summary>Whether the menus are along the top of the window. A Mac shows them in its menu bar whatever this says.</summary>
    public bool Shown
    {
        get => shown;
        set
        {
            shown = value;
            Apply();
        }
    }

    /// <summary>Puts the menus on <paramref name="host"/>, the desktop window the editor is in.</summary>
    public void Hang(Window host)
    {
        window = host;
        settings.MenuBarOffered = !Mac;
        host.AddHandler(InputElement.GotFocusEvent, (_, e) =>
        {
            if (e.Source is IInputElement element && !InMenu(element)) worked = element;
        }, handledEventsToo: true);
        Apply();
    }

    private void Apply()
    {
        if (window is null) return;

        if (Mac)
        {
            NativeMenu.SetMenu(window, Menu);
            return;
        }

        // Not set while hidden, or a Linux desktop with a global menu would show it anyway.
        NativeMenu.SetMenu(window, shown ? Menu : null);
        Bar.IsVisible = shown;
    }

    private static NativeMenuItem Titled(string title, params NativeMenuItemBase[] items)
    {
        var menu = new NativeMenu();

        foreach (var item in items) menu.Add(item);

        return new NativeMenuItem(title) { Menu = menu };
    }

    /// <summary>An item that presses <paramref name="key"/>, with <see cref="Command"/> unless <paramref name="modifiers"/> says otherwise.</summary>
    private NativeMenuItem Pressing(string header, Key key, KeyModifiers? modifiers = null)
    {
        var held = modifiers ?? Command;

        if (held != KeyModifiers.None) held |= Command;

        var gesture = new KeyGesture(key, held);
        var item = new NativeMenuItem(header) { Gesture = gesture };

        item.Click += (_, _) => Press(gesture);

        return item;
    }

    private static NativeMenuItem Doing(string header, Action act, KeyGesture? gesture = null)
    {
        var item = new NativeMenuItem(header) { Gesture = gesture };

        item.Click += (_, _) => act();

        return item;
    }

    /// <summary>An item that does nothing while a dialog is up, as the toolbar under it cannot be clicked then.</summary>
    private NativeMenuItem Asking(string header, Action act) => Doing(header, () =>
    {
        if (!dialog.IsShowing) act();
    });

    private static NativeMenuItem Toggling(string header, ToggleButton button)
    {
        var item = new NativeMenuItem(header);

        item.Click += (_, _) => button.IsChecked = button.IsChecked != true;

        return item;
    }

    /// <summary>Ticks <paramref name="item"/> with <paramref name="button"/>, and greys or hides it with it unless it carries a key.</summary>
    private static NativeMenuItem Following(Control button, NativeMenuItem item)
    {
        var keyed = item.Gesture is not null;

        void Follow()
        {
            if (button is ToggleButton toggle)
            {
                item.ToggleType = MenuItemToggleType.CheckBox;
                item.IsChecked = toggle.IsChecked == true;
            }

            if (!keyed)
            {
                item.IsEnabled = button.IsEnabled;
                item.IsVisible = button.IsVisible;
            }
        }

        Follow();
        button.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty || e.Property == InputElement.IsEnabledProperty || e.Property == Visual.IsVisibleProperty)
                Follow();
        };

        return item;
    }

    /// <summary>
    /// Raises <paramref name="gesture"/> as a key going down on whatever has the focus, or the window;
    /// on what had it before the menu, when the menu took it.
    /// </summary>
    private void Press(KeyGesture gesture)
    {
        var top = holder.Instance;
        var focused = top.FocusManager?.GetFocusedElement();

        if (focused is not null && InMenu(focused)) focused = worked;

        var target = focused as Interactive ?? top;

        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = gesture.Key,
            KeyModifiers = gesture.KeyModifiers,
            Source = target,
        });
    }

    private static bool InMenu(IInputElement element) =>
        element is MenuItem || (element as ILogical)?.FindLogicalAncestorOfType<MenuBase>() is not null;

    private void Launch(string uri) => _ = holder.Instance.Launcher.LaunchUriAsync(new Uri(uri));
}
