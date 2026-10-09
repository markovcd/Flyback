using System.Globalization;
using System.Text.Json;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using static Flyback.Plugins.Assist.ToolArguments;

namespace Flyback.Plugins.Assist;

/// <summary>
/// The workbench's own copy of the patch, the handles its modules answer to, and
/// what the turn has done to it.
/// </summary>
/// <remarks>
/// Nodes are named by short handles rather than by <see cref="Guid"/>: a model
/// asked to invent twenty consistent guids will get it wrong.
/// </remarks>
internal sealed class WorkingPatch(ModuleCatalog modules, ISampleLibrary? samples, IImageLibrary? pictures)
{
    private readonly Dictionary<string, NodeInstance> byHandle = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, string> handleOf = [];

    public ModuleCatalog Modules => modules;

    /// <summary>Where Sample files are looked up, or null for silence.</summary>
    public ISampleLibrary? Samples => samples;

    /// <summary>Where an Image module's file is looked up, and null where nothing can.</summary>
    public IImageLibrary? Pictures => pictures;

    public Patch Patch { get; private set; } = new();

    /// <summary>What the model said of the patch it put forward, or null where it has not.</summary>
    public string? Proposal { get; set; }

    /// <summary>How many edits this turn has made.</summary>
    public int Edits { get; set; }

    /// <summary>The module each handle names.</summary>
    public IReadOnlyDictionary<string, NodeInstance> ByHandle => byHandle;

    /// <summary>The handle each module answers to.</summary>
    public IReadOnlyDictionary<Guid, string> Handles => handleOf;

    public CompileResult CompileForVideo() => Patch.CompileForVideo(modules, samples, pictures);

    public CompileResult CompileForAudio() => Patch.CompileForAudio(modules, samples);

    /// <param name="patch"></param>
    /// <param name="named">
    /// Handles to keep, by the module each names. Every module left without one is
    /// named from its type id, as it always is.
    /// </param>
    public void Adopt(Patch patch, IReadOnlyDictionary<string, Guid>? named = null)
    {
        Patch = patch;

        // Before the handles are made, so the Output gets one like everything
        // else and the assistant has something to wire into. It cannot add one
        // and cannot remove one, so this is where it has to arrive.
        Patch.EnsureOutput(modules);

        byHandle.Clear();
        handleOf.Clear();

        foreach (var (handle, id) in named ?? new Dictionary<string, Guid>())
        {
            if (patch.Find(id) is not { } node || handleOf.ContainsKey(id) || byHandle.ContainsKey(handle)) continue;

            byHandle[handle] = node;
            handleOf[id] = handle;
        }

        foreach (var node in patch.Nodes)
        {
            if (handleOf.ContainsKey(node.Id)) continue;

            var handle = Available(node.TypeId);
            byHandle[handle] = node;
            handleOf[node.Id] = handle;
        }
    }

    /// <summary>Adds <paramref name="node"/> to the patch, answering to <paramref name="handle"/>.</summary>
    public void Place(NodeInstance node, string handle)
    {
        Patch.Nodes.Add(node);
        byHandle[handle] = node;
        handleOf[node.Id] = handle;
    }

    /// <summary>Takes a module and its wires out of the patch, or says false for the Output, which stays.</summary>
    public bool Remove(NodeInstance node)
    {
        var handle = Handle(node);

        if (!Patch.Remove(node.Id)) return false;

        byHandle.Remove(handle);
        handleOf.Remove(node.Id);
        return true;
    }

    /// <summary>A handle nothing is using yet, made from the tail of the type id.</summary>
    public string Available(string typeId)
    {
        var tail = typeId[(typeId.LastIndexOf('.') + 1)..];
        var stem = new string(tail.Where(char.IsAsciiLetterOrDigit).ToArray()).ToLowerInvariant();

        if (stem.Length == 0 || char.IsAsciiDigit(stem[0])) stem = "node" + stem;

        for (var n = 1; ; n++)
        {
            var candidate = stem + n.ToString(CultureInfo.InvariantCulture);
            if (!byHandle.ContainsKey(candidate)) return candidate;
        }
    }

    public string Handle(NodeInstance? node) =>
        node is not null && handleOf.TryGetValue(node.Id, out var handle) ? handle : "?";

    public bool Node(
        JsonElement arguments,
        ToolField field,
        out NodeInstance node,
        out NodeDef def,
        out string refusal)
    {
        node = null!;
        def = null!;

        if (!field.Text(arguments, out var handle))
        {
            refusal = $"{field.Quoted} is required and must be a module's handle.";
            return false;
        }

        return Node(handle, out node, out def, out refusal);
    }

    /// <summary>The module a handle names, or why there is none.</summary>
    public bool Node(string handle, out NodeInstance node, out NodeDef def, out string refusal)
    {
        node = null!;
        def = null!;

        // 'out' is what the Output is called in the language, and the language is
        // what describe_patch answers in — so it is what comes back to these
        // tools, whatever handle the Output happens to carry. Accepting it here
        // is the difference between one call and three spent discovering that
        // the block just read as 'out.left' answers to 'output1'.
        if (!byHandle.TryGetValue(handle, out var found)
            && string.Equals(handle, "out", StringComparison.Ordinal))
        {
            found = Patch.Output;
        }

        if (found is null)
        {
            refusal = byHandle.Count == 0
                ? $"there is no module called '{handle}'; the patch is empty."
                : $"there is no module called '{handle}'. {Likely(handle)}";
            return false;
        }

        if (modules.Get(found.TypeId) is not { } definition)
        {
            refusal = $"'{handle}' is a {found.TypeId}, which this build does not have.";
            return false;
        }

        node = found;
        def = definition;
        refusal = string.Empty;
        return true;
    }

    /// <summary>
    /// Places the nodes so the patch reads left to right, sinks on the right. The
    /// assistant never thinks about coordinates; without this every node would
    /// arrive stacked at the origin.
    /// </summary>
    /// <remarks>
    /// The same routine the editor's tidy button runs, so a patch that arrives from
    /// here is laid out exactly as one the user has just tidied (ADR-0044).
    /// </remarks>
    public void Arrange() => PatchLayout.Arrange(Patch, modules);

    /// <summary>
    /// The handles a mistyped one most likely meant: every handle in a small patch,
    /// and in a large one the few that look most like it.
    /// </summary>
    private string Likely(string handle)
    {
        const int Listed = 8;

        if (byHandle.Count <= Listed) return $"The patch has: {string.Join(", ", byHandle.Keys)}.";

        var close = byHandle.Keys
            .OrderBy(key => key.Contains(handle, StringComparison.OrdinalIgnoreCase)
                || handle.Contains(key, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(key => Distance(key.ToLowerInvariant(), handle.ToLowerInvariant()))
            .ThenBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Take(Listed);

        return $"Nearest of its {byHandle.Count}: {string.Join(", ", close)}. describe_patch lists them all.";

        static int Distance(string a, string b)
        {
            var row = Enumerable.Range(0, b.Length + 1).ToArray();

            for (var i = 1; i <= a.Length; i++)
            {
                var diagonal = row[0];
                row[0] = i;

                for (var j = 1; j <= b.Length; j++)
                {
                    var above = row[j];
                    row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                    diagonal = above;
                }
            }

            return row[b.Length];
        }
    }
}
