using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Core;

namespace Flyback.App.Controls;

/// <summary>
/// What the program is, who wrote it, what it may be done with, and where to
/// send something if it was worth anything to you.
/// </summary>
/// <remarks>
/// The facts live here rather than in the window that shows them, because there
/// is exactly one right answer to each and no reason for a second copy to drift
/// from it. The licence text is not reproduced — a name and a copyright line are
/// what a person reads, and the file beside the source is what a lawyer does.
/// </remarks>
internal static class About
{
    /// <summary>
    /// What it is, in one line. Not a video synthesiser: the same patch is a
    /// picture and a sound, and calling it one or the other names half of it.
    /// </summary>
    public const string Description = "A patchable synthesiser for picture and sound.";

    public const string Author = "Arkadiusz Markowski";

    public const string Licence = "MIT";

    public const string Copyright = "Copyright © 2026 Arkadiusz Markowski";

    /// <summary>
    /// Where a donation would go, or empty while there is nowhere to send one.
    /// </summary>
    /// <remarks>
    /// Empty is shown as empty. An address is a string nobody can check by
    /// reading — one wrong character sends the money to nobody at all — so this
    /// is never stood in for, guessed at, or filled with an example: until there
    /// is a real one here, the window says there is not.
    /// </remarks>
    public const string BitcoinAddress = "";

    /// <summary>
    /// The build's version — a release's own, or a dev build's plus the commit
    /// it was built from.
    /// </summary>
    /// <remarks>
    /// Read from the assembly rather than written down, so it can only ever say
    /// what was actually built. Set by the <c>Version</c> MSBuild property —
    /// <c>0.1.0</c> by default, or whatever a release passes with
    /// <c>-p:Version=X.Y.Z</c> (see Directory.Build.props, the Dockerfile and
    /// the release workflow). Directory.Build.props also decides whether the
    /// commit is appended at all: only the default carries one, since a real
    /// release's version already names something real on its own.
    /// </remarks>
    public static string Version =>
        typeof(About).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(About).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    /// <summary>The contents of the About window.</summary>
    public static Control View()
    {
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("64,*") };

        var mark = new LogoMark
        {
            Width = 56,
            Height = 56,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var titles = new StackPanel { Spacing = 2, Margin = new Thickness(12, 0, 0, 0) };

        titles.Children.Add(new TextBlock
        {
            Text = GlobalConstants.ApplicationName,
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
        });

        titles.Children.Add(Quiet(Description));
        titles.Children.Add(Quiet($"Version {Version}"));
        titles.Children.Add(Quiet($"by {Author}"));

        Grid.SetColumn(mark, 0);
        Grid.SetColumn(titles, 1);
        heading.Children.Add(mark);
        heading.Children.Add(titles);

        var page = new StackPanel { Spacing = 14, Width = 360, Margin = new Thickness(20) };

        page.Children.Add(heading);
        page.Children.Add(Rule());
        page.Children.Add(Caption("Licence"));
        page.Children.Add(new TextBlock { Text = $"{Licence} licence.  {Copyright}", TextWrapping = TextWrapping.Wrap });
        page.Children.Add(Rule());
        page.Children.Add(Caption("Support"));
        page.Children.Add(Donation());

        return page;
    }

    /// <summary>
    /// The address, and a way to take a copy of it that is not retyping it.
    /// </summary>
    /// <remarks>
    /// Selectable and monospaced, because the one thing worse than no address is
    /// one that was read wrongly. Where none is set the section says so in as
    /// many words rather than showing a blank line somebody might take for a
    /// rendering fault.
    /// </remarks>
    private static Control Donation()
    {
        var block = new StackPanel { Spacing = 8 };

        if (BitcoinAddress.Length == 0)
        {
            block.Children.Add(Quiet("There is no donation address set in this build."));
            return block;
        }

        block.Children.Add(new TextBlock
        {
            Text = "If this was worth anything to you, a little bitcoin is welcome.",
            TextWrapping = TextWrapping.Wrap,
        });

        var address = new TextBox
        {
            Text = BitcoinAddress,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };

        var copy = new Button { Content = "Copy", Width = 78, HorizontalAlignment = HorizontalAlignment.Right };

        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(copy)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(BitcoinAddress);

            copy.Content = "Copied";
        };

        block.Children.Add(address);
        block.Children.Add(copy);

        return block;
    }

    private static TextBlock Quiet(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = new SolidColorBrush(Colors.Muted),
        TextWrapping = TextWrapping.Wrap,
    };

    private static TextBlock Caption(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 10.5,
        FontWeight = FontWeight.SemiBold,
        Opacity = 0.6,
    };

    private static Control Rule() => new Border
    {
        Height = 1,
        Background = new SolidColorBrush(Colors.Separator),
    };
}
