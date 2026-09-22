using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.App.PluginPackages;

namespace Flyback.App.Controls;

/// <summary>A plugin this Flyback has, and what state it is in.</summary>
/// <param name="Waiting">The version waiting to replace it at the next start, or null where nothing is.</param>
/// <param name="Picture">Its preview, as the image file it carries.</param>
/// <param name="Removing">Whether it is removed at the next start.</param>
internal sealed record HubInstalled(ListedPlugin Plugin, string? Waiting, bool Loaded, byte[]? Picture, bool Removing = false)
{
    /// <summary>The version that will be running after the next start.</summary>
    public string Version => Waiting ?? Plugin.Version;

    /// <summary>Whether it is loaded, or what waits for the next start.</summary>
    public string State => Removing ? "Removed at the next start"
        : Waiting is { } version
        ? Loaded ? $"{version} loads at the next start" : "Loads at the next start"
        : "Loaded";
}

/// <summary>
/// The plugins window: what is installed, and what the plugin site offers for this
/// system, under one search box that narrows both the way the site's own search does.
/// </summary>
/// <remarks>
/// The installed run is narrowed here and the site's is asked of the site, a quarter of
/// a second after typing stops, with any earlier question cancelled. A site plugin is
/// matched to an installed one by its assembly, which is what an update replaces.
/// </remarks>
internal sealed class PluginHub : IDisposable
{
    private const double PictureWidth = 128;
    private const double PictureHeight = 72;

    private readonly PluginSite? site;
    private readonly Func<Task<IReadOnlyList<HubInstalled>>> readInstalled;
    private readonly Func<SitePlugin, Task<string?>> install;
    private readonly Func<HubInstalled, Task<string?>>? show;

    /// <summary>The site plugins being downloaded, whose rows say so.</summary>
    private readonly HashSet<string> fetching = [];

    private readonly StackPanel installedRows = new() { Name = "installedPlugins", Spacing = 6 };
    /// <summary>What the site has listed so far, of which only the rows scrolled to are built.</summary>
    private readonly ObservableCollection<SitePlugin> listed = [];

    private readonly ItemsControl siteRows;

    /// <summary>The site's rows built now, which a reread tells what is installed.</summary>
    private readonly HashSet<Grid> realized = [];

    /// <summary>Each preview asked for, kept so a row scrolled back to is not fetched again.</summary>
    private readonly Dictionary<string, Task<byte[]?>> previews = [];

    /// <summary>Ends the preview fetches with the window.</summary>
    private readonly CancellationTokenSource closing = new();
    private readonly TextBlock installedStatus = Status("installedStatus");
    private readonly TextBlock siteStatus = Status("siteStatus");
    private readonly Button more = new() { Name = "moreSite", Content = "More", FontSize = Text.Body, IsVisible = false, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock notice = new()
    {
        Name = "pluginNotice",
        FontSize = Text.Body,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(16, 4, 16, 0),
        IsVisible = false,
    };

    private readonly WrapPanel filters = new() { ItemSpacing = 6, Margin = new Thickness(16, 2, 16, 4) };

    /// <summary>How long typing has to stop for before the site is asked.</summary>
    private static readonly TimeSpan Typing = TimeSpan.FromMilliseconds(250);

    private IReadOnlyList<HubInstalled> installed = [];
    private string? tag;
    private int page;
    private CancellationTokenSource? asking;

    /// <param name="install">Downloads and installs a site plugin, asking first, and says how that went, or null where nothing was done.</param>
    /// <param name="show">Shows what an installed plugin is, when its row is clicked, and says what became of it, or null where nothing did.</param>
    public PluginHub(
        PluginSite? site,
        Func<Task<IReadOnlyList<HubInstalled>>> installed,
        Func<SitePlugin, Task<string?>> install,
        Func<HubInstalled, Task<string?>>? show = null)
    {
        this.site = site;
        readInstalled = installed;
        this.install = install;
        this.show = show;

        siteRows = new ItemsControl
        {
            Name = "sitePlugins",
            ItemsSource = listed,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            // A container being emptied is handed no plugin, and shows nothing.
            ItemTemplate = new FuncDataTemplate<SitePlugin?>((plugin, _) => plugin is null ? new Panel() : SiteRow(plugin), supportsRecycling: false),
        };

        Search.TextChanged += (_, _) =>
        {
            ShowInstalled();
            _ = AskAsync(fresh: true, after: Typing);
        };

        Search.KeyDown += (_, e) =>
        {
            // Empties the box; an empty one lets the key through to close the window.
            if (e.Key != Key.Escape || Search.Text is not { Length: > 0 }) return;

            Search.Text = string.Empty;
            e.Handled = true;
        };

        Search.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => Search.Focus());

        more.Click += (_, _) => _ = AskAsync(fresh: false);

        Header = new StackPanel { Children = { Search, filters, notice } };

        View = new StackPanel
        {
            Name = "pluginHub",
            Width = 640,
            Spacing = 6,
            Margin = new Thickness(16, 0, 16, 16),
            Children =
            {
                Heading("INSTALLED"),
                installedStatus,
                installedRows,
                Heading("ON THE PLUGIN SITE"),
                siteStatus,
                siteRows,
                more,
            },
        };

    }

