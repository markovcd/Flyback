using System.Text.Json;
using Flyback.Assist;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Plugins.Assist;

namespace Flyback.Cli.Commands;

/// <summary>
/// A conversation's transcript as <c>ask</c> writes it: prose for a person, or one
/// JSON object a line for a script, with what it looked at and heard kept on disk.
/// </summary>
/// <param name="turn">The turn the conversation is on, which names what is kept.</param>
internal sealed class ConsoleTranscript(AskOptions options, TextWriter output, TextWriter error, Func<int> turn) : ITranscript
{
    private static readonly JsonSerializerOptions Line = new(Writing.Json) { WriteIndented = false };

    /// <summary>How a step of the turn is marked, apart from what the assistant said.</summary>
    internal const string Aside = "  · ";

    private readonly List<TranscriptLine> lines = [];

    /// <summary>Pictures and sounds written to <see cref="AskOptions.Seen"/> so far.</summary>
    private int kept;

    public IReadOnlyList<TranscriptLine> Lines => lines;

    public bool IsEmpty => lines.Count == 0;

    public void Clear() => lines.Clear();

    /// <summary>Takes in what was said before, as the editor shows a saved conversation, writing none of it again.</summary>
    public void Seed(IEnumerable<TranscriptLine> said) => lines.AddRange(said);

    public void Put(Spoken spoken)
    {
        if (spoken.Keep) lines.Add(spoken.Line);

        // The briefing runs to the whole budget, so it is shown only when asked for.
        if (spoken.Kind == "briefing" && !options.Briefing) return;

        var text = spoken.Text;

        switch (spoken.Event)
        {
            case PatchEvent.Saw saw:
                Seen(spoken.Kind, text, saw.Png, "png");
                return;

            case PatchEvent.Heard heard:
                Seen(spoken.Kind, text, heard.Wav, "wav");
                return;

            case PatchEvent.Cost cost:
                Emit(spoken.Kind, new { text, input = cost.Input, cacheRead = cost.CacheRead, output = cost.Output }, Hung(Aside, text));
                return;
        }

        Emit(spoken.Kind, new { text }, spoken.Kind switch
        {
            "you" => null,
            "briefing" => spoken.Line.Text,
            "said" => Hung(string.Empty, text),
            "note" => Hung(string.Empty, text),
            "read" => Hung(Aside, $"looked up {First(text)}"),
            "told" => Hung(Aside, $"told it what changed: {text}"),
            "proposed" => Hung("Proposed: ", text),
            "failed" => Hung("! ", text),
            _ => Hung(Aside, text),
        });
    }

    /// <summary>One thing that happened: a JSON line under <c>--json</c>, else <paramref name="prose"/> where there is any.</summary>
    public void Emit(string kind, object fields, string? prose)
    {
        if (options.Json)
        {
            var body = JsonSerializer.SerializeToNode(fields, Line)!.AsObject();

            body.Insert(0, "kind", kind);
            output.WriteLine(body.ToJsonString(Line));
        }
        else if (prose is not null)
        {
            output.WriteLine(prose);
        }

        output.Flush();
    }

    /// <summary>A tool call as it arrives, which the transcript leaves to its result.</summary>
    public void Called(string tool, JsonElement arguments)
    {
        var given = arguments.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : arguments.Clone();
        var shown = given is { } json ? Flattened(JsonSerializer.Serialize(json, Line)) : "{}";

        Emit("call", new { tool, arguments = given }, Hung("  → ", $"{tool} {Shortened(shown, 160)}"));
    }

    /// <summary>A picture or a sound it took in, written out where <see cref="AskOptions.Seen"/> says.</summary>
    private void Seen(string kind, string caption, byte[] bytes, string extension)
    {
        string? file = null;

        if (options.Seen is { } folder)
        {
            try
            {
                folder.Create();
                file = Path.Combine(folder.FullName, $"{turn():00}-{++kept:00}-{kind}.{extension}");
                File.WriteAllBytes(file, bytes);
            }
            catch (Exception ex)
            {
                error.WriteLine(AskedPatch.Complaint($"{folder.Name}: {ex.Message}"));
                file = null;
            }
        }

        Emit(kind, new { text = caption, file }, Hung(Aside, file is null ? caption : $"{caption}{Environment.NewLine}→ {file}"));
    }

    /// <summary>
    /// <paramref name="text"/> after <paramref name="marker"/>, its later lines hung
    /// under its first and blank lines at either end dropped.
    /// </summary>
    private static string Hung(string marker, string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n')
            .SkipWhile(string.IsNullOrWhiteSpace)
            .Reverse()
            .SkipWhile(string.IsNullOrWhiteSpace)
            .Reverse()
            .ToList();

        return lines.Count == 0
            ? marker.TrimEnd()
            : marker + string.Join(Environment.NewLine + new string(' ', marker.Length), lines);
    }

    /// <summary>The first line of <paramref name="text"/>, cut to a hundred characters.</summary>
    private static string First(string text) =>
        Shortened(text.AsSpan().Trim().ToString().ReplaceLineEndings("\n").Split('\n')[0], 100);

    /// <summary>Every line of <paramref name="text"/>, trimmed and joined onto one.</summary>
    private static string Flattened(string text) => string.Join(
        ' ',
        text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()));

    private static string Shortened(string text, int most) => text.Length > most ? $"{text[..most]}…" : text;
}
