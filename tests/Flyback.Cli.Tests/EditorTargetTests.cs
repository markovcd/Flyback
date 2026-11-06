using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Host.Driving;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary><c>mcp --editor</c>'s end of the channel, against a server answering as the editor would.</summary>
public sealed class EditorTargetTests : IAsyncDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("fb-ch");

    private readonly List<ChannelRequest> heard = [];

    private string Name => OperatingSystem.IsWindows()
        ? $"flyback-test-{folder.Name}"
        : Path.Combine(folder.FullName, "e.sock");

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        folder.Delete(recursive: true);
    }

    [Fact]
    public async Task A_call_reaches_the_editor_and_its_reply_comes_back_with_the_picture()
    {
        await using var server = Serve();
        string? greeted = null;
        server.Connected += client => greeted = client;
        server.Start();

        await using var target = new EditorTarget(Name);
        target.Greet("Claude Code");

        using var arguments = JsonDocument.Parse("""{"type_id":"osc.sine"}""");
        var answered = await target.Call("describe_module", arguments.RootElement, CancellationToken.None);

        answered.Ok.ShouldBeTrue(answered.Text);
        answered.Text.ShouldBe("ran describe_module");
        answered.Png.ShouldBe(new byte[] { 1, 2, 3 });
        greeted.ShouldBe("Claude Code");
        heard.ShouldHaveSingleItem().Arguments!.Value.GetProperty("type_id").GetString().ShouldBe("osc.sine");
    }

    [Fact]
    public async Task Say_reaches_the_editor_as_something_said()
    {
        await using var server = Serve();
        server.Start();

        await using var target = new EditorTarget(Name);

        (await target.Say("Built a drone.", CancellationToken.None)).Ok.ShouldBeTrue();

        heard.ShouldHaveSingleItem().Say.ShouldBe("Built a drone.");
    }

    [Fact]
    public async Task A_second_program_is_turned_away_while_the_first_drives()
    {
        await using var server = Serve();
        server.Start();

        await using var first = new EditorTarget(Name);
        await first.Say("hello", CancellationToken.None);

        await using var second = new EditorTarget(Name);
        var refused = await second.Say("me too", CancellationToken.None);

        refused.Ok.ShouldBeFalse();
        refused.Text.ShouldContain("driving the editor already");
        heard.ShouldHaveSingleItem().Say.ShouldBe("hello");
    }

    [Fact]
    public async Task With_no_editor_listening_the_call_says_how_to_open_one()
    {
        await using var target = new EditorTarget(Name);

        var answered = await target.Say("anyone?", CancellationToken.None);

        answered.Ok.ShouldBeFalse();
        answered.Text.ShouldContain("Let a program on this computer edit the open patch");
    }

    [Fact]
    public async Task The_briefing_is_the_editors_own()
    {
        await using var server = Serve();
        server.Start();

        await using var target = new EditorTarget(Name);

        (await target.Briefing(CancellationToken.None)).ShouldBe("the editor's briefing");
        heard.ShouldHaveSingleItem().Briefing.ShouldBeTrue();
    }

    [Fact]
    public async Task An_editor_that_cannot_give_the_briefing_leaves_it_to_this_program()
    {
        await using var server = new ChannelServer(Name, (request, _) =>
            Task.FromResult(new ChannelReply(request.Id, false, "a request names a tool, or says something.")));
        server.Start();

        await using var target = new EditorTarget(Name);

        (await target.Briefing(CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task With_no_editor_listening_the_briefing_is_left_to_this_program()
    {
        await using var target = new EditorTarget(Name);

        (await target.Briefing(CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Initialize_hands_the_client_the_editors_briefing()
    {
        await using var server = Serve();
        server.Start();

        await using var target = new EditorTarget(Name);
        var mcp = new McpServer(new PluginCatalog([], [], NodeCatalog.BuiltIn, Presets.All, [], []), target);

        var answer = await mcp.Answer("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""", CancellationToken.None);

        var instructions = answer!["result"]!["instructions"]!.GetValue<string>();

        instructions.ShouldEndWith("the editor's briefing");
        instructions.ShouldNotContain("osc.sine");
    }

    [Fact]
    public async Task A_line_longer_than_the_cap_is_never_read_whole()
    {
        var bytes = new byte[EditorChannel.LongestLine + 2];
        Array.Fill(bytes, (byte)'a');
        bytes[^1] = (byte)'\n';

        using var lines = new ChannelLines(new MemoryStream(bytes));

        (await lines.Read(CancellationToken.None)).ShouldBeNull();
    }

    private ChannelServer Serve() => new(Name, (request, _) =>
    {
        lock (heard) heard.Add(request);

        return Task.FromResult(
            request.Tool is { } tool ? new ChannelReply(request.Id, true, $"ran {tool}", Convert.ToBase64String([1, 2, 3]))
            : request.Briefing ? new ChannelReply(request.Id, true, "the editor's briefing")
            : new ChannelReply(request.Id, true, "said."));
    });
}
