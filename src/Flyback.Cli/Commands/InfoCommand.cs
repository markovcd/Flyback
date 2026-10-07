using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using PluginRegistry = Flyback.Cli.Plugins;

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
    public static Command Build(PluginRegistry plugins, Option<bool> json)
    {
        var patch = new Argument<FileInfo?>("patch")
        {
            Description = "The patch to read: a document, a bundle, or one written as text. "
                + $"The extension decides which — .{PatchIO.FileExtension}, "
                + $"{PatchBundle.Extension} or .{PatchLanguage.FileExtension}. Left out, give --preset instead.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var preset = new Option<string>("--preset")
        {
            Description = "A shipped preset, by name, in place of a file.",
        };

        var presets = new Option<bool>("--presets")
        {
            Description = "List what --preset would accept, and stop.",
        };

        var byGroup = new Option<bool>("--by-group")
        {
            Description = "Also list what each group adds to the picture's and the sound's ops, so what makes a patch heavy is one command.",
        };

        var command = new Command("info", "Say what a patch is made of and what each half of it costs.")
        {
            patch, preset, presets, byGroup, json,
        };

        command.SetAction(result =>
        {
            var output = result.InvocationConfiguration.Output;
            var error = result.InvocationConfiguration.Error;

            plugins.Ready();

            if (result.GetValue(presets))
            {
                ShippedPresets.List(plugins.Catalog, output, result.GetValue(json));

                return Exit.Ok;
            }

            var file = result.GetValue(patch);
            var named = result.GetValue(preset);

            if ((file is null) == (named is null))
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: say what to describe: a patch, or --preset and its name.");

                return Exit.Failed;
            }

            Opened? opened;
            string name;

            if (file is not null)
            {
                opened = Patches.Open(file, error);
                name = file.Name;
            }
            else
            {
                var shipped = ShippedPresets.Open(plugins.Catalog, named!, error);

                opened = shipped?.Opened;
                name = shipped?.Name ?? named!;
            }

            return opened is not { } found
                ? Exit.Failed
                : InfoCommand.Run(found.Patch, name, result.GetValue(json), output, error, found.Samples, found.Pictures, result.GetValue(byGroup));
        });

        return command;
    }

    public static int Run(
        Patch patch,
        string name,
        bool json,
        TextWriter output,
        TextWriter error,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        bool byGroup = false)
    {
        var picture = Costed(patch.CompileForVideo(samples: samples, pictures: pictures).Program);
        var sound = Costed(patch.CompileForAudio(samples: samples).Program);
        var reaches = patch.Reaches();

        // The modules lowered once per voice, and the most voices any of them runs.
        var voices = VoiceCounts.Of(Buses.Joined(patch), NodeCatalog.Current);
        var most = voices.Values.DefaultIfEmpty(1).Max();

        var groups = byGroup ? Groups(patch, picture, sound, samples, pictures) : null;

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
                    voices = new { most, modules = voices.Count },
                    picture,
                    sound,
                    groups = groups?.Select(g => new { g.Id, g.Name, g.Modules, picture = g.Picture, sound = g.Sound }),
                },
                Writing.Json));

            return Exit.Ok;
        }

        output.WriteLine(name);
        Line("modules", patch.Nodes.Count.ToString(CultureInfo.InvariantCulture));
        Line("wires", patch.Connections.Count.ToString(CultureInfo.InvariantCulture));
        Line("requires", string.Join(", ", requires));
        Line("length", Length(patch));

        if (voices.Count > 0)
            Line("voices", $"up to {most}, on {Writing.Count(voices.Count, "module")}");
        Line("picture", Describe(picture, reaches.Picture));
        Line("sound", Describe(sound, reaches.Sound));

        if (groups is not null)
        {
            output.WriteLine(groups.Count == 0 ? "  groups    none" : "  groups    ops each adds (picture, sound)");

            foreach (var group in groups)
                output.WriteLine($"    {group.Name}  {Writing.Count(group.Modules, "module")}  {group.Picture} picture, {group.Sound} sound");
        }

        return Exit.Ok;

        void Line(string label, string value) => output.WriteLine($"  {label,-9} {value}");
    }

    /// <summary>
    /// Each group's own cost: the ops the program loses with the group's modules switched
    /// off, so a desk is counted by what it adds and not by everything upstream of it.
    /// The Output cannot be switched off and is not counted.
    /// </summary>
    private static List<GroupCost> Groups(
        Patch patch,
        Cost picture,
        Cost sound,
        ISampleLibrary? samples,
        IImageLibrary? pictures)
    {
        var costs = new List<GroupCost>();

        foreach (var group in patch.Groups ?? [])
        {
            var members = patch.Nodes
                .Where(n => group.Members.Contains(n.Id) && !NodeCatalog.IsSink(n.TypeId))
                .ToArray();

            var was = members.Select(n => n.Off).ToArray();

            foreach (var node in members) node.Off = true;

            try
            {
                var without = Costed(patch.CompileForVideo(samples: samples, pictures: pictures).Program);
                var silent = Costed(patch.CompileForAudio(samples: samples).Program);

                costs.Add(new GroupCost(
                    group.Id,
                    group.Name ?? $"group of {group.Members.Count}",
                    group.Members.Count,
                    picture.Ops - without.Ops,
                    sound.Ops - silent.Ops));
            }
            finally
            {
                for (var i = 0; i < members.Length; i++) members[i].Off = was[i];
            }
        }

        return costs;
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
