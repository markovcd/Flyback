using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Core;

namespace Flyback.App.Controls;

/// <summary>
/// What the program is, who wrote it, what it may be done with, and where to send
/// something if it was worth anything to you.
/// </summary>
/// <remarks>
/// The facts live here rather than in the window that shows them, because there is
/// one right answer to each. The licence text is not reproduced — a name and a
/// copyright line are what a person reads, and the file beside the source is what a
/// lawyer does.
/// </remarks>
internal static class About
{
    /// <summary>
    /// What it is, in one line. Not a video synthesiser: the same patch is a
    /// picture and a sound, and calling it one or the other names half of it.
    /// </summary>
    public const string Description = "A patchable synthesiser for picture and sound.";

    public const string Author = "Arkadiusz Markowski";

    /// <summary>Where the screenshots, the tutorials and the plugin guide live.</summary>
    public const string Website = "https://markovcd.github.io/Flyback/";

    public const string Licence = "MIT";

    public const string Copyright = "Copyright © 2026 Arkadiusz Markowski";

    /// <summary>
    /// Where a donation would go, or empty while there is nowhere to send one.
    /// </summary>
    /// <remarks>
    /// Empty is shown as empty. An address is a string nobody can check by
    /// reading — one wrong character sends the money to nobody at all — so this
    /// is never stood in for, guessed at, or filled with an example: until there
    /// is a real one here, the window says there is not. What reading cannot do
    /// the bech32 checksum can, and a test spends it on every build.
    /// </remarks>
    public const string BitcoinAddress = "bc1qdu86tdtksg6mlqspdxmc2de9qppte3a5430lmg";

    /// <summary>
    /// The build's version — a release's own, or a dev build's plus the commit it was
    /// built from.
    /// </summary>
    /// <remarks>
    /// Read from the assembly rather than written down, so it can only say what was
    /// actually built. Set by the <c>Version</c> MSBuild property, which also decides
    /// whether the commit is appended: only the default carries one, since a release's
    /// version already names something real.
    /// </remarks>
    public static string Version =>
        typeof(About).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(About).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    /// <summary>The contents of the About window.</summary>
    /// <param name="pluginReport">
    /// What loaded and what did not, and where it was looked for — built by the
    /// host, since nothing in this file knows what a plugin is.
    /// </param>
    public static Control View(string pluginReport)
    {
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("64,*") };

        var mark = Mark();

        var titles = new StackPanel { Spacing = 2, Margin = new Thickness(12, 0, 0, 0) };

        titles.Children.Add(new TextBlock
        {
            Text = GlobalConstants.ApplicationName,
            FontSize = Text.Display,
            FontWeight = FontWeight.SemiBold,
        });

        titles.Children.Add(Quiet(Description));
        titles.Children.Add(Quiet($"Version {Version}"));
        titles.Children.Add(Quiet($"by {Author}"));
        titles.Children.Add(Link(Website));

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
        page.Children.Add(Rule());
        page.Children.Add(Caption("Plugins"));
        page.Children.Add(new TextBlock { Text = pluginReport, FontSize = Text.Body, TextWrapping = TextWrapping.Wrap });

        return page;
    }

    /// <summary>
    /// The mark, and what it does for somebody who keeps clicking it: on the
    /// seventh it stops being a drawing and becomes the patch it is a picture
    /// of, for as long as the window is open.
    /// </summary>
    /// <remarks>
    /// Drawn at twice its size and scaled down, so the beam has an edge on a
    /// display that would otherwise show it one pixel wide.
    /// </remarks>
    private static Control Mark()
    {
        const int side = 56;
        const int clicks = 7;

        var drawn = new LogoMark();
        var played = new Image { IsVisible = false };

        var mark = new Panel
        {
            Width = side,
            Height = side,
            VerticalAlignment = VerticalAlignment.Top,

            // Neither a bare Control nor an empty Panel is hit-testable, and the
            // clicks have to land on something.
            Background = Brushes.Transparent,
            Children = { drawn, played },
        };

        var counted = 0;
        PresetMotion? motion = null;

        mark.PointerPressed += (_, _) =>
        {
            if (motion is not null || ++counted < clicks) return;

            drawn.IsVisible = false;
            played.IsVisible = true;
            motion = PresetMotion.Play(LogoBeam.Patch(), played, clock: null, side * 2, side * 2);
        };

        // The frames are drawn on a thread of their own, which nothing else here
        // would ever stop.
        mark.DetachedFromVisualTree += (_, _) => motion?.Dispose();

        return mark;
    }

    /// <summary>
    /// The address as a code to scan and as text to copy, neither of which is
    /// retyping it.
    /// </summary>
    /// <remarks>
    /// The code is encoded from the same constant the text below it shows, so the
    /// two cannot come to disagree. The text is selectable and monospaced, because
    /// the one thing worse than no address is one that was read wrongly. Where
    /// none is set the section says so in as many words rather than showing a
    /// blank line somebody might take for a rendering fault.
    /// </remarks>
    private static Control Donation()
    {
        const int side = 148;

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

        block.Children.Add(Quiet(
            "It buys tokens, which is what Flyback is written with. "
            + "Strictly non-profit: nothing here is sold and nobody is paid out of it."));

        var code = new QrCode
        {
            Text = BitcoinAddress,
            Width = side,
            Height = side,
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        ToolTip.SetTip(code, "Click to copy the address");

        var address = new TextBox
        {
            Text = BitcoinAddress,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"),
            FontSize = Text.Body,
            TextWrapping = TextWrapping.Wrap,
        };

        // Said under the code rather than in the tip, which is not on screen any
        // more by the time there is anything to say.
        var said = Quiet(string.Empty);

        code.PointerPressed += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(code)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(BitcoinAddress);

            said.Text = "Copied.";
        };

        block.Children.Add(code);
        block.Children.Add(address);
        block.Children.Add(said);

        return block;
    }

    /// <summary>A line of text that opens <paramref name="uri"/> in the system browser.</summary>
    private static TextBlock Link(string uri)
    {
        var link = new TextBlock
        {
            Text = uri,
            FontSize = Text.Body,
            Foreground = new SolidColorBrush(Colors.Attention),
            TextDecorations = TextDecorations.Underline,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        link.PointerPressed += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(link)?.Launcher is { } launcher)
                await launcher.LaunchUriAsync(new Uri(uri));
        };

        return link;
    }

    private static TextBlock Quiet(string text) => new()
    {
        Text = text,
        FontSize = Text.Body,
        Foreground = Text.Muted,
        TextWrapping = TextWrapping.Wrap,
    };

    private static TextBlock Caption(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = Text.Caption,
        FontWeight = FontWeight.SemiBold,
        Foreground = Text.Muted,
    };

    private static Control Rule() => new Border
    {
        Height = 1,
        Background = new SolidColorBrush(Colors.Separator),
    };
}
