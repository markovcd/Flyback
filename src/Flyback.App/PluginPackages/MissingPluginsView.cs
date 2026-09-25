using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.App.Controls;

namespace Flyback.App.PluginPackages;

/// <summary>
/// What a patch is short of, offered: the plugins it names that are not installed
/// and that the plugin site has a build of for this system.
/// </summary>
/// <remarks>
/// Nothing is installed from here. The answer opens the plugins window at what was
/// found, where a plugin is installed the one way any is — its row, and the dialog
/// behind it that says what it reaches (ADR-0135).
/// </remarks>
internal static class MissingPluginsView
{
    public const string Title = "Plugins this patch needs";

    /// <param name="found">The site's plugin for each one missing, in the order the patch names them.</param>
    public static Control View(IReadOnlyList<SitePlugin> found)
    {
        var page = new StackPanel { Name = "missingPlugins", Width = 460, Spacing = 8 };

        page.Children.Add(new SelectableTextBlock
        {
            Name = "missingSummary",
            // What the site has, never what the patch needs: where a patch is short of
            // two and the site has one, saying "a plugin you do not have" would offer
            // the one as though it were both. The refusal has already said what is
            // missing, in full.
            Text = found.Count == 1
                ? "The plugin site has a plugin this patch needs:"
                : $"The plugin site has {found.Count} of the plugins this patch needs:",
            FontSize = Text.Body,
            TextWrapping = TextWrapping.Wrap,
        });

        var listed = new StackPanel { Name = "missingList", Spacing = 2, Margin = new Thickness(8, 0, 0, 0) };

        foreach (var plugin in found)
        {
            var line = new TextBlock { FontSize = Text.Body, TextWrapping = TextWrapping.Wrap };

            line.Inlines!.Add(new Run($"{plugin.Plugin.Name} {plugin.Plugin.Version}") { FontWeight = FontWeight.SemiBold });

            if (plugin.Plugin.Author is { Length: > 0 } author) line.Inlines.Add(new Run($", by {author}"));

            listed.Children.Add(line);
        }

        page.Children.Add(listed);

        var note = Text.Quiet("Nothing is installed yet. The plugins window says what each one adds and reaches first.");

        note.TextWrapping = TextWrapping.Wrap;
        page.Children.Add(note);

        var look = new Button { Name = "findPlugins", Content = found.Count == 1 ? "Find it" : "Find them", MinWidth = 96 };
        var later = new Button { Name = "notNow", Content = "Not now", MinWidth = 96 };

        look.Click += (_, _) => Dialog.Close(look, true);
        later.Click += (_, _) => Dialog.Close(later, false);

        page.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { look, later },
        });

        return page;
    }
}
