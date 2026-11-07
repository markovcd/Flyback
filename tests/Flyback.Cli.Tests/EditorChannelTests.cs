using Flyback.Cli.Common;
using Flyback.Host.Driving;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>Where the editor and <c>flyback-cli</c> meet, whatever environment each was started with.</summary>
public sealed class EditorChannelTests : IAsyncDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("fb-ch");

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        folder.Delete(recursive: true);
    }

    [Fact]
    public void Outside_Windows_the_socket_has_a_full_path_so_TMPDIR_never_moves_it()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows names a pipe, not a socket.");

        EditorChannel.Locate(null, "/home/someone", "someone").ShouldStartWith("/");
    }

    [Fact]
    public void On_a_Mac_the_socket_sits_in_the_users_caches()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The path is the Mac's.");

        EditorChannel.Locate(null, "/Users/someone", "someone").ShouldBe("/Users/someone/Library/Caches/Flyback/flyback-editor.sock");
    }

    [Fact]
    public void On_Linux_the_socket_sits_in_the_runtime_folder_named()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The runtime folder is Linux's.");

        EditorChannel.Locate(folder.FullName, "/home/someone", "someone").ShouldBe(Path.Combine(folder.FullName, "flyback-editor.sock"));
    }

    [Fact]
    public void On_Linux_a_client_that_drops_the_runtime_folder_still_finds_the_same_socket()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The runtime folder is Linux's.");
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        Assert.SkipUnless(runtime is { Length: > 0 } && runtime.StartsWith("/run/user/", StringComparison.Ordinal) && Directory.Exists(runtime),
            "Needs a login session's runtime folder.");

        EditorChannel.Locate(null, "/home/someone", "someone").ShouldBe(EditorChannel.Locate(runtime, "/home/someone", "someone"));
    }

    [Fact]
    public async Task The_editor_makes_the_sockets_folder_when_it_is_missing()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows names a pipe, not a socket.");

        var name = Path.Combine(folder.FullName, "Caches", "Flyback", "e.sock");

        await using var server = new ChannelServer(name, (request, _) => Task.FromResult(new ChannelReply(request.Id, true, "heard")));
        server.Start();

        await using var target = new EditorTarget(name);

        (await target.Briefing(CancellationToken.None)).ShouldBe("heard");
    }
}
