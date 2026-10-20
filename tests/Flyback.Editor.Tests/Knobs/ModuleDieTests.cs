using Avalonia.Headless.XUnit;
using Flyback.Core.Graph;
using Flyback.Editor.Knobs;
using Flyback.Editor.Settings;
using Flyback.Host;
using Shouldly;

namespace Flyback.Editor.Tests.Knobs;

/// <summary>Which of a module's knobs a roll moves, and where a stepped one lands.</summary>
public class ModuleDieTests : EditorTest
{
    [AvaloniaFact]
    public void A_stepped_knob_lands_on_a_whole_step()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var note = b.Add("audio.note", 200, 40);
        var window = Open(b.Patch);
        var editor = Editor(window);
        var die = Service<ModuleDie>(window);
        var pitch = NodeCatalog.Get("audio.note")!.Inputs.ToList().FindIndex(p => p.Name == "note");

        Service<OutputSettingRepository>(window).Current.Randomize = new RandomizeSettings { Amount = 1, GlideSeconds = 0 };
        die.Random = new Random(5);
        editor.Selection.Select(note.Id);

        for (var i = 0; i < 20; i++)
        {
            die.Roll();

            var landed = editor.History.Patch.Find(note.Id)!.InputValues[pitch];
            landed.ShouldBe(MathF.Round(landed));
        }
    }

    [AvaloniaFact]
    public void The_Output_has_no_knob_a_roll_moves()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var output = b.Add(NodeCatalog.OutputTypeId, 700, 40);

        ModuleDie.Rollable(b.Patch, output).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void A_socket_with_no_range_of_its_own_is_not_rolled()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var add = b.Add("math.add", 200, 40);

        ModuleDie.Rollable(b.Patch, add).ShouldBeEmpty();
    }
}
