using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Flyback.Cli.Commands;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>Asking a decision model from the command line, listing them, and downloading one, with no network and no real settings.</summary>
public sealed class DecideCommandTests : IDisposable
{
    private readonly string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "flyback-decide-" + Guid.NewGuid().ToString("N"))).FullName;

    private string SettingsPath => Path.Combine(root, "settings.json");

    private string Models => Path.Combine(root, "models");

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public async Task A_yes_no_question_is_answered_in_prose()
    {
        var (code, said, _) = await Decide(new Options { State = "we were billed twice", YesNo = "Is this about money?" });

        code.ShouldBe(Exit.Ok);
        said.ShouldContain("Answered by Echo");
        said.ShouldContain("yes_no: yes, 0.90 that it holds");
    }

    [Fact]
    public async Task Json_is_the_wire_format_s_answer()
    {
        var (code, said, _) = await Decide(new Options
        {
            State = "loud",
            Choice = "Which?",
            Choices = ["a=the first", "b=the second"],
            Json = true,
        });

        code.ShouldBe(Exit.Ok);
        var answer = JsonNode.Parse(said)!["answers"]!["choice"]!;
        answer["type"]!.GetValue<string>().ShouldBe("choice");
        answer["choice"]!.GetValue<string>().ShouldBe("b");
    }

    [Fact]
    public async Task A_state_of_a_dash_is_read_from_standard_input()
    {
        var echo = new Echo();

        var (code, _, _) = await Decide(new Options { State = "-", YesNo = "Is it?", Input = "from the pipe" }, echo);

        code.ShouldBe(Exit.Ok);
        echo.Asked.ShouldHaveSingleItem().State.ShouldBe("from the pipe");
    }

    [Fact]
    public async Task A_file_of_questions_is_asked_whole()
    {
        var file = Path.Combine(root, "questions.json");
        File.WriteAllText(file, """{"questions":{"level":{"type":"score","instructions":"How loud?","criteria":["quiet","loud"]}}}""");

        var (code, said, _) = await Decide(new Options { State = "s", Ask = new FileInfo(file) });

        code.ShouldBe(Exit.Ok);
        said.ShouldContain("level: quiet (score 0.00 of 0–1)");
    }

    [Fact]
    public async Task Nothing_asked_says_how_to_ask()
    {
        var (code, _, complained) = await Decide(new Options { State = "s" });

        code.ShouldBe(Exit.Failed);
        complained.ShouldContain("--yes-no");
    }

    [Fact]
    public async Task A_model_turned_off_in_the_settings_is_not_asked()
    {
        new DecisionSettings { Model = DecisionSettings.Off }.Save(SettingsPath);

        var (code, _, complained) = await Decide(new Options { State = "s", YesNo = "Is it?" });

        code.ShouldBe(Exit.Failed);
        complained.ShouldContain("No decision model is in use");
    }

    [Fact]
    public async Task Status_lists_each_model_and_what_it_lacks()
    {
        var (code, said, _) = await Decide(new Options { Status = true, Json = true }, new Echo(), new Downloaded());

        code.ShouldBe(Exit.Ok);

        var rows = JsonNode.Parse(said)!.AsArray();
        rows.Select(r => r!["id"]!.GetValue<string>()).ShouldBe(["downloaded", "echo"]);
        rows[0]!["prepared"]!.GetValue<bool>().ShouldBeFalse();
        rows[0]!["unavailable"]!.GetValue<string>().ShouldContain("not downloaded yet");
        rows[0]!["chosen"]!.GetValue<bool>().ShouldBeTrue("nobody chose, and it sends nothing anywhere");
    }

    [Fact]
    public async Task A_model_is_downloaded_only_after_a_yes()
    {
        var network = new Network();

        var (code, said, _) = await Decide(new Options { Prepare = true, Answer = "n" }, network, new Downloaded());

        code.ShouldBe(Exit.Failed);
        said.ShouldContain("Nothing was downloaded");
        network.Fetched.ShouldBe(0);
        File.Exists(Path.Combine(Models, "downloaded", "weights.bin")).ShouldBeFalse();
    }

    [Fact]
    public async Task With_nobody_to_ask_a_download_needs_yes()
    {
        var (code, _, complained) = await Decide(new Options { Prepare = true }, new Network(), new Downloaded());

        code.ShouldBe(Exit.Failed);
        complained.ShouldContain("--yes");
    }

    [Fact]
    public async Task A_yes_downloads_the_files_and_the_model_then_answers()
    {
        var network = new Network();
        var model = new Downloaded();

        var (code, said, _) = await Decide(new Options { Prepare = true, Answer = "yes" }, network, model);

        code.ShouldBe(Exit.Ok);
        said.ShouldContain("is ready");
        File.ReadAllBytes(Path.Combine(Models, "downloaded", "weights.bin")).ShouldBe(Network.Weights);

        (await Decide(new Options { State = "s", YesNo = "Is it?" }, network, model)).Code.ShouldBe(Exit.Ok);
    }

    [Fact]
    public async Task A_file_that_does_not_hash_as_pinned_is_refused_and_not_kept()
    {
        var network = new Network { Tampered = true };

        var (code, _, complained) = await Decide(new Options { Prepare = true, Yes = true }, network, new Downloaded());

        code.ShouldBe(Exit.Failed);
        complained.ShouldContain("does not hash as it was pinned");
        File.Exists(Path.Combine(Models, "downloaded", "weights.bin")).ShouldBeFalse();
    }

    private Task<(int Code, string Said, string Complained)> Decide(Options options, params IDecisionModel[] models) =>
        Decide(options, new Network(), models);

    private async Task<(int Code, string Said, string Complained)> Decide(Options options, Network network, params IDecisionModel[] models)
    {
        var said = new StringWriter();
        var complained = new StringWriter();

        var plugins = new PluginCatalog([], [], NodeCatalog.BuiltIn, Presets.All, [], decisionModels: models.Length > 0 ? models : [new Echo()]);

        using var http = new HttpClient(network);

        var code = await DecideCommand.Run(
            plugins,
            new DecideOptions(options.State, options.Ask, null, options.YesNo, options.Choice, options.Choices, null, [], options.Status, options.Prepare, options.Yes, options.Json),
            new StringReader(options.Input),
            said,
            complained,
            CancellationToken.None,
            SettingsPath,
            Models,
            options.Answer is null ? null : new StringReader(options.Answer),
            http);

        return (code, said.ToString(), complained.ToString());
    }

    private sealed record Options
    {
        public string? State { get; init; }
        public FileInfo? Ask { get; init; }
        public string? YesNo { get; init; }
        public string? Choice { get; init; }
        public IReadOnlyList<string> Choices { get; init; } = [];
        public bool Status { get; init; }
        public bool Prepare { get; init; }
        public bool Yes { get; init; }
        public bool Json { get; init; }
        public string Input { get; init; } = "";
        public string? Answer { get; init; }
    }

    /// <summary>Says yes at 0.9, and picks a choice's last option.</summary>
    private class Echo : IDecisionModel
    {
        public List<DecisionRequest> Asked { get; } = [];

        public virtual string Id => "echo";

        public virtual string Name => "Echo";

        public int Priority => 0;

        public AssistantCredential? Credential => null;

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public virtual string? Unavailable(DecisionConfig config) => null;

        public Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel)
        {
            Asked.Add(request);

            return Task.FromResult(new Decision("echo-1", request.Questions.ToDictionary(q => q.Key, q => q.Value switch
            {
                Question.Choice c => (Answer)new Answer.Chosen(c.Options[^1].Label, c.Options.ToDictionary(o => o.Label, _ => 1.0 / c.Options.Count), 0.5),
                Question.Score s => new Answer.Scored(0, s.Levels, [.. s.Levels.Select((_, i) => i == 0 ? 1.0 : 0)], 1),
                _ => new Answer.YesNo(0.9),
            }), DecisionUsage.None));
        }
    }

    /// <summary>A model that runs here once one file is downloaded.</summary>
    private sealed class Downloaded : Echo, IPreparedModel
    {
        public override string Id => "downloaded";

        public override string Name => "Downloaded";

        public IReadOnlyList<ModelFile> Needs { get; } =
            [new ModelFile(new Uri("https://models.test/weights.bin"), "weights.bin", Convert.ToHexStringLower(SHA256.HashData(Network.Weights)), Network.Weights.Length)];

        public bool Prepared(string folder) => File.Exists(Path.Combine(folder, "weights.bin"));
    }

    private sealed class Network : HttpMessageHandler
    {
        public static readonly byte[] Weights = Encoding.ASCII.GetBytes("these are the weights");

        public bool Tampered { get; init; }

        public int Fetched { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Fetched++;

            var body = Tampered ? Encoding.ASCII.GetBytes("these are not weights") : Weights;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }
}
