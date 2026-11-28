using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Site;
using Flyback.Plugins.Hosting;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.PluginPackages;

/// <summary>
/// The module packs the preset site shares, as a section of the plugins window under its
/// search box: each at its newest version, with Install, Update or Installed beside it.
/// </summary>
/// <remarks>Shown once the site answers with any pack at all.</remarks>
internal sealed class PackSiteSection : IDisposable
{
    private readonly PackSite site;
    private readonly Func<IReadOnlyDictionary<string, string>> installed;
    private readonly Func<SitePack, Task<string?>> install;
    private readonly TextBox search;
    private readonly StackPanel rows = new() { Name = "sitePacks", Spacing = 10 };
    private readonly TextBlock status = Text.Quiet(string.Empty, Text.Body);
    private readonly Button more = new() { Name = "morePacks", Content = "More", FontSize = Text.Body, IsVisible = false, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock said = new() { Name = "packNotice", FontSize = Text.Body, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly SiteSearch<SitePack> asking;
    private readonly HashSet<string> fetching = [];
    private readonly List<SitePack> listed = [];

    /// <param name="installed">Each installed pack's version, by its id.</param>
    /// <param name="install">Downloads and installs a pack, and says how that went.</param>
    /// <param name="search">The window's search box, whose words narrow the packs too.</param>
    public PackSiteSection(PackSite site, Func<IReadOnlyDictionary<string, string>> installed, Func<SitePack, Task<string?>> install, TextBox search)
    {
        this.site = site;
        this.installed = installed;
        this.install = install;
        this.search = search;

        status.Name = "packStatus";
        status.IsVisible = false;

        asking = new SiteSearch<SitePack>(SearchAsync, status, more, $"The preset site at {site.Root} did not answer.")
        {
            Show = (found, fresh, _) =>
            {
                if (fresh) Clear();

                listed.AddRange(found);

                // Hidden until the site shares a pack, so a site with none shows no empty section.
                if (found.Count > 0) View!.IsVisible = true;

                foreach (var pack in found) rows.Children.Add(Row(pack));
            },
            Empty = () => listed.Count == 0,
            Nothing = () => "Nothing on the preset site matches.",
            Failed = (fresh, _) => { if (fresh) Clear(); },
        };

        search.TextChanged += Searched;
        more.Click += (_, _) => _ = asking.AskAsync(fresh: false);

        View = new StackPanel
        {
            Name = "packSection",
            Spacing = 6,
            IsVisible = false,
            Children = { Heading(), said, status, rows, more },
        };
    }

    public Control View { get; }

    /// <summary>Asks the site for the first page of what matches.</summary>
    public Task AskAsync() => asking.AskAsync(fresh: true);

    public void Dispose()
    {
        search.TextChanged -= Searched;
        asking.Dispose();
    }

    private void Searched(object? sender, TextChangedEventArgs e) => _ = asking.AskAsync(fresh: true, after: SiteSearch<SitePack>.Typing);

    private async Task<SitePage<SitePack>> SearchAsync(int page, CancellationToken cancel)
    {
        var found = await site.SearchAsync(search.Text, page, cancel);
        return new SitePage<SitePack>(found.Items, found.Total, found.Page, found.PageSize);
    }

    private void Clear()
    {
        listed.Clear();
        rows.Children.Clear();
    }

    private Control Row(SitePack pack)
    {
        var name = new TextBlock { Text = $"{pack.Pack} {pack.Version}", FontSize = Text.Body, FontWeight = FontWeight.SemiBold };
        var modules = Text.Quiet(string.Join(", ", pack.Modules.Select(m => m.Name)), Text.Body);
        modules.TextWrapping = TextWrapping.Wrap;

        var action = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Tag = pack };
        var words = new StackPanel { Spacing = 2, Children = { name, modules } };

        Grid.SetColumn(action, 1);
        row.Children.Add(words);
        row.Children.Add(action);

        Offer(action, pack);

        return row;
    }

    /// <summary>Install where the pack is not installed, Update where an older version is, else how it stands.</summary>
    private void Offer(StackPanel action, SitePack pack)
    {
        action.Children.Clear();

        if (fetching.Contains(pack.Id))
        {
            action.Children.Add(Text.Quiet("Downloading…"));
            return;
        }

        var have = installed().GetValueOrDefault(pack.Pack);
        var order = have is null ? 1 : PluginChanges.Compare(pack.Version, have) ?? (pack.Version == have ? 0 : 1);

        if (order <= 0)
        {
            var state = Text.Quiet(order == 0 ? "Installed" : $"{have} installed");
            state.Name = "packState";
            action.Children.Add(state);
            return;
        }

        var button = new Button { Name = "installPack", Content = have is null ? "Install" : "Update", FontSize = Text.Body };

        button.Click += async (_, _) =>
        {
            fetching.Add(pack.Id);
            Offer(action, pack);

            string? result;

            try
            {
                result = await install(pack);
            }
            finally
            {
                fetching.Remove(pack.Id);
            }

            Say(result);

            foreach (var row in rows.Children.OfType<Grid>())
                if (row.Tag is SitePack shown && row.Children.OfType<StackPanel>().LastOrDefault() is { } actions) Offer(actions, shown);
        };

        action.Children.Add(button);
    }

    private void Say(string? text)
    {
        said.Text = text;
        said.IsVisible = !string.IsNullOrEmpty(text);
    }

    private Control Heading()
    {
        var heading = new TextBlock
        {
            Text = "MODULE PACKS ON THE PRESET SITE",
            FontSize = Text.Caption,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Colors.Attention),
            VerticalAlignment = VerticalAlignment.Center,
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 2),
            Children = { heading, SiteLink.To("Open", site.Root) },
        };
    }
}
