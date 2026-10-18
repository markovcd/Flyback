using System.Text.Json;
using Flyback.Cli.Commands;
using Flyback.Cli.Common;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>
/// <c>tool</c>: one workbench call on a patch file, and the undo history it keeps
/// in a folder of the test's own.
/// </summary>
public sealed class ToolCommandTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-tool");

    private static readonly PluginCatalog Catalog = new([], [], NodeCatalog.BuiltIn, Presets.All, [], []);

    private ToolHistory History => new(Path("history"));

    private string SettingsPath => Path("settings.json");

    public void Dispose() => folder.Delete(recursive: true);

    [Fact]
    public void The_list_in_json_is_every_offered_tool_with_an_object_schema()
    {
        var output = new StringWriter();

        ToolCommand.List(Catalog, json: true, output, SettingsPath).ShouldBe(Exit.Ok);

        var tools = JsonDocument.Parse(output.ToString()).RootElement.EnumerateArray().ToList();

        tools.Select(tool => tool.GetProperty("name").GetString()).ShouldContain("write_patch");
        tools.Select(tool => tool.GetProperty("name").GetString()).ShouldNotContain("listen");
        tools.ShouldAllBe(tool => tool.GetProperty("inputSchema").GetProperty("type").GetString() == "object");
    }

    [Fact]
    public void The_briefing_names_the_modules()
    {
        var output = new StringWriter();

        ToolCommand.Briefing(Catalog, output, SettingsPath).ShouldBe(Exit.Ok);

        output.ToString().ShouldContain("osc.sine");
    }

    [Fact]
    public void The_skill_is_a_skill_file_naming_the_commands()
    {
        ToolCommand.Skill.ShouldStartWith("---\nname: flyback\n");
        ToolCommand.Skill.ShouldContain("flyback-cli tool undo");
    }

    [Fact]
    public async Task An_edit_writes_the_patch_and_undo_and_redo_step_through_it()
    {
        await Call("p.fbk", "write_patch", Source("rings() |> out.color"));
        var first = File.ReadAllText(Path("p.fbk"));

        await Call("p.fbk", "edit_patch", Adding("sine1 = sine()"));
        PatchIO.Read(File.ReadAllText(Path("p.fbk"))).Patch.Nodes.ShouldContain(node => node.TypeId == "osc.sine");

        Step(ToolCommand.Undo).ShouldBe(Exit.Ok);
        File.ReadAllText(Path("p.fbk")).ShouldBe(first);

        Step(ToolCommand.Redo).ShouldBe(Exit.Ok);
        PatchIO.Read(File.ReadAllText(Path("p.fbk"))).Patch.Nodes.ShouldContain(node => node.TypeId == "osc.sine");
    }

    [Fact]
    public async Task Editing_a_text_patch_keeps_what_its_author_wrote()
    {
        File.WriteAllText(Path("p.fbks"), "# a ring\nrings() |> out.color\n");

        var (code, _, complained) = await Call("p.fbks", "edit_patch", Edit("rings()", "rings(freq: 9)"));

        code.ShouldBe(Exit.Ok, complained);
        File.ReadAllText(Path("p.fbks")).ShouldBe("# a ring\nrings(freq: 9) |> out.color\n");
    }

    [Fact]
    public async Task A_handle_chosen_in_one_call_names_the_module_in_the_next()
    {
        await Call("p.fbks", "write_patch", Source("rings() |> out.color"));
        await Call("p.fbks", "edit_patch", Edit("rings() |> out.color", "rings() |> out.color\nlet lfo = sine()"));

        var (code, said, complained) = await Call("p.fbks", "edit_patch", Edit("lfo = sine()", "lfo = sine(freq: 2)"));

        code.ShouldBe(Exit.Ok, complained);
        said.ShouldContain("edit 3");
        File.ReadAllText(Path("p.fbks")).ShouldContain("sine(freq: 2)");
    }

    [Fact]
    public async Task A_refused_call_says_why_and_writes_nothing()
    {
        await Call("p.fbk", "write_patch", Source("rings() |> out.color"));
        var before = File.ReadAllText(Path("p.fbk"));

        var (code, _, complained) = await Call("p.fbk", "edit_patch", Edit("nobody |> out.left", "nobody |> out.right"));

        code.ShouldBe(Exit.Problems);
        complained.ShouldContain("edit_patch refused");
        File.ReadAllText(Path("p.fbk")).ShouldBe(before);
        History.Find(Path("p.fbk")).ShouldNotBeNull().Steps.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Arguments_that_are_not_an_object_are_refused_before_the_patch_is_read()
    {
        var (code, _, complained) = await Call("p.fbk", "describe_patch", "[1]");

        code.ShouldBe(Exit.Failed);
        complained.ShouldContain("--args is a JSON object");
    }

    [Fact]
    public async Task Undoing_every_edit_to_a_new_file_takes_the_file_away()
    {
        await Call("p.fbk", "write_patch", Source("rings() |> out.color"));

        Step(ToolCommand.Undo).ShouldBe(Exit.Ok);

        File.Exists(Path("p.fbk")).ShouldBeFalse();
    }

    [Fact]
    public async Task Undo_to_a_step_goes_back_past_several_edits()
    {
        await Call("p.fbk", "write_patch", Source("rings() |> out.color"));
        var first = File.ReadAllText(Path("p.fbk"));

        await Call("p.fbk", "edit_patch", Adding("sine1 = sine()"));
        await Call("p.fbk", "edit_patch", Adding("saw1 = saw()"));

        Step(ToolCommand.Undo, to: 1).ShouldBe(Exit.Ok);

        File.ReadAllText(Path("p.fbk")).ShouldBe(first);
        History.Find(Path("p.fbk")).ShouldNotBeNull().At.ShouldBe(1);
    }

    [Fact]
    public async Task A_patch_changed_elsewhere_has_no_history_to_undo_and_its_next_edit_starts_one()
    {
        await Call("p.fbk", "write_patch", Source("rings() |> out.color"));
        File.WriteAllText(Path("p.fbk"), PatchIO.ToJson(Presets.Empty(NodeCatalog.BuiltIn), NodeCatalog.BuiltIn));

        var error = new StringWriter();
        ToolCommand.Undo(History, new FileInfo(Path("p.fbk")), null, false, new StringWriter(), error).ShouldBe(Exit.Failed);
        error.ToString().ShouldContain("changed since the last tool call");

        var (_, _, complained) = await Call("p.fbk", "write_patch", Source("rings() |> out.color"));

        complained.ShouldContain("starts again");
        History.Find(Path("p.fbk")).ShouldNotBeNull().Steps.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_new_edit_after_an_undo_drops_what_was_undone()
    {
        await Call("p.fbk", "write_patch", Source("rings() |> out.color"));
        await Call("p.fbk", "edit_patch", Adding("sine1 = sine()"));
        Step(ToolCommand.Undo);

        await Call("p.fbk", "edit_patch", Adding("saw1 = saw()"));

        var log = History.Find(Path("p.fbk")).ShouldNotBeNull();
        log.Steps.Count.ShouldBe(2);
        log.Steps[1].Arguments.ShouldContain("saw()");
    }

    [Fact]
    public async Task History_lists_each_edit_and_marks_the_undone()
    {
        await Call("p.fbk", "write_patch", Source("rings() |> out.color"));
        await Call("p.fbk", "edit_patch", Adding("sine1 = sine()"));
        Step(ToolCommand.Undo);

        var output = new StringWriter();
        ToolCommand.History(History, new FileInfo(Path("p.fbk")), null, true, output, new StringWriter()).ShouldBe(Exit.Ok);

        var listed = JsonDocument.Parse(output.ToString()).RootElement;

        listed.GetProperty("step").GetInt32().ShouldBe(1);
        listed.GetProperty("steps")[1].GetProperty("tool").GetString().ShouldBe("edit_patch");
        listed.GetProperty("steps")[1].GetProperty("undone").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_rendered_picture_is_written_where_it_was_asked_for()
    {
        await Call("p.fbk", "write_patch", Source("rings() |> out.color"));

        var (code, said, complained) = await Call("p.fbk", "render", "{}", picture: Path("seen.png"));

        code.ShouldBe(Exit.Ok, complained);
        said.ShouldContain("seen.png");
        File.ReadAllBytes(Path("seen.png"))[..4].ShouldBe(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' });
        History.Find(Path("p.fbk")).ShouldNotBeNull().Steps.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Json_says_what_the_call_did()
    {
        var (_, said, _) = await Call("p.fbk", "write_patch", Source("rings() |> out.color"), json: true);

        var line = JsonDocument.Parse(said).RootElement;

        line.GetProperty("ok").GetBoolean().ShouldBeTrue();
        line.GetProperty("wrote").GetBoolean().ShouldBeTrue();
        line.GetProperty("step").GetInt32().ShouldBe(1);
    }

    private static string Source(string text) => JsonSerializer.Serialize(new { source = text });

    private static string Edit(string find, string replace) =>
        JsonSerializer.Serialize(new { edits = new[] { new { find, replace } } });

    /// <summary>An edit adding one module, bound with <c>let</c>, beside the Rings a printing of the patch names <c>rings1</c>.</summary>
    private static string Adding(string binding) => Edit("rings1 |> out.color", $"rings1 |> out.color\nlet {binding}");

    private async Task<(int Code, string Said, string Complained)> Call(
        string patch, string tool, string arguments, string? picture = null, bool json = false)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var code = await ToolCommand.Call(
            Catalog,
            History,
            new FileInfo(Path(patch)),
            tool,
            arguments,
            picture is null ? null : new FileInfo(picture),
            null,
            json,
            output,
            error,
            CancellationToken.None,
            SettingsPath);

        return (code, output.ToString(), error.ToString());
    }

    private int Step(Func<ToolHistory, FileInfo, int?, bool, TextWriter, TextWriter, int> run, int? to = null) =>
        run(History, new FileInfo(Path("p.fbk")), to, false, new StringWriter(), new StringWriter());

    private string Path(string name) => System.IO.Path.Combine(folder.FullName, name);
}