    /// <summary>Stops asking the site, which is the end of the window.</summary>
    public void Dispose()
    {
        asking?.Cancel();
        asking?.Dispose();
        asking = null;
        closing.Cancel();
        closing.Dispose();
    }

    public TextBox Search { get; } = new()
    {
        Name = "plugin-filter",
        PlaceholderText = "Search plugins by name, author, tag or module",
        FontSize = Text.Body,
        Margin = new Thickness(16, 8, 16, 2),
    };

    /// <summary>The search box and the tag it is narrowed to, which stay put while the lists scroll.</summary>
    public Control Header { get; }

    public StackPanel View { get; }

    /// <summary>Reads what is installed and asks the site for its first page.</summary>
    public async Task LoadAsync()
    {
        await RereadAsync();
        await AskSiteAsync();
    }

    /// <summary>Asks the site for the first page of what matches.</summary>
    public Task AskSiteAsync() => AskAsync(fresh: true);

    /// <summary>Reads what is installed again, and says so on the site's plugins.</summary>
    public async Task RereadAsync()
    {
        installed = await readInstalled();
        ShowInstalled();

        // The site's rows say Install, Update available or Installed by what is installed now.
        OfferAgain();
    }

    private void Narrow(string? to)
    {
        tag = to;
        filters.Children.Clear();

        if (tag is not null)
        {
            var chip = Chip($"{tag}  ✕", "Show every tag");

            chip.Name = "tagFilter";
            chip.Click += (_, _) => Narrow(null);
            filters.Children.Add(chip);
        }

        ShowInstalled();
        _ = AskAsync(fresh: true);
    }

