using System.Text.Json.Nodes;
using Flyback.Cli.Commands;
using Flyback.Cli.Models;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary><c>check --triage</c>: the complaint likeliest to be why the patch is silent or dark comes first.</summary>
public class CheckTriageTests
{
    /// <summary>A patch with two modules nobody has, the second wired to the speakers.</summary>
    private static Patch TwiceBroken()
    {
        var patch = Presets.All.Single(p => p.Name == "Plasma").Build(NodeCatalog.BuiltIn);

        patch.Nodes.Add(new NodeInstance { Id = Guid.NewGuid(), TypeId = "osc.nonesuch" });
        patch.Connect(patch.Nodes[^1].Id, 0, patch.Output.Id, NodeCatalog.OutputColorPort);
        patch.Nodes.Add(new NodeInstance { Id = Guid.NewGuid(), TypeId = "osc.nothere" });
        patch.Connect(patch.Nodes[^1].Id, 0, patch.Output.Id, NodeCatalog.OutputLeftPort);

        return patch;
    }

    private static Decisions Blaming(string word) =>
        new([new Blamer(word)], new DecisionSettings(), new Credentials(null), new ModelStore(null));

    [Fact]
    public void The_likeliest_complaint_comes_first_with_how_likely_it_is()
    {
        var patch = TwiceBroken();
        var output = new StringWriter();

        CheckCommand.Run(patch, "broken.fbk", true, output, TextWriter.Null, rank: c => CheckCommand.Rank(Blaming("nothere"), patch, c));

        var issues = JsonNode.Parse(output.ToString())!["issues"]!.AsArray();

        issues.Count.ShouldBeGreaterThan(1);
        issues[0]!["message"]!.GetValue<string>().ShouldContain("nothere");
        issues[0]!["likely"]!.GetValue<double>().ShouldBe(1);
        issues[^1]!["likely"]!.GetValue<double>().ShouldBe(0);
    }

    [Fact]
    public void Without_triage_the_compiler_s_order_stands_and_nothing_is_judged()
    {
        var output = new StringWriter();

        CheckCommand.Run(TwiceBroken(), "broken.fbk", true, output, TextWriter.Null);

        JsonNode.Parse(output.ToString())!["issues"]!.AsArray().ShouldAllBe(i => i!["likely"] == null);
    }

    private sealed class Blamer(string word) : IDecisionModel
    {
        public string Id => "blamer";

        public string Name => "Blamer";

        public int Priority => 0;

        public AssistantCredential? Credential => null;

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public string? Unavailable(DecisionConfig config) => null;

        public Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel) =>
            Task.FromResult(new Decision("blamer", request.Questions.ToDictionary(q => q.Key, q =>
            {
                var score = (Question.Score)q.Value;
                var top = score.Instructions.Contains(word, StringComparison.Ordinal) ? score.Levels.Count - 1 : 0;

                return (Answer)new Answer.Scored(top, score.Levels, [.. score.Levels.Select((_, i) => i == top ? 1.0 : 0)], 1);
            }), DecisionUsage.None));
    }
}
