using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Editor.Statistics;
using Flyback.Editor.Desktop.Tests.Statistics;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Editor.Desktop.Tests.Ui;

/// <summary>What the editor's own gestures add to the end of a run's report (ADR-0103).</summary>
public class UsageCountingTests : UiTest
{
    [AvaloniaFact]
    public void The_features_a_run_reached_for_are_counted_at_its_end()
    {
        var sink = new CollectedEvents();
        var usage = new Usage(sink);

        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var osc = b.Add("osc.sine", 0, 0);
        b.Add(NodeCatalog.OutputTypeId, 420, 0);

        var window = Open(b.Patch, new EditorSetup { Usage = usage });
        var editor = Editor(window);

        Click(editor, window, editor.History.Patch.Find(osc.Id)!);
        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control);
        Settle(window);

        usage.Ended();

        var ended = sink.Events.Last(e => e.Name == "ended").Props;
        ended["duplicated"].ShouldBe("1");
        ended["undone"].ShouldBe("1");
        ended["framed"].ShouldBe("1");
        ended["redone"].ShouldBe("0");
        ended["grouped"].ShouldBe("0");
    }
}
