using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.Core.Graph;
using Flyback.Editor.Decide;
using Flyback.Editor.Tests.Ui;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Settings;
using Shouldly;

namespace Flyback.Editor.Tests.Decide;

/// <summary>The status line says a compile's complaints again likeliest first, unless the patch was compiled again first.</summary>
public sealed class IssueOrderingTests : EditorTest
{
    private static (IssueOrdering Ordering, ReportLine Line, Blamer Model) Built()
    {
        var model = new Blamer("volume");
        var decisions = new Decisions([model], new DecisionSettings(), new Credentials(null), new ModelStore(null));
        var line = new ReportLine();

        IssueOrdering.Pause = TimeSpan.Zero;

        return (new IssueOrdering(decisions, line), line, model);
    }

    private static string? Said(ReportLine line) => ToolTip.GetTip(line) as string;

    [AvaloniaFact]
    public async Task The_likeliest_is_said_first_and_what_leads_the_line_keeps_its_place()
    {
        var (ordering, line, _) = Built();

        line.Say(["Showing the Probe", "a Sine is not wired", "the Output's volume is 0"]);
        ordering.Order(["Showing the Probe"], ["a Sine is not wired", "the Output's volume is 0"], new Patch());

        await ordering.Pending;

        Said(line).ShouldBe("Showing the Probe  •  the Output's volume is 0  •  a Sine is not wired");
    }

    [AvaloniaFact]
    public async Task A_compile_since_cancels_the_reordering_of_the_one_before()
    {
        var (ordering, line, model) = Built();
        model.Hold = new TaskCompletionSource();

        ordering.Order([], ["a Sine is not wired", "the Output's volume is 0"], new Patch());
        var first = ordering.Pending;

        line.Say("fixed");
        ordering.Order([], ["fixed"], new Patch());

        model.Hold.SetResult();
        await first;

        Said(line).ShouldBe("fixed");
    }

    internal sealed class Blamer(string word) : IDecisionModel
    {
        public TaskCompletionSource? Hold { get; set; }

        public string Id => "blamer";

        public string Name => "Blamer";

        public int Priority => 0;

        public AssistantCredential? Credential => null;

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public string? Unavailable(DecisionConfig config) => null;

        public async Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel)
        {
            if (Hold is { } hold) await hold.Task;

            return new Decision("blamer", request.Questions.ToDictionary(q => q.Key, q =>
            {
                return (Answer)new Answer.YesNo(request.State.Contains(word, StringComparison.Ordinal) ? 1 : 0);
            }), DecisionUsage.None);
        }
    }
}
