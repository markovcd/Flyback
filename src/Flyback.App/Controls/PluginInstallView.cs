using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.App.PluginPackages;

namespace Flyback.App.Controls;

/// <summary>
/// The dialog a <c>.fbkp</c> opens: what the package says it is, and Install. It
/// answers true only for Install; closing it any other way installs nothing.
/// </summary>
/// <remarks>
/// Everything the package wrote is shown as plain text under a line saying so, and
/// the warning sits outside it, so a description cannot pass itself off as Flyback
/// vouching for the plugin.
/// </remarks>
internal static class PluginInstallView
{
    public const string Title = "Install plugin";

    private static readonly FontFamily Code = new("Consolas, Menlo, DejaVu Sans Mono, monospace");

    /// <param name="platform">The system this is, whose build would be installed.</param>
    /// <param name="refusal">Why Install is off, or null where it is on.</param>
    /// <param name="replacing">The version installed now, where there is one.</param>
    public static Control View(PluginPackage package, string platform, string? refusal, PluginManifest? replacing)
    {
        var manifest = package.Manifest;

        var page = new StackPanel { Name = "pluginInstall", Spacing = 10, Width = 480, Margin = new Thickness(20, 12, 20, 20) };

        page.Children.Add(Text.Quiet("The package says:"));

        page.Children.Add(new SelectableTextBlock
        {
            Name = "pluginName",
            Text = manifest.Name,
            FontSize = Text.Heading,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        var byline = manifest.Author.Length > 0 ? $"Version {manifest.Version}, by {manifest.Author}" : $"Version {manifest.Version}";

        page.Children.Add(Wrapped(byline, Text.Body, Text.Muted));

        if (manifest.Description.Length > 0) page.Children.Add(Wrapped(manifest.Description, Text.Body));

        var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 4, 0, 0) };

        Fact(facts, "Id", manifest.Id);
        if (manifest.Website is { } website) Fact(facts, "Website", website);
        Fact(facts, "Built for", Builds(package, platform));

        if (package.BuildFor(platform) is { } build)
        {
            var files = package.Files(build);

            Fact(facts, "Adds", $"{files.Count} file(s), {Size(files.Sum(f => f.Length))}");
        }

        Fact(facts, "SHA-256", package.Sha256, mono: true);

        page.Children.Add(facts);

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
                "A plugin runs inside Flyback with everything you can do on this computer: your files, "
                + "your network, your keys. Nobody has checked what the package says about itself. "
                + "Install it only if you trust whoever gave it to you.",
                Text.Body),
        });

        if (refusal is not null)
        {
            var reason = Wrapped($"Cannot be installed. {refusal}", Text.Body, new SolidColorBrush(Colors.Sink));

            reason.Name = "pluginRefusal";
            page.Children.Add(reason);
        }

        var install = new Button { Name = "install", Content = "Install", MinWidth = 96, IsEnabled = refusal is null };
        var cancel = new Button { Name = "cancel", Content = "Cancel", MinWidth = 96 };

        install.Click += (_, _) => Dialog.Close(install, true);
        cancel.Click += (_, _) => Dialog.Close(cancel, false);

        page.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { install, cancel },
        });

        return page;
    }

    /// <summary>Each system the package has a build for, marking the one that would be used here.</summary>
    private static string Builds(PluginPackage package, string platform)
    {
        if (package.Builds.Count == 0) return "nothing";

        var used = package.BuildFor(platform);

        return string.Join(", ", package.Builds.Select(b =>
            b == used ? $"{PluginPackage.Describe(b)} (used here)" : PluginPackage.Describe(b)));
    }

    private static string Size(long bytes) =>
        bytes < 1 << 20 ? $"{Math.Max(1, bytes >> 10)} KB" : $"{bytes / (double)(1 << 20):0.#} MB";

    private static void Fact(Grid grid, string label, string value, bool mono = false)
    {
        var row = grid.RowDefinitions.Count;

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var name = Text.Quiet(label);

        name.Margin = new Thickness(0, 2, 12, 2);
        Grid.SetRow(name, row);

        var text = new SelectableTextBlock
        {
            Text = value,
            FontSize = mono ? Text.Small : Text.Body,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2),
        };

        if (mono) text.FontFamily = Code;

        Grid.SetRow(text, row);
        Grid.SetColumn(text, 1);

        grid.Children.Add(name);
        grid.Children.Add(text);
    }

    private static SelectableTextBlock Wrapped(string text, double size, IBrush? foreground = null)
    {
        var block = new SelectableTextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };

        if (foreground is not null) block.Foreground = foreground;

        return block;
    }
}
