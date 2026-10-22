using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Flyback.Editor.Composition;
using Flyback.Editor.Controls;
using Flyback.Editor.Shell;
using Flyback.Editor.Statistics;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Statistics;

/// <summary>The first-start question, asked only of a run that could send counts and has nobody's answer yet.</summary>
public sealed class UsageQuestionTests : EditorTest
{
    private readonly string settingsPath = Path.Combine(
        Path.GetTempPath(),
        "flyback-usage-question-" + Guid.NewGuid().ToString("N"),
        "settings.json");

    private readonly CollectedEvents sink = new();

    public override void Dispose()
    {
        base.Dispose();

        var folder = Path.GetDirectoryName(settingsPath);

        if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private MainWindow Open(Usage usage)
    {
        var window = NewMainWindow(new EditorSetup { Folders = new() { SettingsPath = settingsPath }, Usage = usage });

        window.Show();
        Settle(window);

        for (var attempt = 0; attempt < 20 && !All<ModalOverlay>(window).Any(); attempt++)
            Dispatcher.UIThread.RunJobs();

        return window;
    }

    private static ModalOverlay? Question(MainWindow window) =>
        All<ModalOverlay>(window).SingleOrDefault(o => All<DialogBody>(o).Any(b => b.Name == "usageQuestion"));

    private static void Answer(MainWindow window, string button)
    {
        Press(All<Button>(Question(window).ShouldNotBeNull()).Single(b => b.Name == button));
        Settle(window);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Sharing_sends_the_first_start_and_keeps_the_answer()
    {
        var usage = new Usage(sink, new Launch(First: true), hold: true);
        var window = Open(usage);

        sink.Events.ShouldBeEmpty("nothing leaves before the answer");

        Answer(window, "shareUsage");

        Question(window).ShouldBeNull();
        sink.Events.ShouldHaveSingleItem().Name.ShouldBe("started");

        var saved = UsageSettings.Load(settingsPath);

        saved.CountUsage.ShouldBeTrue();
        saved.Asked.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Not_sharing_sends_nothing_and_keeps_the_answer()
    {
        var usage = new Usage(sink, hold: true);
        var window = Open(usage);

        Answer(window, "keepUsage");

        usage.Played(["flyback.oscillator"]);
        sink.Events.ShouldBeEmpty();

        var saved = UsageSettings.Load(settingsPath);

        saved.CountUsage.ShouldBeFalse();
        saved.Asked.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_run_with_nowhere_to_send_is_not_asked()
    {
        Question(Open(Usage.Off)).ShouldBeNull();
    }

    [AvaloniaFact]
    public void A_run_already_answered_is_not_asked()
    {
        Question(Open(new Usage(sink))).ShouldBeNull();
    }

    /// <summary>Draws the window with the question up. Run by hand with SHOT_DIR naming a folder.</summary>
    [AvaloniaFact]
    public void Draw_the_question()
    {
        var folder = Environment.GetEnvironmentVariable("SHOT_DIR");
        Assert.SkipWhen(folder is null, "a tool for looking at the question, run with SHOT_DIR naming a folder to draw into");

        var window = Open(new Usage(sink, hold: true));

        window.Width = 1280;
        window.Height = 800;
        Settle(window);

        Directory.CreateDirectory(folder);

        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("the window rendered nothing");

        frame.Save(Path.Combine(folder, "usage-question.png"), new PngBitmapEncoderOptions());
    }
}
