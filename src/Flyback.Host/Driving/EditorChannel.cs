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
/// socket sits in the user's own runtime folder where there is one.
/// </remarks>
internal static class EditorChannel
{
    /// <summary>The longest line either end reads, against a peer that never sends a newline.</summary>
    public const int LongestLine = 32 * 1024 * 1024;

    public const PipeOptions Options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The pipe's name, or the socket's path, for this user.</summary>
    public static string Name =>
        !OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime && Directory.Exists(runtime)
            ? Path.Combine(runtime, "flyback-editor.sock")
            : $"flyback-editor-{Safe(Environment.UserName)}";

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
