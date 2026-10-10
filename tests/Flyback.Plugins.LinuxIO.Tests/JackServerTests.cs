using System.Net.Sockets;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.LinuxIO.Tests;

/// <summary>How the JACK output tells a live server from a dead or wedged one, on sockets standing in for jackd's.</summary>
public sealed class JackServerTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"flyback-jack-{Guid.NewGuid():N}");

    [Fact]
    public void A_server_that_takes_clients_answers()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "a non-blocking connect completes at once only on Linux, the one place the output asks");

        using var listener = Listening(backlog: 4);

        JackServer.Answers(path).ShouldBeTrue();
    }

    [Fact]
    public void The_socket_a_dead_server_left_does_not_answer()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "a non-blocking connect completes at once only on Linux, the one place the output asks");

        Listening(backlog: 4).Dispose();

        JackServer.Answers(path).ShouldBeFalse();
    }

    /// <summary>A server that has stopped accepting, its backlog full, is not waited on.</summary>
    [Fact]
    public void A_server_too_wedged_to_take_a_client_does_not_answer_and_is_not_waited_on()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "a full backlog refuses rather than waits only on Linux");

        using var listener = Listening(backlog: 0);
        var queued = new List<Socket>();

        try
        {
            while (queued.Count < 1000)
            {
                var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified) { Blocking = false };
                queued.Add(client);

                try
                {
                    client.Connect(new UnixDomainSocketEndPoint(path));
                }
                catch (SocketException)
                {
                    break;
                }
            }

            JackServer.Answers(path).ShouldBeFalse();
        }
        finally
        {
            foreach (var client in queued) client.Dispose();
        }
    }

    public void Dispose()
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private Socket Listening(int backlog)
    {
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(backlog);

        return listener;
    }
}
