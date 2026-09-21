using Reqnroll;
using Shouldly;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Flyback.Core.Specs.Support;

namespace Flyback.Core.Specs.Steps;

/// <summary>Every preset that ships, checked as a whole so a new one is covered the day it lands.</summary>
[Binding]
public sealed class PresetSteps(Session session)
{
    [Given("every shipped preset")]
    public void GivenEveryPreset() => session.Presets = Presets.All;

    [Then("each one opens with nothing wrong")]
    public void ThenNothingWrong() => Each(patch =>
    {
        var wrong = new[] { patch.CompileForVideo(), patch.CompileForAudio() }
            .SelectMany(result => result.Issues.Where(i => i.Severity == IssueSeverity.Error))
            .Select(i => i.Message);

        return string.Join(" | ", wrong);
    });

    [Then("each one saved and opened again is the same instrument")]
    public void ThenFilesKeepIt() => Each(patch => Differences(patch, PatchIO.Read(PatchIO.ToJson(patch)).Patch));

    [Then("each one written out as text and read back is the same instrument")]
    public void ThenTextKeepsIt() => Each(patch =>
    {
        var load = PatchLanguage.Build(PatchPrinter.Print(patch, NodeCatalog.BuiltIn), NodeCatalog.BuiltIn);
        return load.Ok ? Differences(patch, load.Patch) : load.Report;
    });

    [Then("each one opens saying what it is for")]
    public void ThenEachIsDescribed()
    {
        var missing = session.Presets
            .Where(preset => preset.Build(NodeCatalog.BuiltIn).Description is not { Length: > 0 })
            .Select(preset => preset.Name)
            .ToList();

        missing.ShouldBeEmpty(string.Join(", ", missing));
    }

    /// <summary>Runs <paramref name="fault"/> on every preset and fails once, naming each preset that had one.</summary>
    private void Each(Func<Patch, string> fault)
    {
        session.Presets.ShouldNotBeEmpty();

        var faults = session.Presets
            .Select(preset => (preset.Name, Fault: fault(preset.Build(NodeCatalog.BuiltIn))))
            .Where(p => p.Fault.Length > 0)
            .Select(p => $"{p.Name}: {p.Fault}")
            .ToList();

        faults.ShouldBeEmpty(string.Join(Environment.NewLine, faults));
    }

    /// <summary>
    /// The same instrument is the same two programs, op for op. Two graphs may list
    /// their modules in another order and still be one instrument.
    /// </summary>
    private static string Differences(Patch expected, Patch actual)
    {
        var faults = new List<string>();

        if (!Ops(expected.CompileForVideo()).SequenceEqual(Ops(actual.CompileForVideo()))) faults.Add("the picture differs");
        if (!Ops(expected.CompileForAudio()).SequenceEqual(Ops(actual.CompileForAudio()))) faults.Add("the sound differs");

        return string.Join(", ", faults);
    }

    private static IEnumerable<(OpCode, int, int, int, int, float)> Ops(CompileResult result) =>
        result.Program.Ops.Select(o => (o.Code, o.Out, o.A, o.B, o.C, o.K));
}
