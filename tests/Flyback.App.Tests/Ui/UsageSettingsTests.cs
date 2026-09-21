using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.App.Statistics;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The Usage tab of the settings window: the switch, and that clearing it stops the
/// run it is cleared in rather than only the next one (ADR-0094).
/// </summary>
public sealed class UsageSettingsTests : UiTest
{
    private readonly string settingsPath = Path.Combine(
        Path.GetTempPath(),
        "flyback-usage-settings-" + Guid.NewGuid().ToString("N"),
        "usage.json");

    private const int UsageTab = 8;

    private sealed class Collected : IUsageSink
    {
        public List<UsageEvent> Events { get; } = [];

        public void Send(UsageEvent happened) => Events.Add(happened);

        public void Drain(TimeSpan most) { }
    }

    public override void Dispose()
    {
        base.Dispose();

        var folder = Path.GetDirectoryName(settingsPath);

        if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static MainWindow Open(string? settingsPath = null, Usage? usage = null)
    {
        var window = new MainWindow(usageSettingsPath: settingsPath, usage: usage);

        window.Show();
        Settle(window);
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    private static ModalOverlay OpenSettings(MainWindow window)
    {
        All<Button>(window).Single(b => b.Name == "settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        for (var attempt = 0; attempt < 20 && !All<ModalOverlay>(window).Any(); attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);

        var dialog = All<ModalOverlay>(window).Single();

        All<TabControl>(dialog).Single(t => t.Name == "settingsTabs").SelectedIndex = UsageTab;
        Settle(window);

        return dialog;
    }

    private static void Close(MainWindow window, ModalOverlay dialog, string by)
    {
        All<Button>(dialog)
            .Single(b => b.Content as string == by)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        for (var attempt = 0; attempt < 20 && All<ModalOverlay>(window).Any(); attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);
    }

    private static CheckBox Switch(ModalOverlay dialog) =>
        All<CheckBox>(dialog).Single(c => c.Name == "sendUsageStatistics");

    [AvaloniaFact]
    public void Statistics_are_counted_until_switched_off()
    {
        var window = Open(settingsPath);

        Switch(OpenSettings(window)).IsChecked.ShouldBe(true);
    }

    [AvaloniaFact]
    public void Switching_off_and_saving_keeps_it_off()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window);

        Switch(dialog).IsChecked = false;
        Close(window, dialog, "Save");

        UsageSettings.Load(settingsPath).SendUsageStatistics.ShouldBeFalse();
        Switch(OpenSettings(Open(settingsPath))).IsChecked.ShouldBe(false, "the next launch reads it back");
    }

    [AvaloniaFact]
    public void Cancel_puts_the_switch_back()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window);

        Switch(dialog).IsChecked = false;
        Close(window, dialog, "Cancel");

        File.Exists(settingsPath).ShouldBeFalse();
        Switch(OpenSettings(window)).IsChecked.ShouldBe(true);
    }

    [AvaloniaFact]
    public void The_app_does_not_close_while_the_settings_are_up()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window);

        window.Close();
        Dispatcher.UIThread.RunJobs();

        window.IsVisible.ShouldBeTrue("the settings are still waiting on Save or Cancel");
        All<ModalOverlay>(window).ShouldHaveSingleItem().ShouldBe(dialog);
    }

    [AvaloniaFact]
    public void The_app_closes_once_the_settings_are_down()
    {
        var window = Open(settingsPath);

        Close(window, OpenSettings(window), "Cancel");

        window.Close();
        Dispatcher.UIThread.RunJobs();

        window.IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Switching_it_off_stops_this_run_too()
    {
        var sink = new Collected();
        var usage = new Usage(sink);
        var window = Open(settingsPath, usage);

        var dialog = OpenSettings(window);
        Switch(dialog).IsChecked = false;
        Close(window, dialog, "Save");

        var said = sink.Events.Count;

        usage.Assistant("openai");
        usage.Played(["flyback.oscillator"]);

        sink.Events.Count.ShouldBe(said, "the box was cleared in this run, not the next one");
    }

    /// <summary>
    /// A window says what it started as, whatever else the run goes on to do — and
    /// with no plugins loaded and no device, that is a platform and no sound.
    /// </summary>
    [AvaloniaFact]
    public void A_window_says_what_it_started_as()
    {
        var sink = new Collected();

        Open(settingsPath, new Usage(sink));

        var started = sink.Events.ShouldHaveSingleItem();

        started.Name.ShouldBe("started");
        started.Props["sound"].ShouldBe("none");
    }
}
