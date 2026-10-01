using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Cli.Commands;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>
/// <c>ask</c> over an assistant that answers from a script: which patch it is
/// handed, where the answer and the conversation are written, and what it says.
/// Nothing reaches a network, the real settings or the real conversation store.
/// </summary>
public sealed class AskCommandTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-ask");

    private readonly Scripted assistant = new();

    private string SettingsPath => System.IO.Path.Combine(folder.FullName, "assistant.json");

    private ConversationStore Store => new(System.IO.Path.Combine(folder.FullName, "sessions"));

    public AskCommandTests() => new AssistantSettings { Provider = "scripted" }.Save(SettingsPath);

    public void Dispose() => folder.Delete(recursive: true);

    [Fact]
    public async Task A_file_that_does_not_exist_yet_is_written_with_the_answer()
    {
        var (code, _, complained) = await Ask("field.fbk", "a gray field");

        code.ShouldBe(Exit.Ok, complained);

        var written = PatchIO.Read(File.ReadAllText(Path("field.fbk"))).Patch;

        written.Nodes.ShouldContain(node => node.TypeId == "value");
        written.Connections.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_next_ask_carries_the_conversation_on()
    {
        await Ask("field.fbk", "a gray field");
        var (code, said, _) = await Ask("field.fbk", "now brighter");

        code.ShouldBe(Exit.Ok);
        assistant.Resumed.ShouldHaveSingleItem().ShouldContain("a gray field");
        said.ShouldContain("Carrying on");
        said.ShouldContain("heard 2 messages");
    }

    [Fact]
    public async Task Fresh_starts_a_new_conversation_however_much_was_saved()
    {
        await Ask("field.fbk", "a gray field");
        await Ask("field.fbk", "now brighter", options => options with { Fresh = true });

        assistant.Resumed.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_bundle_carries_its_conversation_inside_it()
    {
        await Ask("field.fbkb", "a gray field");

        using var zip = ZipFile.OpenRead(Path("field.fbkb"));
        using var reading = new StreamReader(zip.GetEntry(PatchBundle.ConversationEntry).ShouldNotBeNull().Open());

        var saved = SavedConversation.Read(reading.ReadToEnd()).ShouldNotBeNull();

        saved.Provider.ShouldBe("scripted");
        saved.Turns.ShouldBe(1);
        saved.Transcript.ShouldContain(line => line.Voice == Voice.You && line.Text == "a gray field");
    }

    [Fact]
    public async Task A_turn_that_only_talks_leaves_the_patch_file_as_it_was()
    {
        await Ask("field.fbk", "a gray field");

        var longAgo = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path("field.fbk"), longAgo);

        var (code, said, _) = await Ask("field.fbk", "just talk");

        code.ShouldBe(Exit.Ok);
        File.GetLastWriteTimeUtc(Path("field.fbk")).ShouldBe(longAgo);
        said.ShouldContain("Kept the conversation");
        assistant.Resumed.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Out_writes_the_answer_elsewhere_and_leaves_the_patch_alone()
    {
        await Ask("field.fbk", "a gray field");
        var before = File.ReadAllText(Path("field.fbk"));

        var (code, _, _) = await Ask("field.fbk", "again", into: "copy.fbks");

        code.ShouldBe(Exit.Ok);
        File.ReadAllText(Path("field.fbk")).ShouldBe(before);
        File.ReadAllText(Path("copy.fbks")).ShouldContain("value");
    }

    [Fact]
    public async Task A_failed_turn_fails_the_command_and_leaves_the_patch_as_it_was()
    {
        await Ask("field.fbk", "a gray field");
        var before = File.ReadAllText(Path("field.fbk"));

        var (code, said, _) = await Ask("field.fbk", "fail please");

        code.ShouldBe(Exit.Failed);
        said.ShouldContain("! asked to fail");
        File.ReadAllText(Path("field.fbk")).ShouldBe(before);
    }

    [Fact]
    public async Task Json_writes_one_object_a_line_and_each_names_its_kind()
    {
        var (_, said, _) = await Ask("field.fbk", "a gray field", options => options with { Json = true });

        var kinds = said.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line)!["kind"]!.GetValue<string>())
            .ToList();

        kinds[0].ShouldBe("started");
        kinds.ShouldContain("call");
        kinds.ShouldContain("proposed");
        kinds[^1].ShouldBe("wrote");
    }

    [Fact]
    public async Task It_says_up_front_what_the_assistant_can_take_in()
    {
        var (_, said, _) = await Ask("field.fbk", "a gray field");

        said.ShouldContain("It cannot see the picture, and cannot hear the sound.");
    }

    [Fact]
    public async Task Every_tool_call_is_shown_with_its_arguments()
    {
        var (_, said, _) = await Ask("field.fbk", "a gray field");

        said.ShouldContain("→ add_module");
        said.ShouldContain("\"type_id\":\"value\"");
    }

    [Fact]
    public async Task Seen_keeps_each_picture_it_looked_at()
    {
        var (_, said, _) = await Ask("field.fbk", "look at it", options => options with { Seen = new DirectoryInfo(Path("seen")) });

        var kept = Directory.GetFiles(Path("seen")).ShouldHaveSingleItem();

        File.ReadAllBytes(kept).ShouldBe(Scripted.Png);
        said.ShouldContain(kept);
    }

    [Fact]
    public async Task With_no_message_it_reads_one_from_standard_input()
    {
        var (code, _, _) = await Ask("field.fbk", null, input: "a gray field from a pipe");

        code.ShouldBe(Exit.Ok);
        assistant.Heard.ShouldHaveSingleItem().ShouldBe("a gray field from a pipe");
    }

    [Fact]
    public async Task An_empty_pipe_is_refused_before_anything_is_asked()
    {
        var (code, _, complained) = await Ask("field.fbk", null, input: "  ");

        code.ShouldBe(Exit.Failed);
        complained.ShouldContain("say what to ask");
        assistant.Heard.ShouldBeEmpty();
    }

    [Fact]
    public async Task At_a_terminal_it_asks_line_by_line_until_an_empty_one()
    {
        var (code, _, _) = await Ask("field.fbk", null, console: "a gray field\nnow brighter\n\nnever sent\n");

        code.ShouldBe(Exit.Ok);
        assistant.Heard.ShouldBe(["a gray field", "now brighter"]);
    }

    [Fact]
    public async Task Set_lays_a_setting_over_the_saved_ones_for_this_run()
    {
        await Ask("field.fbk", "a gray field", options => options with { Set = ["model=small"] });

        assistant.Values.ShouldNotBeNull().Text("model").ShouldBe("small");
        AssistantSettings.Load(SettingsPath).Of("scripted").All.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_setting_that_is_not_a_pair_is_refused()
    {
        var (code, _, complained) = await Ask("field.fbk", "a gray field", options => options with { Set = ["model"] });

        code.ShouldBe(Exit.Failed);
        complained.ShouldContain("key=value");
    }

    [Fact]
    public async Task An_assistant_that_is_not_installed_is_refused_naming_those_that_are()
    {
        var (code, _, complained) = await Ask("field.fbk", "a gray field", options => options with { Provider = "nobody" });

        code.ShouldBe(Exit.Failed);
        complained.ShouldContain("no assistant called 'nobody'");
        complained.ShouldContain("Installed: scripted.");
    }

    [Fact]
    public void A_preset_needs_somewhere_to_write_to()
    {
        var error = new StringWriter();

        AskCommand.Open(Catalog(), null, "Plasma", null, error).ShouldBeNull();
        error.ToString().ShouldContain("--out");
    }

    [Fact]
    public void A_file_it_cannot_write_is_refused_by_its_extension()
    {
        var error = new StringWriter();

        AskCommand.Open(Catalog(), new FileInfo(Path("field.txt")), null, null, error).ShouldBeNull();
        error.ToString().ShouldContain("the extension says what to write");
    }

    private string Path(string name) => System.IO.Path.Combine(folder.FullName, name);

    private PluginCatalog Catalog() => new([], [], NodeCatalog.BuiltIn, Presets.All, [], [assistant]);

    private async Task<(int Code, string Said, string Complained)> Ask(
        string patch,
        string? message,
        Func<AskOptions, AskOptions>? adjust = null,
        string? into = null,
        string input = "",
        string? console = null)
    {
        var said = new StringWriter();
        var complained = new StringWriter();
        var plugins = Catalog();
        var store = Store;

        if (AskCommand.Open(plugins, new FileInfo(Path(patch)), null, into is null ? null : new FileInfo(Path(into)), complained, store)
            is not { } about)
        {
            return (Exit.Failed, said.ToString(), complained.ToString());
        }

        var options = new AskOptions(message, null, [], false, false, null, false, null);

        var code = await AskCommand.Run(
            plugins,
            about,
            adjust?.Invoke(options) ?? options,
            said,
            complained,
            new StringReader(input),
            console is null ? null : new StringReader(console),
            CancellationToken.None,
            SettingsPath,
            store,
            Path("logs"));

        return (code, said.ToString(), complained.ToString());
    }

    /// <summary>
    /// Builds a gray field through the workbench, as a provider would, and remembers
    /// every message across a saved conversation.
    /// </summary>
    private sealed class Scripted : IPatchAssistant
    {
        public static readonly byte[] Png = [0x89, 0x50, 0x4e, 0x47];

        public List<string> Resumed { get; } = [];

        public List<string> Heard { get; } = [];

        public SettingValues? Values { get; private set; }

        public string Id => "scripted";

        public string Name => "Scripted";

        public int Priority => 0;

        public AssistantCredential Credential => new("FLYBACK_SCRIPTED_KEY", "No key is needed.");

        public Uri? Endpoint(SettingValues values) => new("https://assistant.test/");

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public AssistantSenses Senses(SettingValues values) => new(false);

        public string? Unavailable(AssistantConfig config) => null;

        public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config)
        {
            Values = config.Values;

            return new Session(this, workbench, []);
        }

        public IPatchSession? Resume(PatchWorkbench workbench, AssistantConfig config, string saved)
        {
            Resumed.Add(saved);
            Values = config.Values;

            return new Session(this, workbench, JsonSerializer.Deserialize<List<string>>(saved) ?? []);
        }

        private sealed class Session(Scripted owner, PatchWorkbench workbench, List<string> history) : IPatchSession
        {
            private static readonly (string Tool, string Arguments)[] Field =
            [
                ("add_module", """{"type_id":"value","handle":"knob1","knobs":[{"port":"value","value":0.5}]}"""),
                ("connect", """{"from":"knob1","to":"output1","to_port":"color"}"""),
                ("propose", """{"summary":"a flat gray field"}"""),
            ];

            public async IAsyncEnumerable<PatchEvent> Ask(string instruction, [EnumeratorCancellation] CancellationToken cancel)
            {
                history.Add(instruction);
                owner.Heard.Add(instruction);

                yield return new PatchEvent.Said($"heard {history.Count} messages");

                if (instruction.Contains("fail", StringComparison.Ordinal))
                {
                    yield return new PatchEvent.Failed("asked to fail, so it did.");
                    yield break;
                }

                if (instruction.Contains("talk", StringComparison.Ordinal)) yield break;

                if (instruction.Contains("look", StringComparison.Ordinal))
                {
                    yield return new PatchEvent.Saw(Png, "a frame");
                    yield break;
                }

                foreach (var (tool, arguments) in Field)
                {
                    var outcome = await workbench
                        .InvokeAsync(tool, JsonSerializer.Deserialize<JsonElement>(arguments), cancel)
                        .ConfigureAwait(false);

                    yield return new PatchEvent.Did(outcome.Text);
                }

                yield return new PatchEvent.Proposed(workbench.Snapshot(), workbench.ProposalSummary);
            }

            public string? Save() => JsonSerializer.Serialize(history);

            public void Dispose()
            {
            }
        }
    }
}
