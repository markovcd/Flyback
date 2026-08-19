using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flyback.Cli;

/// <summary>The few things every command writes the same way.</summary>
internal static class Writing
{
    /// <summary>How every <c>--json</c> in this program writes itself out.</summary>
    /// <remarks>
    /// Named the same way throughout, whether a field came from an anonymous
    /// object here or from a record: half a document in camelCase and half in
    /// Pascal is a document nobody can write a selector against.
    /// <para>
    /// And escaped for a terminal rather than for a web page. The default
    /// encoder turns every apostrophe into <c>'</c>, which is safe in an
    /// HTML attribute and unreadable in a compiler message — and these messages
    /// are full of them, because they quote socket names.
    /// </para>
    /// </remarks>
    public static JsonSerializerOptions Json { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// A count and the thing it counts, in the number the count calls for — "1
    /// delay line", "2 delay lines".
    /// </summary>
    /// <remarks>
    /// Here rather than in each command because both of them count things, and
    /// a program that says "1 warnings" reads as one nobody finished.
    /// </remarks>
    public static string Count(int many, string thing) =>
        $"{many} {thing}{(many == 1 ? string.Empty : "s")}";
}
