using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The About window, opened on its own, headless.</summary>
[Binding]
public sealed class AboutSteps(HeadlessTurn turn)
{
    /// <summary>How many turns the UI thread is given after a click, enough for a clipboard's round trip.</summary>
    private const int Turns = 4;

    private Window? window;

    [AfterScenario]
    public void Close()
    {
        if (window is not { } open) return;

        window = null;

        Headless.Run(() =>
        {
            open.Close();
            Dispatcher.UIThread.RunJobs();
        });

        turn.Leave(this);
    }

    [Given("the About window is open")]
    public void GivenAboutIsOpen()
    {
        turn.Take(this);

        window = Headless.Run(() =>
        {
            var open = new Window { SizeToContent = SizeToContent.WidthAndHeight, Content = About.View() };

            open.Show();
            open.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            return open;
        });
    }

    [When("its mark is clicked seven times")]
    public void WhenTheMarkIsClicked() =>
        Headless.Run(async () =>
        {
            var open = Open;
            var mark = open.GetVisualDescendants().OfType<LogoMark>().Single();
            var at = mark.TranslatePoint(mark.Bounds.Center - mark.Bounds.Position, open) ?? default;

            for (var click = 0; click < 7; click++)
            {
                open.MouseDown(at, MouseButton.Left);
                open.MouseUp(at, MouseButton.Left);
            }

            // A clipboard answers off this thread and back onto it.
            for (var step = 0; step < Turns; step++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(1);
            }

            return true;
        });

    [Then("the clipboard holds text that builds the picture the mark plays")]
    public void ThenTheClipboardBuildsTheMark()
    {
        var text = Headless.Run(async () =>
            await (TopLevel.GetTopLevel(Open)?.Clipboard ?? throw new InvalidOperationException("no clipboard")).TryGetTextAsync());

        var built = PatchLanguage.Build(text.ShouldNotBeNull(), NodeCatalog.BuiltIn);

        built.Ok.ShouldBeTrue(built.Report);
        built.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program.Ops
            .ShouldBe(LogoBeam.Patch().CompileForVideo(NodeCatalog.BuiltIn).Program.Ops);
    }

    private Window Open => window.ShouldNotBeNull("the About window is not open");
}
