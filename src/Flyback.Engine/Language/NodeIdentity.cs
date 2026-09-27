namespace Flyback.Core.Language;

/// <summary>Names placed modules by their source path so rebuilds keep their identities.</summary>
internal sealed class NodeIdentity
{
    private readonly HashSet<Guid> issued = [];

    private string where = string.Empty;
    private int placedHere;
    private int unnamedHere;

    internal readonly record struct Position(string Where, int Placed, int Unnamed);

    /// <summary>Enters a nested source segment, saving the current naming position.</summary>
    internal Position Enter(string segment)
    {
        var outer = new Position(where, placedHere, unnamedHere);

        where = where.Length == 0 ? segment : where + "/" + segment;
        placedHere = 0;
        unnamedHere = 0;

        return outer;
    }

    /// <summary>Restores the naming position saved by <see cref="Enter"/>.</summary>
    internal void Leave(Position outer) =>
        (where, placedHere, unnamedHere) = (outer.Where, outer.Placed, outer.Unnamed);

    internal string Anonymous() => "#" + unnamedHere++;

    internal string Stamping(string name) => name + "~" + unnamedHere++;

    /// <summary>Allocates the next stable module id in the current source segment.</summary>
    /// <remarks>Colliding placements in already-invalid source still receive distinct ids.</remarks>
    internal Guid Next()
    {
        var name = $"{where}#{placedHere++}";
        var id = FromName(name);

        for (var again = 1; !issued.Add(id); again++) id = FromName($"{name}'{again}");

        return id;
    }

    /// <summary>A guid from a name, the same one every time.</summary>
    /// <remarks>
    /// A hash rather than a counter, because what has to be stable is the mapping
    /// from a piece of source to an id — across runs, across machines, and with
    /// statements added around it. SHA-256 cut to sixteen bytes: this is a name
    /// and not a secret.
    /// </remarks>
    internal static Guid FromName(string name) =>
        new(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(name)).AsSpan(0, 16));

    internal static Guid PanelId(string word) => FromName("panel " + word);
}
