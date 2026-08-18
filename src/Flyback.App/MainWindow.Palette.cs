using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.App.Controls;
using Flyback.Core.Graph;

namespace Flyback.App;

/// <summary>
/// The list of modules on the left, and the two controls that decide which of
/// them are in it: a filter, and a tick per plugin.
/// </summary>
/// <remarks>
/// A loop over the catalogue rather than markup, which is what makes a module
/// added anywhere — the engine or a plugin — appear here with no change to the
/// shell at all. Nothing in this file names a single module.
/// </remarks>
public sealed partial class MainWindow
{
    private Control BuildPalette()
    {
        filter.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) FillPalette();
        };

        // Escape empties it, which is the quickest way back to the whole list
        // once you have found the module you were after.
        filter.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;

            filter.Text = string.Empty;
            e.Handled = true;
        };

        var ticks = new StackPanel { Spacing = 2, Margin = new Thickness(4) };

        foreach (var provider in Providers)
        {
            var id = provider.Id;
            var tick = new CheckBox { Content = provider.Name, IsChecked = true, FontSize = 12 };

            tick.IsCheckedChanged += (_, _) =>
            {
                if (tick.IsChecked == true) hidden.Remove(id); else hidden.Add(id);

                DescribeSources();
                FillPalette();
            };

            ticks.Children.Add(tick);
        }

        sources.Flyout = new Flyout { Content = ticks, Placement = PlacementMode.BottomEdgeAlignedLeft };

        DescribeSources();
        FillPalette();

        var header = new StackPanel { Spacing = 6, Margin = new Thickness(10, 8, 10, 0) };
        header.Children.Add(filter);

        // With nothing installed there is only the engine's own entry, and a
        // dropdown that can only say "all" or "the only one" is noise.
        if (Providers.Count > 1) header.Children.Add(sources);

        // The header stays put while the list beneath it scrolls; a filter that
        // scrolled away with the results would be the wrong way round.
        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);

        panel.Children.Add(header);
        panel.Children.Add(new ScrollViewer { Content = modules });

        return new Border
        {
            Background = new SolidColorBrush(Colours.Panel),
            Child = panel,
        };
    }

    /// <summary>
    /// Rebuilds the module list for whatever is in the filter box. Rebuilding
    /// rather than hiding buttons keeps the category headings honest: a heading
    /// with nothing under it is worse than no heading.
    /// </summary>
    private void FillPalette()
    {
        var text = filter.Text?.Trim() ?? string.Empty;
        var matches = NodeCatalog.All.Where(d => Matches(d, text)).ToList();

        modules.Children.Clear();

        if (matches.Count == 0)
        {
            modules.Children.Add(Hint(
                hidden.Count == Providers.Count ? "No plugins are ticked."
                : text.Length == 0 ? "Nothing to show."
                : $"Nothing matches “{text}”."));
            return;
        }

        if (text.Length == 0 && hidden.Count == 0)
            modules.Children.Add(Hint("Click to drop a module into the middle of the patch."));

        foreach (var category in matches.Select(d => d.Category).Distinct())
        {
            modules.Children.Add(new TextBlock
            {
                Text = category.ToUpperInvariant(),
                FontSize = 10.5,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Colours.Accent(category)),
                Margin = new Thickness(2, 12, 2, 4),
            });

            foreach (var def in matches.Where(d => d.Category == category))
            {
                var button = new Button
                {
                    Content = def.Name,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 4),
                    FontSize = 12,
                };

                // Naming the plugin only where it is not the engine keeps the
                // built-in modules reading as they always did, and tells you
                // which patches will need something installed to open.
                var from = plugins.Modules.ProviderOf(def.TypeId);
                var origin = from is null || from.Id == NodeCatalog.BuiltInProvider.Id
                    ? string.Empty
                    : $"{Environment.NewLine}{Environment.NewLine}From {from.Name} ({from.Id})";

                var tip = def.Description + origin;
                if (tip.Length > 0) ToolTip.SetTip(button, tip);

                var typeId = def.TypeId;
                button.Click += (_, _) => editor.AddNode(typeId);
                modules.Children.Add(button);
            }
        }
    }

    /// <summary>Every provider the installed catalogue knows, the engine's own first.</summary>
    private IReadOnlyList<ModuleProvider> Providers => plugins.Modules.Providers;

    /// <summary>Says what the ticks add up to, so the state is readable without opening them.</summary>
    private void DescribeSources()
    {
        var showing = Providers.Count - hidden.Count;

        sources.Content = showing switch
        {
            _ when hidden.Count == 0 => "All modules  ▾",
            0 => "No plugins  ▾",
            1 => $"{Providers.First(p => !hidden.Contains(p.Id)).Name}  ▾",
            _ => $"{showing} of {Providers.Count} plugins  ▾",
        };
    }

    /// <summary>
    /// Text matches name, category and type id, but deliberately not the
    /// description: every module has a sentence of prose, and matching it turns a
    /// search for a common word into most of the catalogue. The ticks narrow
    /// separately, so the two combine rather than compete.
    /// </summary>
    private bool Matches(NodeDef def, string text)
    {
        // The Output is never listed. Every patch has one already and cannot
        // have a second, so a button for it could only ever select the one that
        // is there — and a palette entry that never adds anything is a puzzle.
        if (NodeCatalog.IsSink(def.TypeId)) return false;

        if (plugins.Modules.ProviderOf(def.TypeId) is { } from && hidden.Contains(from.Id)) return false;

        return text.Length == 0
            || def.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || def.Category.Contains(text, StringComparison.OrdinalIgnoreCase)
            || def.TypeId.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 11,
        Opacity = 0.55,
        Margin = new Thickness(2, 8, 2, 0),
    };
}
