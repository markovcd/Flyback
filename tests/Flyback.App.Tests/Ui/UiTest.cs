using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Themes.Fluent;
using Flyback.App.Tests.Ui;

// Every [AvaloniaFact] and [AvaloniaTheory] in this assembly runs against this
// application, on a UI thread the session owns. Declared once, at the assembly.
[assembly: AvaloniaTestApplication(typeof(UiTest))]

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The Avalonia application a UI test runs inside, and the small amount of
/// scaffolding one needs.
/// </summary>
/// <remarks>
/// Headless is a real Avalonia: it measures, arranges, applies templates,
/// routes input and — with Skia underneath — rasterises. What it does not do is
/// open a window, which is the only part of the shell a test has no opinion
/// about.
/// <para>
/// The Fluent theme is not decoration. Every templated control the inspector
/// uses — a NumericUpDown, a Button — is an empty shell without it, and a test
/// that skipped it would be checking a layout nobody sees.
/// </para>
/// </remarks>
public class UiTest
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<TestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    /// <summary>
    /// Puts a control in a window and lays it out, which is what makes its
    /// templates real. Everything below the window is the control under test.
    /// </summary>
    /// <param name="width">
    /// The inspector's own minimum, so a test sees the layout at the narrowest
    /// the panel is allowed to be rather than at whatever a window happened to be.
    /// </param>
    protected static Window Show(Control content, double width = 300)
    {
        var window = new Window
        {
            Width = width,
            SizeToContent = SizeToContent.Height,
            Content = content,
        };

        window.Show();
        Settle(window);

        return window;
    }

    /// <summary>Runs layout to completion, after something has changed the tree.</summary>
    protected static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    /// <summary>Every descendant of a control, itself included.</summary>
    protected static IEnumerable<Visual> Tree(Visual root)
    {
        yield return root;

        foreach (var child in root.GetVisualChildren())
        foreach (var node in Tree(child))
            yield return node;
    }

    protected static IEnumerable<T> All<T>(Visual root) where T : Visual => Tree(root).OfType<T>();
}

/// <summary>Nothing but the theme — the shell's own App does far more than a test wants.</summary>
public sealed class TestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}
