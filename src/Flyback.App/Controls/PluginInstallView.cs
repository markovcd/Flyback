using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Flyback.App.PluginPackages;
using Flyback.Plugins.Hosting;

namespace Flyback.App.Controls;

/// <summary>What the install dialog was answered with. Closing it without an answer is <see cref="Cancel"/>.</summary>
internal enum PluginAnswer
{
    Cancel,
    Install,
    InstallAndRestart,
    Remove,

    /// <summary>Fetch the newer build the plugin site has, and ask about installing it.</summary>
    Download,
}

/// <summary>
/// The dialog a <c>.fbkp</c> opens: what the plugin is, what it can do, what it
/// replaces, and Install, or Update where it is a newer build of a plugin installed
/// already. Only that button answers with anything but <see cref="PluginAnswer.Cancel"/>.
/// </summary>
/// <remarks>
/// Everything shown is read from the plugin's own assemblies without running them:
/// its name and author as its author set them, and what it adds and reaches from what
/// its code calls. The warning sits apart from all of it, so a description cannot pass
/// itself off as Flyback vouching for the plugin.
/// </remarks>
internal static class PluginInstallView
{
    public static string Title(PluginChange change) => $"{change.Verb()} plugin";

    private static readonly FontFamily Code = new("Consolas, Menlo, DejaVu Sans Mono, monospace");

    /// <param name="platform">The system this is, whose build would be installed.</param>
    /// <param name="refusal">Why Install is off, or null where it is on.</param>
    /// <param name="replacing">The plugin installed in the same folder now, where there is one.</param>
    /// <param name="change">What installing does to <paramref name="replacing"/>.</param>
    /// <param name="offerRestart">Whether to offer starting Flyback again, which is what loads the plugin.</param>
    /// <param name="removable">Whether the plugin is installed and may be removed.</param>
    /// <param name="awaiting">
    /// How many more plugins the patch that asked for this one is still short of, which
    /// is said here because it is why no restart is offered yet.
    /// </param>
    public static Control View(
        PluginPackage package,
        string platform,
        string? refusal,
        InstalledPlugin? replacing,
        PluginChange change,
        bool offerRestart = false,
        bool removable = false,
        int awaiting = 0)
    {
        var page = new StackPanel { Name = "pluginInstall", Spacing = 10, Width = 480, Margin = new Thickness(20, 12, 20, 20) };

        if (package.DescriptionFor(platform) is { } plugin)
        {
            var facts = Describe(page, ListedPlugin.Of(plugin), plugin);

            Fact(facts, "Built for", Builds(package, platform));

            if (package.BuildFor(platform) is { } build)
            {
                var files = package.Files(build);

                Fact(facts, "Adds files", $"{files.Count}, {Size(files.Sum(f => f.Length))}");
            }

            Fact(facts, "Signed by", package.Signer is { } signer ? $"key {signer.Fingerprint}" : "nobody", "pluginSigner", mono: package.Signer is not null);
            Fact(facts, "SHA-256", package.Sha256, mono: true);

            page.Children.Add(facts);

            var note = Text.Quiet(
                "Read from its code without running it. Native code, and code it loads while running, "
                + "can reach more than it names.");

            note.TextWrapping = TextWrapping.Wrap;
            page.Children.Add(note);
        }

        if (replacing?.Description is { } old && package.DescriptionFor(platform) is { } incoming)
        {
            var line = Wrapped(change switch
            {
                PluginChange.Update => $"Updates {old.Name} {old.Version}, which is installed now, to {incoming.Version}.",
                PluginChange.Reinstall => $"{old.Name} {old.Version} is installed already, and this installs it again.",
                PluginChange.Downgrade => $"Replaces {old.Name} {old.Version}, which is installed now, with the older {incoming.Version}.",
                _ => $"Replaces {old.Name} {old.Version}, which is installed now.",
            }, Text.Body);

            line.Name = "pluginReplacing";
            page.Children.Add(line);
        }

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

        if (awaiting > 0)
        {
            var waiting = Wrapped(
                awaiting == 1
                    ? "The patch needs one more plugin as well. Install it too, and the restart that loads both is offered then."
                    : $"The patch needs {awaiting} more plugins as well. Install them too, and the restart that loads them all is offered then.",
                Text.Body);

            waiting.Name = "pluginAwaiting";
            page.Children.Add(waiting);
        }

        var install = new Button { Name = "install", Content = change.Verb(), MinWidth = 96, IsEnabled = refusal is null };
        var cancel = new Button { Name = "cancel", Content = "Cancel", MinWidth = 96 };

        install.Click += (_, _) => Dialog.Close(install,
            offerRestart && restart.IsChecked == true ? PluginAnswer.InstallAndRestart : PluginAnswer.Install);
        cancel.Click += (_, _) => Dialog.Close(cancel, PluginAnswer.Cancel);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { install },
        };

        if (removable) buttons.Children.Add(RemoveButton(null));

