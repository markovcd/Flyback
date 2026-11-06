using System.Globalization;
using System.Text;
using System.Text.Json;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;

namespace Flyback.Plugins.Assist;

/// <summary>
/// The tools that change the patch, all through its text: writing it afresh, editing
/// what it says, and putting it back as it started.
/// </summary>
/// <param name="startingPoint">The patch <c>reset</c> goes back to, as patch JSON, so a reset cannot hand back something an edit reached into.</param>
/// <param name="startingText">The text that patch was written in, or null where it was not.</param>
/// <param name="delivery">How what is built reaches the person, which is what <c>reset</c> says it goes back to.</param>
internal sealed class WholePatchEdits(WorkingPatch bench, PatchReports reports, string startingPoint, string? startingText, Delivery delivery)
{
    public ToolOutcome Reset()
    {
        bench.Adopt(PatchIO.Read(startingPoint, bench.Modules).Patch);
        bench.Source = startingText;
        bench.Proposal = null;
        bench.Missed = null;
        bench.Edits++;

        var back = delivery switch
        {
            Delivery.Live => "back to the patch as you last proposed it or as the file stood before these tools touched it.",
            Delivery.Proposal => "back to the patch on the person's canvas, as you last proposed it or as they last changed it.",
            _ => "back to the patch as it was when this started.",
        };

        return ToolOutcome.Fine($"{back} {reports.Describe()}");
    }

    /// <summary>
    /// <c>describe_patch</c>, which also lets <c>propose</c> through after a failed edit, the
    /// patch having now been seen as it stands.
    /// </summary>
    public ToolOutcome Describe()
    {
        bench.Missed = null;

        return ToolOutcome.Fine(reports.Describe());
    }

    /// <summary><paramref name="outcome"/>, noting on the bench whether the edit it answers went through.</summary>
    private ToolOutcome Noted(ToolOutcome outcome)
    {
        bench.Missed = outcome.Ok ? null : outcome.Text;

        return outcome;
    }

    /// <summary>
    /// Builds a whole patch from the text language, in place of the one being worked
    /// on.
    /// </summary>
    /// <remarks>
    /// The reason this exists is arithmetic: a module or a wire at a time makes the
    /// Whole band preset 222 calls against a <see cref="WorkbenchLimits.MaxToolCalls"/>
    /// of 200. A module the text names by a handle the patch already had stays that
    /// module (<see cref="KeptModules"/>).
    /// </remarks>
    public ToolOutcome WritePatch(JsonElement arguments) => Noted(Written(arguments));

    private ToolOutcome Written(JsonElement arguments)
    {
        if (!ToolFields.Source.Text(arguments, out var source))
            return ToolOutcome.Refused($"{ToolFields.Source.Quoted} is required: the patch, written in the language.");

        var load = Adopted(source);

        if (!load.Ok) return ToolOutcome.Refused($"this patch does not read:{Environment.NewLine}{Complaints(load)}");

        var text = new StringBuilder($"written: {bench.Patch.Nodes.Count} modules and {bench.Patch.Connections.Count} wires, "
            + "each module answering to its let name.");

        return ToolOutcome.Fine(Said(text, load));
    }

    /// <summary>
    /// Replaces pieces of the patch's text and builds the result, which is how a
    /// patch that exists is changed.
    /// </summary>
    /// <remarks>
    /// The text is the person's own where the patch was written, comments and
    /// <c>def</c>s included, and otherwise the printing <c>describe_patch</c> shows.
    /// Every piece has to be found exactly once, and nothing is adopted unless all of
    /// them are and the result reads.
    /// </remarks>
    public ToolOutcome EditPatch(JsonElement arguments) => Noted(Edited(arguments));

    private ToolOutcome Edited(JsonElement arguments)
    {
        if (!ToolFields.Edits.List(arguments, out var edits) || edits.GetArrayLength() == 0)
            return ToolOutcome.Refused($"{ToolFields.Edits.Quoted} is required: a list of {{find, replace}} pieces of the patch's text.");

        // Matched with plain line feeds, and written back with the line endings the text had.
        var written = reports.Text();
        var newline = written.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var text = written.ReplaceLineEndings("\n");

        if (string.IsNullOrWhiteSpace(text))
            return ToolOutcome.Refused("the patch is empty, so there is nothing to edit; build it with write_patch.");
        var changed = new List<(int From, int To)>();
        var n = 0;

        foreach (var edit in edits.EnumerateArray())
        {
            n++;

            if (!ToolFields.Find.Text(edit, out var find))
                return ToolOutcome.Refused($"edit {n} needs {ToolFields.Find.Quoted}: the piece of the text to replace.");

            ToolFields.Replace.Find(edit, out var given);
            var replace = given.ValueKind == JsonValueKind.String ? given.GetString() ?? string.Empty : null;

            if (replace is null)
                return ToolOutcome.Refused($"edit {n} needs {ToolFields.Replace.Quoted}: what goes in its place, empty to delete it.");

            find = find.ReplaceLineEndings("\n");
            replace = replace.ReplaceLineEndings("\n");

            var at = text.IndexOf(find, StringComparison.Ordinal);

            if (at < 0)
                return ToolOutcome.Refused(
                    $"edit {n}'s {ToolFields.Find.Quoted} is not in the patch's text. Nothing was changed. "
                    + "It has to match exactly, spaces and line breaks included; describe_patch shows the text as it stands.");

            if (text.IndexOf(find, at + 1, StringComparison.Ordinal) >= 0)
                return ToolOutcome.Refused(
                    $"edit {n}'s {ToolFields.Find.Quoted} is in the patch's text more than once. Nothing was changed. "
                    + "Take in enough around it to say which.");

            text = string.Concat(text.AsSpan(0, at), replace, text.AsSpan(at + find.Length));
            changed = Moved(changed, at, find.Length, replace.Length);
        }

        var load = Adopted(text.ReplaceLineEndings(newline));

        if (!load.Ok) return ToolOutcome.Refused($"the edited patch does not read, so nothing was changed:{Environment.NewLine}{Complaints(load)}");

        var said = new StringBuilder($"edited: {bench.Patch.Nodes.Count} modules and {bench.Patch.Connections.Count} wires. It now reads:");

        said.AppendLine().Append(Lines(text, changed));

        return ToolOutcome.Fine(Said(said, load));
    }

