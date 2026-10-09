using System.CommandLine;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Hosting;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>
/// Says what modules this build has, or everything about one of them.
/// </summary>
/// <remarks>
/// The question a patch that did not load completely raises and nothing else
/// answers: <c>info</c> says what a patch requires, and this says what is here
/// to meet it. It matters most to this program, which reads the plugins beside
/// it and has none when it is run out of a build rather than a publish.
/// </remarks>
internal static class ModulesCommand
{
    /// <summary>Lists the installed catalog, which is what a plugin adds to, or describes one module in it.</summary>
    public static Command Build(PluginRegistry plugins, Option<bool> json)
    {
        var module = new Argument<string?>("module")
        {
            Description = "One module to describe, by type id or name: its sockets, defaults, ranges and what it does.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var find = new Option<string?>("--find")
        {
            Description = "List the modules a phrase describes, likeliest first, as the decision model the settings choose ranks them.",
        };

        var settings = DecisionSettingsOption.Create();

        var command = new Command("modules", "Say what modules this build has, or everything about one of them.")
        {
            module, find, settings, json,
        };

        command.SetAction(async (result, cancel) =>
        {
            plugins.Ready();

            var output = result.InvocationConfiguration.Output;

            if (result.GetValue(find) is { } phrase)
                return await FindAsync(plugins.Catalog, NodeCatalog.Current, phrase, result.GetValue(json), output, result.InvocationConfiguration.Error, cancel, result.GetValue(settings));

            return result.GetValue(module) is { } wanted
                ? Describe(
                    NodeCatalog.Current, wanted, result.GetValue(json), output, result.InvocationConfiguration.Error)
                : Run(NodeCatalog.Current, result.GetValue(json), output);
        });

        return command;
    }

    /// <summary>The modules <paramref name="phrase"/> describes, by meaning, with how likely each is.</summary>
    /// <param name="settingsPath">Somewhere other than the usual place, for the tests.</param>
    public static async Task<int> FindAsync(
        PluginCatalog plugins,
        ModuleCatalog catalog,
        string phrase,
        bool json,
        TextWriter output,
        TextWriter error,
        CancellationToken cancel,
        string? settingsPath = null,
        string? modelsRoot = null)
    {
        var decisions = new Decisions(
            plugins,
            DecisionSettings.Load(settingsPath),
            new Credentials(plugins.PreferredSecretStore),
            new ModelStore(modelsRoot ?? ModelStore.DefaultRoot));

        var candidates = catalog.All.Where(d => !NodeCatalog.IsSink(d.TypeId) && !ExpressionFusion.Retired(d)).ToList();
        var spelled = candidates.Where(d => ModuleFinder.Spelled(d, phrase)).ToList();

        // What the phrase spells is sure and needs no model, so it leads; the model ranks the rest.
        var why = decisions.Unavailable();

        if (why is not null && spelled.Count == 0)
        {
            error.WriteLine(why);
            return Exit.Failed;
        }

        var meant = why is null ? await new ModuleFinder(decisions).Find(phrase, candidates, cancel).ConfigureAwait(false) : [];
        var found = spelled.Select(d => new FoundModule(d, 1)).Concat(meant.Where(f => !spelled.Contains(f.Module))).ToList();

        if (found.Count == 0 && decisions.Problem is { } problem)
        {
            error.WriteLine(problem);
            return Exit.Failed;
        }

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                found.Select(f => new { typeId = f.Module.TypeId, name = f.Module.Name, category = f.Module.Category, probability = f.Probability }),
                Writing.Json));

