using System.Text.Json;
using Flyback.Core.Graph;

namespace Flyback.Cli;

/// <summary>One module of the installed catalog, as this command writes it out.</summary>
internal sealed record Module(
    string TypeId,
    string Name,
    string Category,
    string Provider,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs);

/// <summary>One input of a described module: what it is set to unwired and what it takes.</summary>
/// <param name="Min">Null where the socket declares no range.</param>
/// <param name="Display">How the text writes a value for it: a number, a note, a duration or a whole number.</param>
/// <param name="WiredOnly">True where the socket has no knob and does nothing until wired.</param>
/// <param name="Help">What the socket is for, and empty where its name says it all.</param>
internal sealed record Input(
    int Index,
    string Name,
    string Kind,
    string Display,
    float Default,
    float? Min,
    float? Max,
    bool WiredOnly,
    string Help);

/// <summary>What a module carries that is neither a socket nor a knob.</summary>
internal sealed record Carried(string Key, string Says);

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
        var carried = def.Extras.Select(extra => new Carried(extra.Key, extra.Announce().Trim())).ToArray();

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
                    piped = piped.Select(port => def.Inputs[port].Name),
                    pipedColor = piped.Length == 0 && Tinted(def) is { } colored ? def.Inputs[colored].Name : null,
                    inputs,
                    outputs = def.Outputs.Select((port, index) => new { index, name = port.Name, help = SocketHelp.For(port, input: false) }),
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

        var name = def.Inputs.Select(port => port.Name.Length).DefaultIfEmpty(0).Max();
        var at = def.Inputs.Select(port => At(port).Length).DefaultIfEmpty(0).Max();

        output.WriteLine();
        output.WriteLine("inputs");

        if (def.Inputs.Count == 0) output.WriteLine("  none");

        for (var i = 0; i < def.Inputs.Count; i++)
        {
            output.WriteLine($"{(piped.Contains(i) ? "|>" : "  ")}{i,2} {def.Inputs[i].Name.PadRight(name)}  {At(def.Inputs[i]).PadRight(at)}  {Turns(def.Inputs[i])}".TrimEnd());
            Helped(SocketHelp.For(def.Inputs[i], input: true));
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
            Helped(SocketHelp.For(def.Outputs[i], input: false));
        }

        if (carried.Length > 0)
        {
            output.WriteLine();
            output.WriteLine("carries");

            foreach (var extra in carried) output.WriteLine($"  {extra.Says}");
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
        SocketHelp.For(port, input: true));

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
