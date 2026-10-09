using System.Globalization;
using System.Text;
using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using static Flyback.Plugins.Assist.ToolArguments;

namespace Flyback.Plugins.Assist;

/// <summary>
/// The tools that act on the patch as a whole rather than on a module: writing it
/// afresh, putting it back as it started, and its keyboard and length.
/// </summary>
/// <param name="startingPoint">The patch the workbench was built over, as text, so a reset cannot hand back something an edit reached into.</param>
internal sealed class WholePatchEdits(WorkingPatch bench, PatchReports reports, string startingPoint)
{
    /// <summary>
    /// The computer keyboard's layout, which is the patch's rather than any
    /// module's (ADR-0099) — so it takes no handle.
    /// </summary>
    public ToolOutcome SetKeyboard(JsonElement arguments)
    {
        ToolFields.Layout.Text(arguments, out var layout);

        if (layout == "piano")
        {
            bench.Patch.Keyboard = null;
            bench.Edits++;

            return ToolOutcome.Fine($"the computer keyboard is a piano. {reports.Issues()}");
        }

        if (layout != "scale")
            return ToolOutcome.Refused($"{ToolFields.Layout.Quoted} is required, and is 'piano' or 'scale'.");

        if (!ToolFields.Tonic.Whole(arguments, out var tonic) || tonic is < 0 or >= Pitch.Classes)
            return ToolOutcome.Refused($"a scale needs {ToolFields.Tonic.Quoted}: a pitch class, 0 to 11, where 0 is C and 9 is A.");

        var scale = ToolFields.KeyScale.Text(arguments, out var named)
            ? Chords.Scales.FirstOrDefault(mode => mode.Id == named)
            : null;

        if (scale is null)
            return ToolOutcome.Refused(
                $"a scale needs {ToolFields.KeyScale.Quoted}, one of {string.Join(", ", Chords.Scales.Select(mode => mode.Id))}.");

        bench.Patch.Keyboard = new KeyboardScale(tonic, scale.Id);
        bench.Edits++;

        return ToolOutcome.Fine($"laid the computer keyboard out as {PatchPrinter.Keyboard(bench.Patch.Keyboard)}. {reports.Issues()}");
    }

    /// <summary>How long the patch plays for, which is the patch's rather than any module's, so it takes no handle.</summary>
    public ToolOutcome SetLength(JsonElement arguments)
    {
        if (!ToolFields.Length.Number(arguments, out var seconds) || !double.IsFinite(seconds)
            || seconds is < PatchLength.Shortest or > PatchLength.Longest)
            return ToolOutcome.Refused(
                $"{ToolFields.Length.Quoted} is required: a number from {PatchLength.Shortest} to {PatchLength.Longest:0} (a day).");

        bench.Patch.Length = seconds;
        bench.Edits++;

        return ToolOutcome.Fine($"the patch plays for {PatchLength.Say(bench.Patch.Lasts)}. {reports.Issues()}");
    }

    public ToolOutcome Reset()
    {
        bench.Adopt(PatchIO.Read(startingPoint, bench.Modules).Patch);
        bench.Proposal = null;
        bench.Edits++;

        return ToolOutcome.Fine($"back to the patch as it was when this started. {reports.Describe()}");
    }

    /// <summary>
    /// Builds a whole patch from the text language, in place of the one being worked
    /// on.
    /// </summary>
    /// <remarks>
    /// The reason this exists is arithmetic: placing a module is one call and so is
    /// every wire, which makes the Whole band preset 222 of them against a
    /// <see cref="WorkbenchLimits.MaxToolCalls"/> of 200.
    /// <para>
    /// It replaces rather than edits, which is why the wiring tools stay. A module
    /// written in the language is named after the piece of source that made it
    /// (ADR-0067), so rewriting a patch with one line changed keeps the identity of
    /// everything else — and an id is what joins a Meter's reading and a Scope's
    /// buffer to the program that reads them. What it cannot keep is a patch that
    /// was not written in the language, whose ids and positions this has never seen.
    /// </para>
    /// </remarks>
    public ToolOutcome WritePatch(JsonElement arguments)
    {
        if (!ToolFields.Source.Text(arguments, out var source))
            return ToolOutcome.Refused($"{ToolFields.Source.Quoted} is required: the patch, written in the language.");

        var load = PatchLanguage.Build(source, bench.Modules);

        // Nothing partial is adopted. A patch with a mistake in it is a patch
        // half of which was not what anybody wrote, and the complaints carry a
        // line and a column apiece, which is enough to fix it and try again.
        if (!load.Ok)
            return ToolOutcome.Refused($"this patch does not read:{Environment.NewLine}{Complaints(load)}");

        // Each module answers to the name the model bound it to, so the source it
        // just wrote is already the description and nothing is echoed back.
        var named = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, name) in load.Map.Named) named.TryAdd(name, id);

        bench.Adopt(load.Patch, named);
        bench.Proposal = null;
        bench.Edits++;

        var text = new StringBuilder($"written: {bench.Patch.Nodes.Count} modules and {bench.Patch.Connections.Count} wires, "
            + "each module answering to its let name.");

        var unnamed = bench.Patch.Nodes.Select(bench.Handle).Where(handle => !named.ContainsKey(handle)).ToArray();

        if (unnamed.Length > 0) text.Append(" Those with none answer to: ").Append(string.Join(", ", unnamed)).Append('.');

        if (load.Issues.Count > 0)
            text.AppendLine().AppendLine("It has warnings, and is built as it stands:").AppendLine(Complaints(load));

        return ToolOutcome.Fine(text.Append(' ').Append(reports.Issues()).ToString());
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
