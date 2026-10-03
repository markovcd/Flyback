using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Inspect;

/// <summary>
/// What the patch is for: text that a click turns into a box with Save and Cancel
/// under it, standing exactly where the text stood.
/// </summary>
internal sealed class PatchDescription(Func<Patch> patch, Document document, Action finished)
{
    /// <summary>How far the text keeps in from the edge of the button or box that holds it.</summary>
    private static readonly Thickness Inset = new(10, 8);

    private const double Size = Text.Heading;
    private const double LineHeight = Size * 1.45;
    private const int Rows = 3;

    /// <summary>The description as text, or the prompt for one; a plain line where the text owns the patch.</summary>
    internal Control Shown()
    {
        var description = patch().Description;

        var text = new TextBlock
        {
            Name = "patch-description",
            Text = description ?? "Add a description",
            TextWrapping = TextWrapping.Wrap,
            FontSize = Size,
            LineHeight = LineHeight,
            FontStyle = description is null ? FontStyle.Italic : FontStyle.Normal,
            Foreground = description is null ? Text.Muted : new SolidColorBrush(Colors.Label),
        };

        if (document.IsAdrift)
        {
            text.IsVisible = description is not null;
            return text;
        }

        var fill = new SolidColorBrush(Colors.Toolbar);

        var button = new Button
        {
            Name = "patch-description-button",
            Content = text,
            Margin = new Thickness(-Inset.Left, -Inset.Top),
            Padding = Inset,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Ibeam),
        };

        button.Resources["ButtonBackgroundPointerOver"] = fill;
        button.Resources["ButtonBackgroundPressed"] = fill;

        button.Click += (_, _) => Editing(button);

        return button;
    }

    private void Editing(Control shown)
    {
        var fill = new SolidColorBrush(Colors.Toolbar);

        // Border and padding add up to the inset, so the text does not move.
        var box = new TextBox
        {
            Name = "patch-description-box",
            Text = patch().Description ?? "",
            PlaceholderText = "What does this patch do?",
            MaxLength = Patch.DescriptionLimit,
            TextWrapping = TextWrapping.Wrap,
            FontSize = Size,
            LineHeight = LineHeight,
            Margin = new Thickness(-Inset.Left, -Inset.Top, -Inset.Left, 0),
            Padding = new Thickness(Inset.Left - 1, Inset.Top - 1),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            CornerRadius = new CornerRadius(10),
            MinHeight = Rows * LineHeight + 2 * Inset.Top,
            Background = fill,
        };

        box.Resources["TextControlBackground"] = fill;
        box.Resources["TextControlBackgroundPointerOver"] = fill;
        box.Resources["TextControlBackgroundFocused"] = fill;

        var save = new Button
        {
            Name = "patch-description-save",
            Content = "Save",
            Height = 36,
            Padding = new Thickness(16, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(8),
            FontWeight = FontWeight.SemiBold,
            Background = new SolidColorBrush(Colors.Source),
            Foreground = new SolidColorBrush(Colors.Window),
        };

        var cancel = new Button
        {
            Name = "patch-description-cancel",
            Content = "Cancel",
            Height = 36,
            Padding = new Thickness(14, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Colors.Toolbar),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(1),
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, save } };
        var hint = Text.Quiet("Enter saves · Esc cancels", Text.Small);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(0, 0, 8, 0);

        var below = new DockPanel { Children = { hint, buttons } };
        DockPanel.SetDock(buttons, Dock.Right);

        var editor = new StackPanel { Name = "patch-description-editor", Spacing = 10, Children = { box, below } };

        var closed = false;

        void Close(bool keep)
        {
            if (closed) return;
            closed = true;

            var before = patch().Description;
            if (keep) patch().Describe(box.Text);
            if (patch().Description != before) finished();

            if (editor.Parent is Panel host && host.Children.IndexOf(editor) is var at and >= 0) host.Children[at] = Shown();
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;

            e.Handled = true;
            Close(keep: e.Key == Key.Enter);
        };

        save.Click += (_, _) => Close(keep: true);
        cancel.Click += (_, _) => Close(keep: false);

        // Leaving the box for anywhere but its own buttons keeps what was typed, as a
        // selection change would otherwise rebuild the panel and lose it.
        editor.LostFocus += (_, _) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (!closed && !editor.IsKeyboardFocusWithin) Close(keep: true);
            });

        if (shown.Parent is Panel parent && parent.Children.IndexOf(shown) is var where and >= 0)
        {
            parent.Children[where] = editor;
            box.Focus();
            box.CaretIndex = box.Text?.Length ?? 0;
        }
    }
}