    /// <summary>
    /// Builds <paramref name="source"/> and, where it reads, makes it the patch on the
    /// bench, keeping each module it still names.
    /// </summary>
    private LanguageLoad Adopted(string source)
    {
        var load = PatchLanguage.Build(source, bench.Modules);

        // Nothing partial is adopted. A patch with a mistake in it is a patch
        // half of which was not what anybody wrote, and the complaints carry a
        // line and a column apiece, which is enough to fix it and try again.
        if (!load.Ok) return load;

        var named = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, name) in load.Map.Named) named.TryAdd(name, id);

        var (patch, kept) = KeptModules.Keep(load.Patch, named, bench.Patch, bench.ByHandle);

        bench.Adopt(patch, kept);
        bench.Source = source;
        bench.Proposal = null;
        bench.Edits++;

        return load;
    }

    /// <summary>What a write or an edit answers after its first line: the handles nothing named, the warnings and the issues.</summary>
    private string Said(StringBuilder text, LanguageLoad load)
    {
        // On a line of its own after the numbered lines an edit echoes, so it is not read as one of them.
        if (reports.Unnamed() is { Length: > 0 } unnamed)
        {
            text.Append(text.ToString().Contains('\n') ? Environment.NewLine : " ")
                .Append("Those with no let name answer to: ").Append(unnamed).Append('.');
        }

        if (load.Issues.Count > 0)
            text.AppendLine().AppendLine("It has warnings, and is built as it stands:").AppendLine(Complaints(load));

        return text.AppendLine().Append(reports.Issues()).ToString();
    }

    /// <summary>
    /// The spans an edit has changed, after one more replaces <paramref name="removed"/>
    /// characters at <paramref name="at"/> with <paramref name="inserted"/>: those it
    /// touches merged into its own, and those after it moved along.
    /// </summary>
    private static List<(int From, int To)> Moved(List<(int From, int To)> spans, int at, int removed, int inserted)
    {
        var moved = new List<(int From, int To)>();
        var (from, to) = (at, at + inserted);

        foreach (var (f, t) in spans)
        {
            if (t < at) moved.Add((f, t));
            else if (f > at + removed) moved.Add((f + inserted - removed, t + inserted - removed));
            else (from, to) = (Math.Min(from, f), Math.Max(to, t > at + removed ? t + inserted - removed : at + inserted));
        }

        moved.Add((from, to));

        return moved;
    }

    /// <summary>The whole lines of <paramref name="text"/> the spans touch, once each, numbered and in order.</summary>
    private static string Lines(string text, List<(int From, int To)> spans)
    {
        var lines = text.Split('\n');
        var touched = new SortedSet<int>();

        foreach (var (from, to) in spans)
        {
            var first = text.AsSpan(0, from).Count('\n');
            var last = text.AsSpan(0, Math.Min(to, text.Length)).Count('\n');

            for (var line = first; line <= last; line++) touched.Add(line);
        }

        return string.Join(Environment.NewLine, touched.Select(line => string.Create(CultureInfo.InvariantCulture, $"{line + 1,4}| {lines[line]}")));
    }

    /// <summary>
    /// What is wrong with text that does not read, each complaint with its code,
    /// the line it is on and a caret under it, and the fix where it has exactly
    /// one — which a model can make as it stands.
    /// </summary>
    private static string Complaints(LanguageLoad load)
    {
        var lines = load.Source.ReplaceLineEndings("\n").Split('\n');
        var text = new StringBuilder();

        // One stray comma can cascade into dozens, and the first few are the ones to fix.
        const int Shown = 8;

        foreach (var issue in load.Issues.Take(Shown))
        {
            text.Append(CultureInfo.InvariantCulture, $"{issue.Line}:{issue.Column} [{issue.Code}] {(issue.IsError ? "" : "warning: ")}{issue.Message}").AppendLine();

            if (issue.Line >= 1 && issue.Line <= lines.Length)
            {
                var line = lines[issue.Line - 1];

                text.Append("    ").AppendLine(line);
                text.Append("    ").Append(' ', Math.Clamp(issue.Column - 1, 0, line.Length)).AppendLine("^");
            }
        }

        if (load.Issues.Count > Shown) text.Append("and ").Append(load.Issues.Count - Shown).AppendLine(" more after these.");

        return text.ToString().TrimEnd();
    }
}
