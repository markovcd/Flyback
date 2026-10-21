using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Flyback.Editor.Bars;
using Flyback.Editor.Controls;
using Flyback.Editor.Notices;

namespace Flyback.Editor.Windows;

/// <summary>
/// The editor's menus in the macOS menu bar: File, Edit, View, Play, Window and Help.
/// </summary>
/// <remarks>
/// macOS hands a key with a menu shortcut to the menu and never to the window, so an
/// item with a key the editor already answers presses that key on whatever has the
/// focus: a text box copies its own text, the canvas its modules, exactly as the key
/// would have done. Such an item stays enabled, or macOS would swallow the key. A tick
/// or a greyed item follows the toolbar button it stands for.
/// </remarks>
internal sealed class MenuBar
{
    private readonly WindowHolder holder;

    public NativeMenu Menu { get; } = new();

    public MenuBar(Toolbar toolbar, StatusBar status, FullScreenPreview fullScreen, Reactions reactions, WindowHolder holder)
    {
        this.holder = holder;

        Menu.Add(Titled("File",
            Pressing("Open…", Key.O),
            Pressing("Save…", Key.S)));

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
            Doing("Full-Screen Picture", fullScreen.Toggle, new KeyGesture(Key.F, KeyModifiers.Meta | KeyModifiers.Control))));

        Menu.Add(Titled("Play",
            Pressing("Pause or Play", Key.P),
            Doing("Back to the Start", () => reactions.Raise(new RewindAsked())),
            new NativeMenuItemSeparator(),
            Pressing("Record", Key.R),
            Pressing("Measure", Key.M, KeyModifiers.Shift),
            new NativeMenuItemSeparator(),
            Pressing("Roll the Panel's Knobs", Key.K, KeyModifiers.Shift),
            Pressing("Roll the Selected Modules' Knobs", Key.D, KeyModifiers.Shift)));

        Menu.Add(Titled("Window",
            Doing("Minimize", () => holder.Window.WindowState = WindowState.Minimized, new KeyGesture(Key.M, KeyModifiers.Meta)),
            Doing("Zoom", () => holder.Window.WindowState =
                holder.Window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized)));

        Menu.Add(Titled("Help",
            Following(status.Tutorial, Toggling("Tutorials", status.Tutorial)),
            new NativeMenuItemSeparator(),
            Doing("Flyback Website", () => Launch(About.Website)),
            Doing("Write to the Author", () => Launch($"mailto:{About.Email}"))));
    }

    private static NativeMenuItem Titled(string title, params NativeMenuItemBase[] items)
    {
        var menu = new NativeMenu();

        foreach (var item in items) menu.Add(item);

        return new NativeMenuItem(title) { Menu = menu };
    }

    /// <summary>An item that presses <paramref name="key"/>, with Command unless <paramref name="modifiers"/> says otherwise.</summary>
    private NativeMenuItem Pressing(string header, Key key, KeyModifiers modifiers = KeyModifiers.Meta)
    {
        if (modifiers != KeyModifiers.None) modifiers |= KeyModifiers.Meta;

        var gesture = new KeyGesture(key, modifiers);
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

    /// <summary>Raises <paramref name="gesture"/> as a key going down on whatever has the focus, or the window.</summary>
    private void Press(KeyGesture gesture)
    {
        var top = holder.Instance;
        var target = top.FocusManager?.GetFocusedElement() as Interactive ?? top;

        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = gesture.Key,
            KeyModifiers = gesture.KeyModifiers,
            Source = target,
        });
    }

    private void Launch(string uri) => _ = holder.Instance.Launcher.LaunchUriAsync(new Uri(uri));
}