    private void ShowInstalled()
    {
        var search = Search.Text?.Trim();
        var shown = installed.Where(p => p.Plugin.Matches(search, tag)).OrderBy(p => p.Plugin.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        installedRows.Children.Clear();

        foreach (var plugin in shown) installedRows.Children.Add(InstalledRow(plugin));

        installedStatus.Text = installed.Count == 0 ? "No plugins are installed." : "No installed plugin matches.";
        installedStatus.IsVisible = shown.Count == 0;
    }

    /// <summary>
    /// Asks the site for the first page again, or for the page after the last, once
    /// <paramref name="after"/> has passed without another question.
    /// </summary>
    private async Task AskAsync(bool fresh, TimeSpan after = default)
    {
        if (site is null)
        {
            siteStatus.Text = "This window has no plugin site.";
            siteStatus.IsVisible = true;
            return;
        }

        asking?.Cancel();
        asking?.Dispose();

        var cancel = (asking = new CancellationTokenSource()).Token;
        var wanted = fresh ? 1 : page + 1;

        if (fresh) siteStatus.Text = "Looking…";

        siteStatus.IsVisible = fresh;
        more.IsEnabled = false;

        SitePage found;

        try
        {
            if (after > TimeSpan.Zero) await Task.Delay(after, cancel);

            found = await site.SearchAsync(Search.Text, tag, wanted, cancel);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            if (fresh) listed.Clear();

            siteStatus.Text = $"The plugin site at {site.Root} did not answer.";
            siteStatus.IsVisible = true;
            more.IsEnabled = true;
            return;
        }

        if (cancel.IsCancellationRequested) return;

        page = found.Page;

        if (fresh) listed.Clear();

        foreach (var plugin in found.Items) listed.Add(plugin);

        siteStatus.Text = "Nothing on the plugin site matches.";
        siteStatus.IsVisible = listed.Count == 0;
        more.IsVisible = found.More;
        more.IsEnabled = true;
    }

    /// <summary>A site plugin's row, built as it is scrolled to.</summary>
    private Grid SiteRow(SitePlugin plugin)
    {
        var picture = Picture(null);
        var row = Row(plugin.Plugin, picture, new StackPanel());

        // The panel has no spacing, so the gap the installed rows get is margin here.
        row.Margin = new Thickness(0, 7);
        row.Tag = plugin;
        row.AttachedToVisualTree += (_, _) => realized.Add(row);
        row.DetachedFromVisualTree += (_, _) => realized.Remove(row);

        Offer(row, plugin);
        Clickable(row, () => FetchAsync(plugin));
        _ = ShowPreviewAsync(plugin, picture);

        return row;
    }

    private async Task ShowPreviewAsync(SitePlugin plugin, Border into)
    {
        if (site is null || plugin.Preview is null || closing.IsCancellationRequested) return;

        if (!previews.TryGetValue(plugin.Id, out var fetching))
            previews[plugin.Id] = fetching = site.PreviewAsync(plugin, closing.Token);

        try
        {
            if (await fetching is { } bytes && Image(bytes) is { } image) into.Child = image;
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>What a site plugin's row offers: Install where it is not installed, else how it stands against what is.</summary>
    private void Offer(Control row, SitePlugin plugin)
    {
        if (row is not Grid grid || grid.Children.OfType<StackPanel>().FirstOrDefault(c => Grid.GetColumn(c) == 2) is not { } actions) return;

        actions.Children.Clear();

        if (fetching.Contains(plugin.Id))
        {
            actions.Children.Add(Text.Quiet("Downloading…"));
            return;
        }

        var have = Have(plugin);

        if (have is not null)
        {
            var order = PluginChanges.Compare(plugin.Plugin.Version, have.Version);

            actions.Children.Add(Text.Quiet(order switch
            {
                > 0 => "Update available",
                0 => "Installed",
                _ => $"{have.Version} installed",
            }));

            return;
        }

        var button = new Button { Name = "install", Content = "Install", MinWidth = 84, FontSize = Text.Body, Tag = plugin };

        ToolTip.SetTip(button, $"Download {plugin.Plugin.Name} {plugin.Plugin.Version} and see what it is before installing it.");
        button.Click += (_, _) => _ = FetchAsync(plugin);

        actions.Children.Add(button);
    }

    /// <summary>The installed plugin <paramref name="plugin"/> would replace, or null for none.</summary>
    private HubInstalled? Have(SitePlugin plugin) =>
        installed.FirstOrDefault(p => !p.Removing && string.Equals(p.Plugin.Assembly, plugin.Plugin.Assembly, StringComparison.OrdinalIgnoreCase));

    /// <summary>Downloads a site plugin and puts the install dialog up for it.</summary>
    private async Task FetchAsync(SitePlugin plugin)
    {
        if (!fetching.Add(plugin.Id)) return;

        OfferAgain();

        try
        {
            Say(await install(plugin));
        }
        finally
        {
            fetching.Remove(plugin.Id);
        }

        await RereadAsync();
    }

    private void OfferAgain()
    {
        foreach (var row in realized)
            if (row.Tag is SitePlugin plugin) Offer(row, plugin);
    }

    private void Say(string? said)
    {
        notice.Text = said;
        notice.IsVisible = said is not null;
    }

    /// <summary>Makes <paramref name="row"/> run <paramref name="open"/> when clicked anywhere but a button on it.</summary>
    private static void Clickable(Grid row, Func<Task> open)
    {
        // Transparent, so the gaps between its parts take the click too.
        row.Background = Brushes.Transparent;
        row.Cursor = new Cursor(StandardCursorType.Hand);

        row.Tapped += async (_, e) =>
        {
            if (e.Source is Visual clicked && clicked.FindAncestorOfType<Button>(includeSelf: true) is not null) return;

            e.Handled = true;
            await open();
        };
    }

    /// <summary>An installed plugin's row, which shows what it is when clicked anywhere but a tag.</summary>
    private Grid InstalledRow(HubInstalled plugin)
    {
        var row = Row(plugin.Plugin, Picture(plugin.Picture), Installed(plugin));

        if (show is { } showing)
        {
            Clickable(row, async () =>
            {
                Say(await showing(plugin));
                await RereadAsync();
            });
        }

        return row;
    }

    /// <summary>What an installed plugin's row says about it.</summary>
    private static Control Installed(HubInstalled plugin)
    {
        var line = Text.Quiet(plugin.State);

        line.Name = "pluginState";

        return new StackPanel { Children = { line } };
    }

    /// <summary>A plugin's picture, name, byline, description, tags and modules, and what can be done about it.</summary>
    private Grid Row(ListedPlugin plugin, Border picture, Control actions)
    {
        var text = new StackPanel { Spacing = 3, Margin = new Thickness(12, 0) };

        text.Children.Add(new TextBlock
        {
            Name = "pluginName",
            Text = plugin.Name,
            FontSize = Text.Emphasis,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var byline = plugin.Version.Length > 0 ? $"Version {plugin.Version}" : plugin.Assembly;

        if (plugin.Author.Length > 0) byline += $", by {plugin.Author}";

        text.Children.Add(Text.Quiet(byline));

        if (plugin.Description.Length > 0)
        {
            text.Children.Add(new TextBlock
            {
                Text = plugin.Description,
                FontSize = Text.Body,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.WordEllipsis,
            });
        }

        if (plugin.Modules.Count > 0)
        {
            var modules = Text.Quiet("Modules: " + string.Join(", ", plugin.Modules));

            modules.TextWrapping = TextWrapping.Wrap;
            modules.MaxLines = 2;
            modules.TextTrimming = TextTrimming.WordEllipsis;
            text.Children.Add(modules);
        }

        if (plugin.Tags.Count > 0)
        {
            var tags = new WrapPanel { ItemSpacing = 4, LineSpacing = 4, Margin = new Thickness(0, 2, 0, 0) };

            foreach (var word in plugin.Tags)
            {
                var chip = Chip(word, $"Show the plugins tagged {word}");

                chip.Click += (_, _) => Narrow(word);
                tags.Children.Add(chip);
            }

            text.Children.Add(tags);
        }

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(0, 4),
        };

        actions.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(text, 1);
        Grid.SetColumn(actions, 2);

        row.Children.Add(picture);
        row.Children.Add(text);
        row.Children.Add(actions);

        return row;
    }

    /// <summary>The plugin's preview, or a plug where it has none.</summary>
    private static Border Picture(byte[]? bytes) => new()
    {
        Width = PictureWidth,
        Height = PictureHeight,
        CornerRadius = new CornerRadius(3),
        ClipToBounds = true,
        VerticalAlignment = VerticalAlignment.Top,
        Background = new SolidColorBrush(Colors.Canvas),
        Child = (bytes is null ? null : Image(bytes)) ?? NoPicture(),
    };

    private static Control NoPicture()
    {
        return new Viewbox
        {
            Width = 28,
            Height = 28,
            Opacity = 0.35,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = Glyphs.Plug(),
        };
    }

    /// <summary>The image, or null where it will not decode.</summary>
    private static Image? Image(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);

            return new Image { Source = new Bitmap(stream), Stretch = Stretch.UniformToFill };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    private static Button Chip(string text, string tip)
    {
        var chip = new Button
        {
            Content = text,
            FontSize = Text.Small,
            Padding = new Thickness(7, 1),
            MinHeight = 0,
            CornerRadius = new CornerRadius(9),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(1),
        };

        ToolTip.SetTip(chip, tip);

        return chip;
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = Text.Caption,
        FontWeight = FontWeight.SemiBold,
        Foreground = new SolidColorBrush(Colors.Feedback),
        Margin = new Thickness(0, 10, 0, 2),
    };

    private static TextBlock Status(string name)
    {
        var status = Text.Quiet(string.Empty, Text.Body);

        status.Name = name;
        status.IsVisible = false;

        return status;
    }
}