        buttons.Children.Add(cancel);

        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };

        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);

        if (offerRestart && refusal is null) row.Children.Add(restart);

        page.Children.Add(row);

        return page;
    }

    /// <summary>
    /// What an installed plugin is, as the install dialog shows a package, with Update
    /// where the plugin site has a newer build, Remove, and Close.
    /// </summary>
    /// <param name="described">What its folder holds, or null where that cannot be read.</param>
    /// <param name="fromPackage">The package that put it there, or null where none did.</param>
    /// <param name="state">Whether it is loaded or waiting for the next start.</param>
    /// <param name="newer">
    /// The newer version the plugin site has, or null for none. The dialog is up before
    /// it is answered, and offers the update once it is.
    /// </param>
    /// <param name="removal">Why Remove is off, or null where it is on.</param>
    /// <param name="id">The ids it loaded under, or empty where it is not loaded.</param>
    /// <param name="provider">Who its modules belong to in a saved patch, or empty where it adds none this run.</param>
    public static Control Installed(
        ListedPlugin listed,
        PluginDescription? described,
        InstalledPlugin? fromPackage,
        string? folder,
        string state,
        Task<string?>? newer = null,
        string? removal = null,
        string id = "",
        string provider = "")
    {
        var page = new StackPanel { Name = "pluginInstalled", Spacing = 10, Width = 480, Margin = new Thickness(20, 12, 20, 20) };
        var facts = Describe(page, listed, described);

        if (id.Length > 0) Fact(facts, "Id", id, "pluginId");
        if (provider.Length > 0) Fact(facts, "Provider", provider, "pluginProvider");

        Fact(facts, "Installed", fromPackage is null ? "not from a package" : "from a package", "pluginOrigin");

        if (fromPackage is not null)
            Fact(facts, "Signed by", fromPackage.Signer is { } signer ? $"key {signer.Fingerprint}" : "nobody", "pluginSigner", mono: fromPackage.Signer is not null);

        if (folder is not null) Fact(facts, "Folder", folder, "pluginFolder");

        page.Children.Add(facts);

        var line = Wrapped(state, Text.Body);

        line.Name = "pluginState";
        page.Children.Add(line);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0),
        };

        buttons.Children.Add(RemoveButton(removal));

        var close = new Button { Name = "close", Content = "Close", MinWidth = 96 };

        close.Click += (_, _) => Dialog.Close(close, PluginAnswer.Cancel);
        buttons.Children.Add(close);

        page.Children.Add(buttons);

        if (newer is not null) _ = OfferAsync(page, buttons, newer);

        return page;
    }

    /// <summary>Says what newer build the plugin site has, and offers Update, once the site has answered.</summary>
    private static async Task OfferAsync(StackPanel page, StackPanel buttons, Task<string?> newer)
    {
        if (await newer is not { } found) return;

        var offer = Wrapped($"The plugin site has {found}.", Text.Body);

        offer.Name = "pluginNewer";
        page.Children.Insert(page.Children.IndexOf(buttons), offer);

        var update = new Button { Name = "update", Content = "Update", MinWidth = 96 };

        ToolTip.SetTip(update, $"Download {found} and see what it is before installing it.");
        update.Click += (_, _) => Dialog.Close(update, PluginAnswer.Download);
        buttons.Children.Insert(0, update);
    }

    /// <summary>Remove, off with <paramref name="refusal"/> as its tip where that is given.</summary>
    private static Button RemoveButton(string? refusal)
    {
        var remove = new Button { Name = "remove", Content = "Remove", MinWidth = 96, IsEnabled = refusal is null };

        ToolTip.SetTip(remove, refusal ?? "Uninstall it. A plugin this run has loaded goes at the next start.");
        ToolTip.SetShowOnDisabled(remove, true);
        remove.Click += (_, _) => Dialog.Close(remove, PluginAnswer.Remove);

        return remove;
    }

    /// <summary>
    /// Adds a plugin's preview, name, byline and description to <paramref name="page"/>,
    /// and returns its facts so far, where <paramref name="described"/> has any.
    /// </summary>
    private static Grid Describe(StackPanel page, ListedPlugin listed, PluginDescription? described)
    {
        if (described?.Preview is { } preview && Picture(preview) is { } picture) page.Children.Add(picture);

        page.Children.Add(new SelectableTextBlock
        {
            Name = "pluginName",
            Text = listed.Name,
            FontSize = Text.Heading,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        var byline = listed.Version.Length > 0 ? $"Version {listed.Version}" : $"{listed.Assembly}.dll";

        if (listed.Author.Length > 0) byline += $", by {listed.Author}";

        page.Children.Add(Wrapped(byline, Text.Body, Text.Muted));

        if (listed.Description.Length > 0) page.Children.Add(Wrapped(listed.Description, Text.Body));

        var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 4, 0, 0) };

        if (listed.Tags.Count > 0) Fact(facts, "Tags", string.Join(", ", listed.Tags), "pluginTags");

        if (described is not { } plugin)
        {
            if (listed.Modules.Count > 0) Fact(facts, "Modules", string.Join(", ", listed.Modules), "pluginModules");

            return facts;
        }

        Fact(facts, "Adds", plugin.Adds.Count > 0 ? string.Join(", ", plugin.Adds) : "nothing Flyback can find", "pluginAdds");
        if (plugin.Modules.Count > 0) Fact(facts, "Modules", string.Join(", ", plugin.Modules.Select(m => m.Name)), "pluginModules");
        else if (plugin.ModulesUnlisted) Fact(facts, "Modules", "not listed: it was built before a plugin declared them", "pluginModules");
        Fact(facts, "Reaches", plugin.Reaches.Count > 0 ? string.Join(", ", plugin.Reaches) : "nothing outside Flyback that it names", "pluginReaches");
        Fact(facts, "Assembly", $"{plugin.Assembly}.dll");

        if (plugin.BuiltAgainst.Length > 0) Fact(facts, "Built against", plugin.BuiltAgainst, "pluginContract");

        return facts;
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
