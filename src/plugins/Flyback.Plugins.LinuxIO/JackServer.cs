using System.Net.Sockets;

namespace Flyback.Plugins.LinuxIO;

/// <summary>
/// Whether a JACK server is running for this user, answered from its socket and without
/// registering a client.
/// </summary>
/// <remarks>
/// Only jackd's own socket counts. PipeWire answers libjack too, but a PipeWire desktop is
/// already served by ALSA, which follows the system's default device; JACK takes over only
/// where somebody started a server.
/// </remarks>
internal static class JackServer
{
    private const string SocketFolder = "/dev/shm";

    /// <summary>The server libjack connects to unless <c>JACK_DEFAULT_SERVER</c> names another.</summary>
    public static string Name
    {
        get
        {
            var named = Environment.GetEnvironmentVariable("JACK_DEFAULT_SERVER");

            return string.IsNullOrWhiteSpace(named) ? "default" : named;
        }
    }

    /// <summary>
    /// jackd listens on <c>jack_&lt;server&gt;_&lt;uid&gt;_0</c>. Connecting tells a live server from
    /// the socket a crashed one leaves behind.
    /// </summary>
    public static bool IsRunning
    {
        get
        {
            try
            {
                var name = Name;

                if (name.IndexOfAny(['*', '?', '/']) >= 0 || !Directory.Exists(SocketFolder)) return false;

                return Directory.EnumerateFiles(SocketFolder, $"jack_{name}_*_0").Any(Answers);
            }
            catch
            {
                return false;
            }
        }
    }

    private static bool Answers(string path)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

            socket.Connect(new UnixDomainSocketEndPoint(path));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
