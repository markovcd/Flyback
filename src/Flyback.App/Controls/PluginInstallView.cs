using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Flyback.Plugins.Hosting;

namespace Flyback.App.Controls;

/// <summary>What the install dialog was answered with. Closing it without an answer is <see cref="Cancel"/>.</summary>
internal enum PluginAnswer
{
    Cancel,
    Install,
    InstallAndRestart,
}

/// <summary>
/// The dialog a <c>.fbkp</c> opens: what the plugin is, what it can do, and Install.
/// Only Install answers with anything but <see cref="PluginAnswer.Cancel"/>.
/// </summary>
/// <remarks>
/// Everything shown is read from the plugin's own assemblies without running them:
/// its name and author as its author set them, and what it adds and reaches from what
/// its code calls. The warning sits apart from all of it, so a description cannot pass
/// itself off as Flyback vouching for the plugin.
/// </remarks>
internal static class PluginInstallView
{
    public const string Title = "Install plugin";

    private static readonly FontFamily Code = new("Consolas, Menlo, DejaVu Sans Mono, monospace");

    /// <param name="platform">The system this is, whose build would be installed.</param>
    /// <param name="refusal">Why Install is off, or null where it is on.</param>
    /// <param name="replacing">The plugin installed in the same folder now, where there is one.</param>
    /// <param name="offerRestart">Whether to offer starting Flyback again, which is what loads the plugin.</param>
    public static Control View(
        PluginPackage package,
        string platform,
        string? refusal,
        PluginDescription? replacing,
        bool offerRestart = false)
    {
        var page = new StackPanel { Name = "pluginInstall", Spacing = 10, Width = 480, Margin = new Thickness(20, 12, 20, 20) };

        if (package.DescriptionFor(platform) is { } plugin)
        {
            if (plugin.Preview is { } preview && Picture(preview) is { } picture) page.Children.Add(picture);

            page.Children.Add(new SelectableTextBlock
            {
                Name = "pluginName",
                Text = plugin.Name,
                FontSize = Text.Heading,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            });

            var byline = plugin.Author.Length > 0 ? $"Version {plugin.Version}, by {plugin.Author}" : $"Version {plugin.Version}";

            page.Children.Add(Wrapped(byline, Text.Body, Text.Muted));

            if (plugin.Description.Length > 0) page.Children.Add(Wrapped(plugin.Description, Text.Body));

            var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 4, 0, 0) };

            if (plugin.Tags.Count > 0) Fact(facts, "Tags", string.Join(", ", plugin.Tags), "pluginTags");
            Fact(facts, "Adds", plugin.Adds.Count > 0 ? string.Join(", ", plugin.Adds) : "nothing Flyback can find", "pluginAdds");
            Fact(facts, "Reaches", plugin.Reaches.Count > 0 ? string.Join(", ", plugin.Reaches) : "nothing outside Flyback that it names", "pluginReaches");
            Fact(facts, "Assembly", $"{plugin.Assembly}.dll");

            if (plugin.BuiltAgainst.Length > 0) Fact(facts, "Built against", plugin.BuiltAgainst, "pluginContract");
            Fact(facts, "Built for", Builds(package, platform));

            if (package.BuildFor(platform) is { } build)
            {
                var files = package.Files(build);

                Fact(facts, "Adds files", $"{files.Count}, {Size(files.Sum(f => f.Length))}");
            }

            Fact(facts, "SHA-256", package.Sha256, mono: true);

            page.Children.Add(facts);

            var note = Text.Quiet(
                "Read from its code without running it. Native code, and code it loads while running, "
                + "can reach more than it names.");

            note.TextWrapping = TextWrapping.Wrap;
            page.Children.Add(note);
        }

        if (replacing is not null)
            page.Children.Add(Wrapped($"Replaces {replacing.Name} {replacing.Version}, which is installed now.", Text.Body));

        page.Children.Add(new Border
        {
            Name = "pluginWarning",
            BorderBrush = new SolidColorBrush(Colors.Attention),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 6, 0, 0),
            Child = Wrapped(
                "A plugin runs inside Flyback with everything you can do on this computer. Its name and "
                + "author are whatever its author wrote, and nobody has checked them. Install it only if "
                + "you trust whoever gave it to you.",
                Text.Body),
        });

        if (refusal is not null)
        {
            var reason = Wrapped($"Cannot be installed. {refusal}", Text.Body, new SolidColorBrush(Colors.Sink));

            reason.Name = "pluginRefusal";
            page.Children.Add(reason);
        }

        // On by default: a plugin does nothing until Flyback starts again, and the
        // close asks about unsaved work like any other.
        var restart = new CheckBox
        {
            Name = "restart",
            Content = "Restart Flyback to load it",
            IsChecked = true,
            FontSize = Text.Body,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var install = new Button { Name = "install", Content = "Install", MinWidth = 96, IsEnabled = refusal is null };
        var cancel = new Button { Name = "cancel", Content = "Cancel", MinWidth = 96 };

        install.Click += (_, _) => Dialog.Close(install,
            offerRestart && restart.IsChecked == true ? PluginAnswer.InstallAndRestart : PluginAnswer.Install);
        cancel.Click += (_, _) => Dialog.Close(cancel, PluginAnswer.Cancel);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { install, cancel },
        };

        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };

        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);

        if (offerRestart && refusal is null) row.Children.Add(restart);

        page.Children.Add(row);

        return page;
    }

    /// <summary>The plugin's preview, or null where it will not decode.</summary>
    private static Image? Picture(PluginPreview preview)
    {
        try
        {
            using var stream = new MemoryStream(preview.Bytes, writable: false);

            return new Image
            {
                Name = "pluginPreview",
                Source = new Bitmap(stream),
                Stretch = Stretch.Uniform,
                MaxHeight = 270,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 0),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Each system the package has a build for, marking the one that would be used here.</summary>
    private static string Builds(PluginPackage package, string platform)
    {
        var used = package.BuildFor(platform);

        return string.Join(", ", package.Builds.Select(b =>
            b == used ? $"{PluginPackage.Describe(b)} (used here)" : PluginPackage.Describe(b)));
    }

    private static string Size(long bytes) =>
        bytes < 1 << 20 ? $"{Math.Max(1, bytes >> 10)} KB" : $"{bytes / (double)(1 << 20):0.#} MB";

    private static void Fact(Grid grid, string label, string value, string? name = null, bool mono = false)
    {
        var row = grid.RowDefinitions.Count;

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var caption = Text.Quiet(label);

        caption.Margin = new Thickness(0, 2, 12, 2);
        Grid.SetRow(caption, row);

        var text = new SelectableTextBlock
        {
            Name = name,
            Text = value,
            FontSize = mono ? Text.Small : Text.Body,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2),
        };

        if (mono) text.FontFamily = Code;

        Grid.SetRow(text, row);
        Grid.SetColumn(text, 1);

        grid.Children.Add(caption);
        grid.Children.Add(text);
    }

    private static SelectableTextBlock Wrapped(string text, double size, IBrush? foreground = null)
    {
        var block = new SelectableTextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };

        if (foreground is not null) block.Foreground = foreground;

        return block;
    }
}
