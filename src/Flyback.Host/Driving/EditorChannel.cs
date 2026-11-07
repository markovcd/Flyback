using System.IO.Pipes;
using System.Text.Json;

namespace Flyback.Host.Driving;

/// <summary>
/// Where the editor listens for a program driving the open patch, and the one JSON message
/// a line both ends speak (ADR-0202).
/// </summary>
/// <remarks>
/// A named pipe on Windows and a Unix socket elsewhere, both opened
/// <see cref="PipeOptions.CurrentUserOnly"/>: only the same user can connect, and the
/// socket sits in a folder of the user's own where there is one.
/// </remarks>
internal static class EditorChannel
{
    /// <summary>The longest line either end reads, against a peer that never sends a newline.</summary>
    public const int LongestLine = 32 * 1024 * 1024;

    public const PipeOptions Options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The pipe's name, or the socket's path, for this user.</summary>
    public static string Name => Locate(
        Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.UserName);

    /// <summary>The channel, built only from what an MCP client passes to a server it starts.</summary>
    /// <remarks>
    /// Never from <c>TMPDIR</c>, where .NET would put an unrooted socket: Claude's desktop app
    /// starts its servers without it, so the editor and <c>flyback-cli</c> would look in two folders.
    /// </remarks>
    internal static string Locate(string? runtime, string home, string user)
    {
        if (OperatingSystem.IsWindows()) return $"flyback-editor-{Safe(user)}";

        if (OperatingSystem.IsMacOS()) return Path.Combine(home, "Library", "Caches", "Flyback", "flyback-editor.sock");

        if (runtime is not { Length: > 0 } || !Directory.Exists(runtime))
        {
            runtime = Uid() is { } uid && Directory.Exists($"/run/user/{uid}") ? $"/run/user/{uid}" : null;
        }

        return runtime is not null
            ? Path.Combine(runtime, "flyback-editor.sock")
            : Path.Combine("/tmp", $"flyback-editor-{Safe(user)}.sock");
    }

    /// <summary>This process's user id, read from <c>/proc</c>; null where there is none.</summary>
    private static string? Uid()
    {
        try
        {
            return File.ReadLines("/proc/self/status")
                .FirstOrDefault(line => line.StartsWith("Uid:", StringComparison.Ordinal))
                ?.Split('\t', StringSplitOptions.RemoveEmptyEntries)
                .ElementAtOrDefault(1);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Safe(string name) => new([.. name.Where(char.IsAsciiLetterOrDigit)]);

    public static string Write<T>(T message) => JsonSerializer.Serialize(message, Json) + "\n";

    /// <summary>The message on <paramref name="line"/>, or null where it is not one.</summary>
    public static T? Read<T>(string line) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(line, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
