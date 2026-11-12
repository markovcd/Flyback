using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Editor.Capture;
using Flyback.Editor.Composition;
using Flyback.Editor.Controls;
using Flyback.Editor.Inspect;
using Flyback.Editor.Shell;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Capture;

/// <summary>Only a desktop records: a phone, a tablet or a page has no shortcut, Recording tab or help row for it.</summary>
public sealed class DeviceRecordTests : EditorTest
{
    private MainWindow Open(EditorPlace place)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        b.Add("osc.sine", 0, 0, (1, 3f));
        b.Add(OutputModule.TypeId, 700, 0);

        var window = NewMainWindow(new EditorSetup { Host = new() { Place = place } });

        window.Show();
        Settle(window);
        Dispatcher.UIThread.RunJobs();

        Editor(window).History.Open(b.Patch);
        Settle(window);

        return window;
    }

    [AvaloniaFact]
    public void Ctrl_R_records_nothing_on_a_device()
    {
        var window = Open(EditorPlace.Device);

        window.KeyPress(Key.R, RawInputModifiers.Control, PhysicalKey.R, "r");
        Settle(window);

        var recording = window.Recording;
        recording.Counting.ShouldBeFalse();
        recording.InHand.ShouldBeFalse();
        All<ModalOverlay>(window).ShouldBeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(EditorPlace.Desktop, true)]
    [InlineData(EditorPlace.Device, false)]
    public void Only_a_desktop_has_the_Recording_tab(EditorPlace place, bool listed)
    {
        var dialog = OpenSettings(Open(place));
        var tabs = All<TabControl>(dialog).Single(t => t.Name == "settingsTabs");

        tabs.Items.OfType<TabItem>().Any(item => (item.Header as TextBlock)?.Text == "Recording").ShouldBe(listed);
    }

    [AvaloniaTheory]
    [InlineData(EditorPlace.Desktop, true, true)]
    [InlineData(EditorPlace.Desktop, false, true)]
    [InlineData(EditorPlace.Device, true, false)]
    [InlineData(EditorPlace.Device, false, false)]
    [InlineData(EditorPlace.Page, true, false)]
    [InlineData(EditorPlace.Page, false, false)]
    public void The_help_names_Record_only_where_it_is_offered(EditorPlace place, bool fingers, bool named) =>
        InspectorHelp.Shortcuts(new EditorHost { Place = place }, fingers)
            .SelectMany(g => g.Rows)
            .Any(r => r.Title == "Record")
            .ShouldBe(named);
}
