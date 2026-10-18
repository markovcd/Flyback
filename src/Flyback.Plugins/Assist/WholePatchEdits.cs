using System.Globalization;
using System.Text;
using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using static Flyback.Plugins.Assist.ToolArguments;

namespace Flyback.Plugins.Assist;

/// <summary>
/// The tools that change the patch, all through its text: writing it afresh, editing
/// what it says, and putting it back as it started.
/// </summary>
/// <param name="startingPoint">The patch the workbench was built over, as patch JSON, so a reset cannot hand back something an edit reached into.</param>
/// <param name="startingText">The text that patch was written in, or null where it was not.</param>
internal sealed class WholePatchEdits(WorkingPatch bench, PatchReports reports, string startingPoint, string? startingText)
{
    public ToolOutcome Reset()
    {
        bench.Adopt(PatchIO.Read(startingPoint, bench.Modules).Patch);
        bench.Source = startingText;
        bench.Proposal = null;
        bench.Edits++;

        return ToolOutcome.Fine($"back to the patch as it was when this started. {reports.Describe()}");
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
    public ToolOutcome WritePatch(JsonElement arguments)
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
    public ToolOutcome EditPatch(JsonElement arguments)
    {
        if (!ToolFields.Edits.List(arguments, out var edits) || edits.GetArrayLength() == 0)
            return ToolOutcome.Refused($"{ToolFields.Edits.Quoted} is required: a list of {{find, replace}} pieces of the patch's text.");

        var text = reports.Text().ReplaceLineEndings("\n");

        if (string.IsNullOrWhiteSpace(text))
            return ToolOutcome.Refused("the patch is empty, so there is nothing to edit; build it with write_patch.");
        var changed = new List<string>();
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
            changed.Add(Lines(text, at, replace.Length));
        }

        var load = Adopted(text);

        if (!load.Ok) return ToolOutcome.Refused($"the edited patch does not read, so nothing was changed:{Environment.NewLine}{Complaints(load)}");

        var said = new StringBuilder($"edited: {bench.Patch.Nodes.Count} modules and {bench.Patch.Connections.Count} wires. It now reads:");

        foreach (var lines in changed) said.AppendLine().Append(lines);

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
        if (reports.Unnamed() is { Length: > 0 } unnamed) text.Append(" Those with no let name answer to: ").Append(unnamed).Append('.');

        if (load.Issues.Count > 0)
            text.AppendLine().AppendLine("It has warnings, and is built as it stands:").AppendLine(Complaints(load));

        return text.AppendLine().Append(reports.Issues()).ToString();
    }

    /// <summary>The whole lines of <paramref name="text"/> that the span from <paramref name="at"/> touches, numbered.</summary>
    private static string Lines(string text, int at, int length)
    {
        var first = at == 0 ? 0 : text.LastIndexOf('\n', at - 1) + 1;

        var last = text.IndexOf('\n', at + length);
        var number = text.AsSpan(0, first).Count('\n') + 1;
        var lines = text[first..(last < 0 ? text.Length : last)].Split('\n');
        var shown = new StringBuilder();

        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) shown.AppendLine();
            shown.Append(CultureInfo.InvariantCulture, $"{number + i,4}| {lines[i]}");
        }

        return shown.ToString();
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