            return Exit.Ok;
        }

        if (found.Count == 0) output.WriteLine($"No module was taken to mean “{phrase}”.");

        foreach (var f in found)
            output.WriteLine($"{f.Module.TypeId,-24} {f.Module.Name,-20} {f.Probability.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");

        return Exit.Ok;
    }

    public static int Run(ModuleCatalog catalog, bool json, TextWriter output)
    {
        var modules = catalog.All.Select(def => Listed(catalog, def)).ToArray();

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new
                {
                    providers = catalog.Providers.Select(provider => new { provider.Id, provider.Name }),
                    modules,
                },
                Writing.Json));

            return Exit.Ok;
        }

        Write(catalog, modules, output);

        return Exit.Ok;
    }

    /// <summary>
    /// One module, found by its type id or its name: every socket with its default,
    /// range and help, what it carries besides, and its description.
    /// </summary>
    public static int Describe(ModuleCatalog catalog, string wanted, bool json, TextWriter output, TextWriter error)
    {
        if (Find(catalog, wanted) is not { } def)
        {
            error.WriteLine($"There is no module '{wanted}'.{Nearest(catalog, wanted)}");

            return Exit.Failed;
        }

        var listed = Listed(catalog, def);
        var inputs = def.Inputs.Select(Described).ToArray();
        var piped = Piped(def);
        var carried = def.Extras
            .Select(extra => new Carried(
                extra.Key,
                extra.Announce().Trim(),
                extra.Explained().ToDictionary(said => said.Name, said => said.Help, StringComparer.Ordinal)))
            .ToArray();

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new
                {
                    typeId = def.TypeId,
                    name = def.Name,
                    category = def.Category,
                    provider = listed.Provider,
                    reaches = Reaches(def.Sinks),
                    description = def.Description,
                    words = def.Words,
                    piped = piped.Select(port => def.Inputs[port].Name),
                    pipedColor = piped.Length == 0 && Tinted(def) is { } colored ? def.Inputs[colored].Name : null,
                    inputs,
                    outputs = def.Outputs.Select((port, index) => new { index, name = port.Name, help = port.Help }),
                    carries = carried,
                },
                Writing.Json));

            return Exit.Ok;
        }

        output.WriteLine($"{def.Name}  {def.TypeId}");
        output.WriteLine($"  {def.Category}, from {listed.Provider}, {Reaches(def.Sinks)}");

        if (def.Description.Length > 0)
        {
            output.WriteLine();
            output.WriteLine($"  {def.Description}");
        }

        if (def.Words.Length > 0) output.WriteLine($"  Also: {def.Words}.");

        var name = def.Inputs.Select(port => port.Name.Length).DefaultIfEmpty(0).Max();
        var at = def.Inputs.Select(port => At(port).Length).DefaultIfEmpty(0).Max();

        output.WriteLine();
        output.WriteLine("inputs");

        if (def.Inputs.Count == 0) output.WriteLine("  none");

        for (var i = 0; i < def.Inputs.Count; i++)
        {
            output.WriteLine($"{(piped.Contains(i) ? "|>" : "  ")}{i,2} {def.Inputs[i].Name.PadRight(name)}  {At(def.Inputs[i]).PadRight(at)}  {Turns(def.Inputs[i])}".TrimEnd());
            Helped(def.Inputs[i].Help);
        }

        var tinted = piped.Length == 0 ? Tinted(def) : null;

        if (tinted is { } tint)
            output.WriteLine($"  a color piped in lands on {def.Inputs[tint].Name}");

        if (def.Inputs.Count > 0 && piped.Length == 0)
        {
            output.WriteLine(
                $"  {(tinted is null ? "a pipe" : "anything else")} says where it lands: "
                + $"{def.TypeId}({def.Inputs[0].Name.Replace(' ', '_')}: _)");
        }

        output.WriteLine();
        output.WriteLine("outputs");

        if (def.Outputs.Count == 0) output.WriteLine("  none");

        for (var i = 0; i < def.Outputs.Count; i++)
        {
            output.WriteLine($"  {i,2} {def.Outputs[i].Name}");
            Helped(def.Outputs[i].Help);
        }

        if (carried.Length > 0)
        {
            output.WriteLine();
            output.WriteLine("carries");

            foreach (var extra in carried)
            {
                output.WriteLine($"  {extra.Says}");

                foreach (var (field, help) in extra.Help)
                    if (help.Length > 0) output.WriteLine($"       {field}: {help}");
            }
        }

        return Exit.Ok;

        // Under the socket's row, indented past its number.
        void Helped(string help)
        {
            if (help.Length > 0) output.WriteLine($"       {help}");
        }
    }

    /// <summary>A type id first, then a name, neither minding case.</summary>
    private static NodeDef? Find(ModuleCatalog catalog, string wanted) =>
        catalog.Get(wanted)
        ?? catalog.All.FirstOrDefault(def => string.Equals(def.TypeId, wanted, StringComparison.OrdinalIgnoreCase))
        ?? catalog.All.FirstOrDefault(def => string.Equals(def.Name, wanted, StringComparison.OrdinalIgnoreCase));

    private static string Nearest(ModuleCatalog catalog, string wanted)
    {
        var tail = wanted[(wanted.LastIndexOf('.') + 1)..];

        var close = catalog.All
            .Where(def => def.TypeId.Contains(tail, StringComparison.OrdinalIgnoreCase)
                || def.Name.Contains(tail, StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .Select(def => def.TypeId)
            .ToArray();

        return close.Length == 0 ? string.Empty : $" Did you mean {string.Join(", ", close)}?";
    }

    /// <summary>
    /// The inputs a bare <c>|&gt;</c> lands on: <c>in</c> where there is one, else
    /// the only one, else <c>x</c> and <c>y</c> together when they lead. None means
    /// the call says where with <c>socket: _</c>.
    /// </summary>
    private static int[] Piped(NodeDef def)
    {
        var signal = def.Inputs.ToList().FindIndex(port => string.Equals(port.Name, "in", StringComparison.OrdinalIgnoreCase));

        if (signal >= 0) return [signal];
        if (def.Inputs.Count == 1) return [0];

        return def.Inputs.Count >= 2 && string.Equals(def.Inputs[0].Name, "x", StringComparison.OrdinalIgnoreCase)
            && string.Equals(def.Inputs[1].Name, "y", StringComparison.OrdinalIgnoreCase) ? [0, 1] : [];
    }

    /// <summary>The one color socket a module has, where a color piped in lands; null for none or several.</summary>
    private static int? Tinted(NodeDef def)
    {
        var colors = Enumerable.Range(0, def.Inputs.Count).Where(i => def.Inputs[i].Kind == PortKind.Color).ToList();

        return colors is [var tint] ? tint : null;
    }

    private static Module Listed(ModuleCatalog catalog, NodeDef def) => new(
        def.TypeId,
        def.Name,
        def.Category,
        (catalog.ProviderOf(def.TypeId) ?? NodeCatalog.BuiltInProvider).Id,
        [.. def.Inputs.Select(port => port.Name)],
        [.. def.Outputs.Select(port => port.Name)]);

    private static Input Described(PortSpec port, int index) => new(
        index,
        port.Name,
        port.Kind.ToString().ToLowerInvariant(),
        port.Display.ToString().ToLowerInvariant(),
        port.Default,
        port.Ranged ? port.Min : null,
        port.Ranged ? port.Max : null,
        port.NeedsAWire,
        port.Help);

    /// <summary>What an input sits at unwired, or that it has nothing to sit at.</summary>
    private static string At(PortSpec port) => port.NeedsAWire ? "wired only" : port.Format(port.Default);

    /// <summary>How far an input turns, and what it takes where that is not a number.</summary>
    private static string Turns(PortSpec port) => port.Kind switch
    {
        PortKind.Color => "color",
        PortKind.Any => "scalar or color",
        _ when port.Ranged && !port.PatchOnly => $"{port.Format(port.Min)} to {port.Format(port.Max)}",
        _ => string.Empty,
    };

    private static string Reaches(ModuleSinks sinks) => sinks switch
    {
        ModuleSinks.Audio => "sound only",
        ModuleSinks.Video => "picture only",
        _ => "picture and sound",
    };

    private static void Write(ModuleCatalog catalog, IReadOnlyList<Module> modules, TextWriter output)
    {
        output.WriteLine("providers");

        foreach (var provider in catalog.Providers)
        {
            var count = modules.Count(module => module.Provider == provider.Id);

            output.WriteLine($"  {provider.Id,-20} {provider.Name,-20} {Writing.Count(count, "module")}");
        }

        // By category and in the catalog's own order, which is the order the
        // palette shows them in.
        foreach (var category in catalog.Categories)
        {
            output.WriteLine();
            output.WriteLine(category);

            foreach (var module in modules.Where(module => module.Category == category))
                output.WriteLine($"  {module.TypeId,-24} {module.Name}");
        }
    }
}
