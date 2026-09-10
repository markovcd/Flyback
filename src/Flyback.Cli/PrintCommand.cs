using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Language;

namespace Flyback.Cli;

/// <summary>
/// Writes a patch out as text in the language, and can say whether the text it
/// would write is still the same instrument.
/// </summary>
/// <remarks>
/// The door onto the language that ADR-0065 left open: every other way in reads
/// text, so until there is a verb for it nothing outside the window can hand one
/// back. That matters most for the format the language is a second reading of — a
/// <c>.fbk</c> is JSON keyed by Guids, and a diff of one says which ids moved.
/// <para>
/// A printing is lossy in the two directions the window knows about and in one
/// more: comments live in the lexer and never reach a patch, so printing a
/// <c>.fbks</c> writes back everything it said and nothing it explained. Which is
/// why the file it was read from is the one path this refuses to write to.
/// </para>
/// </remarks>
internal static class PrintCommand
{
    public static int Run(
        Patch patch,
        FileInfo file,
        FileInfo? output,
        bool check,
        TextWriter writer,
        TextWriter error,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null)
    {
        var source = PatchPrinter.Print(patch);
        var absent = Absent(patch, file.Name, error);

        var code = check
            ? Checked(patch, file.Name, source, writer, error, samples, pictures)
            : Written(source, file, output, writer, error);

        // A printing short of a module is still a printing and is still written,
        // the way pack writes a bundle short of a file. What the exit code must
        // not say is that a patch missing part of itself came out whole.
        return code == Exit.Ok && absent ? Exit.Problems : code;
    }

    /// <summary>
    /// Says which of a patch's modules the catalogue cannot define, and whether there
    /// were any.
    /// </summary>
    /// <remarks>
    /// A module with no definition has no socket names to write a call from, so the
    /// printer leaves it out silently — and this program is the one most likely to
    /// meet it, since a build rather than a publish has no plugins beside it.
    /// </remarks>
    private static bool Absent(Patch patch, string name, TextWriter error)
    {
        var missing = patch.Nodes
            .Select(node => node.TypeId)
            .Where(type => NodeCatalog.Current.Get(type) is null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (missing.Length == 0) return false;

        error.WriteLine(
            $"{GlobalConstants.ApplicationName}: {name}: "
            + $"{Writing.Count(missing.Length, "module")} nothing installed defines, "
            + "which the language has no way to write — the printing is without them.");

        foreach (var type in missing) error.WriteLine($"    {type}");

        return true;
    }

    /// <summary>Puts the printing where it was asked for.</summary>
    private static int Written(
        string source,
        FileInfo file,
        FileInfo? output,
        TextWriter writer,
        TextWriter error)
    {
        // Standard output where no file was named, so that a printing can be
        // read, piped and diffed without leaving one behind — and nothing else
        // goes there, or what is piped is a patch with a sentence on top of it.
        if (output is null)
        {
            writer.Write(source);
            return Exit.Ok;
        }

        if (string.Equals(file.FullName, output.FullName, StringComparison.OrdinalIgnoreCase))
        {
            error.WriteLine(
                $"{GlobalConstants.ApplicationName}: {output.Name}: this is the file being read — "
                + "a printing is a copy of a patch and not a tidying of one, so writing it here would "
                + "take away any comments and groups the original has.");

            return Exit.Failed;
        }

        try
        {
            File.WriteAllText(output.FullName, source);
        }
        catch (Exception ex)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {output.Name}: {ex.Message}");
            return Exit.Failed;
        }

        // Nothing said on success, the way render says nothing: the file is the
        // whole of the answer.
        return Exit.Ok;
    }

    /// <summary>
    /// Prints, reads the printing back, and compiles both — the claim the language
    /// rests on, made about one patch rather than about the presets. Both sinks,
    /// because each walks back from its own socket: a printing that drops something
    /// only the speakers hear has an identical picture.
    /// </summary>
    private static int Checked(
        Patch patch,
        string name,
        string source,
        TextWriter writer,
        TextWriter error,
        ISampleLibrary? samples,
        IImageLibrary? pictures)
    {
        var again = PatchLanguage.Build(source);

        // A printing that does not read back is a fault in the printer rather
        // than in anybody's file, and the complaints are about lines nobody
        // wrote — so the text goes out beside them, or there is nothing to read
        // them against.
        if (!again.Ok)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {name}: what was printed does not read back.");
            error.WriteLine(again.Report);
            error.WriteLine(source);

            return Exit.Problems;
        }

        var differences = new[]
        {
            Difference(
                "picture",
                patch.CompileForVideo(samples: samples, pictures: pictures).Program,
                again.Patch.CompileForVideo(samples: samples, pictures: pictures).Program),
            Difference(
                "sound",
                patch.CompileForAudio(samples: samples).Program,
                again.Patch.CompileForAudio(samples: samples).Program),
        };

        if (differences.All(difference => difference is null))
        {
            writer.WriteLine($"{name}: the printing is the same instrument.");
            return Exit.Ok;
        }

        error.WriteLine($"{GlobalConstants.ApplicationName}: {name}: the printing is not the same instrument.");

        foreach (var difference in differences.OfType<string>())
            error.WriteLine($"    {difference}");

        return Exit.Problems;
    }

    /// <summary>
    /// Where one sink's two programs first stop agreeing, or null where they never
    /// do. The first one and not all of them: one op that moved shifts every
    /// register after it, so the last thousand differences say nothing the first did
    /// not.
    /// </summary>
    private static string? Difference(string sink, CompiledPatch was, CompiledPatch now)
    {
        var shared = Math.Min(was.Ops.Length, now.Ops.Length);

        for (var i = 0; i < shared; i++)
        {
            if (Same(was.Ops[i], now.Ops[i])) continue;

            return $"{sink}: op {i} reads {now.Ops[i]} where it read {was.Ops[i]}.";
        }

        return was.Ops.Length == now.Ops.Length
            ? null
            : $"{sink}: {Writing.Count(now.Ops.Length, "op")} where there were {was.Ops.Length}.";
    }

    /// <summary>Whether two instructions are the same one, field for field.</summary>
    /// <remarks>
    /// <see cref="float.Equals(float)"/> rather than <c>==</c> for the constant,
    /// which makes two NaNs agree and a negative zero differ from a positive one —
    /// the comparison <c>PrinterTests</c> already holds the printer to.
    /// </remarks>
    private static bool Same(Op a, Op b) =>
        a.Code == b.Code
        && a.Out == b.Out
        && a.A == b.A
        && a.B == b.B
        && a.C == b.C
        && a.K.Equals(b.K);
}
