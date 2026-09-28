using System.Globalization;
using System.Text.Json.Nodes;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Assist;

/// <summary>
/// Carries settings changed on the canvas onto another copy of the same patch: knobs,
/// module names, what a module holds besides its knobs, the panel's knobs and the
/// patch's own words.
/// </summary>
/// <remarks>
/// A three-way merge by module id. A setting the other copy changed itself keeps
/// its own value, so an assistant asked to change something is not overruled by a
/// knob nudged while it worked. Modules and wires are not settings and are never
/// carried; a patch whose shape moved is a different patch (<see cref="PatchShape"/>).
/// </remarks>
internal static class Retuning
{
    /// <summary>The longest value written out in full.</summary>
    private const int Longest = 60;

    /// <summary>
    /// Carries onto <paramref name="onto"/> each setting that went from
    /// <paramref name="was"/> to <paramref name="now"/>, and says which.
    /// </summary>
    public static List<Retuned> Carry(Patch was, Patch now, Patch onto)
    {
        var carried = new List<Retuned>();

        foreach (var node in now.Nodes)
        {
            if (was.Find(node.Id) is not { } old || onto.Find(node.Id) is not { } target) continue;
            if (old.TypeId != node.TypeId || target.TypeId != node.TypeId) continue;

            var ports = Math.Min(node.InputValues.Length, Math.Min(old.InputValues.Length, target.InputValues.Length));

            for (var port = 0; port < ports; port++)
            {
                var at = port;

                Merge(carried, old.InputValues[at], node.InputValues[at], target.InputValues[at],
                    value => target.InputValues[at] = value,
                    kept => new Retuned(node.Id, at, string.Empty, Number(node.InputValues[at]), kept));
            }

            Merge(carried, old.Name, node.Name, target.Name,
                name => target.Name = name,
                kept => new Retuned(node.Id, -1, "name", Quoted(node.Name ?? string.Empty), kept));

            Merge(carried, old.Off, node.Off, target.Off,
                off => target.Off = off,
                kept => new Retuned(node.Id, -1, "off", node.Off ? "true" : "false", kept));

            var keys = Keys(old).Union(Keys(node)).ToList();

            foreach (var key in keys)
            {
                Merge(carried, old.StateOf(key), node.StateOf(key), target.StateOf(key),
                    held => target.SetState(key, held?.DeepClone()),
                    kept => new Retuned(node.Id, -1, key, Json(node.StateOf(key)), kept),
                    JsonNode.DeepEquals);
            }
        }

        foreach (var knob in now.Controls ?? [])
        {
            if (Control(was, knob.Id) is not { } old || Control(onto, knob.Id) is not { } target) continue;

            Merge(carried, old.Value, knob.Value, target.Value,
                value => target.Value = value,
                kept => new Retuned(null, -1, $"knob {Quoted(knob.Name)}", Number(knob.Value), kept));

            Merge(carried, old.Name, knob.Name, target.Name,
                name => target.Name = name,
                kept => new Retuned(null, -1, $"knob {Quoted(old.Name)} name", Quoted(knob.Name), kept));
        }

        Merge(carried, was.Description, now.Description, onto.Description,
            words => onto.Description = words,
            kept => new Retuned(null, -1, "description", Quoted(now.Description ?? string.Empty), kept));

        Merge(carried, was.Author, now.Author, onto.Author,
            author => onto.Author = author,
            kept => new Retuned(null, -1, "author", Quoted(now.Author ?? string.Empty), kept));

        Merge(carried, was.Length, now.Length, onto.Length,
            length => onto.Length = length,
            kept => new Retuned(null, -1, "length", now.Length?.ToString(CultureInfo.InvariantCulture) ?? "default", kept));

        Merge(carried, was.Tags, now.Tags, onto.Tags,
            tags => onto.Tags = tags is null ? null : [.. tags],
            kept => new Retuned(null, -1, "tags", string.Join(' ', now.Tags ?? []), kept),
            (a, b) => (a ?? []).SequenceEqual(b ?? []));

        return carried;
    }

    /// <summary>A copy of everything <see cref="Carry"/> and <see cref="PatchShape"/> read, untouched by later edits to the original.</summary>
    public static Patch Copy(Patch patch) => new()
    {
        Nodes = [.. patch.Nodes.Select(node => node.Clone())],
        Connections = [.. patch.Connections],
        Controls = patch.Controls is { } knobs ? [.. knobs.Select(knob => knob.Clone())] : null,
        Description = patch.Description,
        Author = patch.Author,
        Tags = patch.Tags is { } tags ? [.. tags] : null,
        Length = patch.Length,
    };

    private static void Merge<T>(
        List<Retuned> carried,
        T was,
        T now,
        T onto,
        Action<T> set,
        Func<bool, Retuned> said,
        Func<T, T, bool>? same = null)
    {
        same ??= EqualityComparer<T>.Default.Equals;

        if (same(was, now) || same(onto, now)) return;

        var free = same(onto, was);

        if (free) set(now);

        carried.Add(said(free));
    }

    private static IEnumerable<string> Keys(NodeInstance node) =>
        node.State?.Keys ?? Enumerable.Empty<string>();

    private static PatchControl? Control(Patch patch, Guid id) =>
        patch.Controls?.FirstOrDefault(knob => knob.Id == id);

    private static string Number(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string? Quoted(string text) => text.Length > Longest ? null : $"\"{text}\"";

    private static string? Json(JsonNode? node)
    {
        var text = node?.ToJsonString() ?? "none";

        return text.Length > Longest ? null : text;
    }
}
