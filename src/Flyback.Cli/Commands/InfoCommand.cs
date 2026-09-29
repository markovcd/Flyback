using System.Globalization;
using System.Text.Json;
using Flyback.Cli.Models;
using Flyback.Cli.Common;
using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Cli.Commands;

/// <summary>
/// What a patch is made of and what each half of it costs, without opening a window
/// to find out.
/// </summary>
/// <remarks>
/// Everything here is already computed by the compiler on the way to a program; none
/// of it is measured or guessed. The two costs are separate because a module only the
/// speakers reach is not in the picture's op list at all.
/// </remarks>
internal static class InfoCommand
{
    public static int Run(
        Patch patch,
        string name,
        bool json,
        TextWriter output,
        TextWriter error,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null)
    {
        var picture = Costed(patch.CompileForVideo(samples: samples, pictures: pictures).Program);
        var sound = Costed(patch.CompileForAudio(samples: samples).Program);
        var reaches = patch.Reaches();

        var requires = (patch.Requires ?? [])
            .Select(r => r.Id)
            .DefaultIfEmpty(NodeCatalog.BuiltInProvider.Id)
            .ToArray();

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new
                {
                    patch = name,
                    version = patch.Version ?? PatchIO.FirstVersion,
                    modules = patch.Nodes.Count,
                    wires = patch.Connections.Count,
                    requires,
                    length = new { seconds = patch.Lasts, set = patch.Length is not null },
                    wired = new { picture = reaches.Picture, sound = reaches.Sound },
                    picture,
                    sound,
                },
                Writing.Json));

            return Exit.Ok;
        }

        output.WriteLine(name);
        Line("modules", patch.Nodes.Count.ToString(CultureInfo.InvariantCulture));
        Line("wires", patch.Connections.Count.ToString(CultureInfo.InvariantCulture));
        Line("requires", string.Join(", ", requires));
        Line("length", Length(patch));
        Line("picture", Describe(picture, reaches.Picture));
        Line("sound", Describe(sound, reaches.Sound));

        return Exit.Ok;

        void Line(string label, string value) => output.WriteLine($"  {label,-9} {value}");
    }

    /// <summary>A length the patch sets, or the default and what the viewers make of a patch with none.</summary>
    private static string Length(Patch patch) => patch.Length is { } seconds
        ? PatchLength.Say(seconds)
        : $"not set: {PatchLength.Say(Patch.DefaultLength)} in the editor, no end in the viewers";

    private static Cost Costed(CompiledPatch program) => new(
        program.Ops.Length,
        program.RegisterCount,
        program.DelayLengths.Count,
        program.PhaseCount,
        program.UnitCount);

    /// <summary>
    /// A half of the patch in one line. The state is only mentioned when there
    /// is any, because most patches have none and a row of zeroes says nothing.
    /// </summary>
    private static string Describe(Cost cost, bool wired)
    {
        var parts = new List<string>
        {
            $"{cost.Ops} ops",
            $"{cost.Registers} registers",
        };

        if (cost.Delays > 0) parts.Add(Writing.Count(cost.Delays, "delay line"));
        if (cost.Phases > 0) parts.Add(Writing.Count(cost.Phases, "phase accumulator"));
        if (cost.Cells > 0) parts.Add(Writing.Count(cost.Cells, "feedback cell"));

        // Said plainly, because an unwired half still compiles to a program with
        // a couple of ops in it and the numbers alone would look like a patch
        // that does something.
        if (!wired) parts.Add("nothing wired in");

        return string.Join(", ", parts);
    }
}
