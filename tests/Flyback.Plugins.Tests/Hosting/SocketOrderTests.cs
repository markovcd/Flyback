using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Hosting;

/// <summary>
/// Every module's sockets by position, the engine's and every plugin's, held to <c>sockets.txt</c>.
/// A saved patch names a socket by its number, so one inserted anywhere but the end rewires it.
/// </summary>
public sealed class SocketOrderTests
{
    private const string Kept = "sockets.txt";

    [Fact]
    public void Every_modules_sockets_keep_their_positions()
    {
        var listing = Listing();
        var kept = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, Kept)).ReplaceLineEndings("\n");

        if (listing == kept) return;

        var received = Path.Combine(AppContext.BaseDirectory, "sockets.received.txt");
        File.WriteAllText(received, listing);

        var changed = listing.Split('\n').Except(kept.Split('\n')).ToList();
        var gone = kept.Split('\n').Except(listing.Split('\n')).ToList();

        changed.ShouldBeEmpty(
            $"A new socket goes at the end of its module. Kept: {string.Join(" / ", gone)}. "
            + $"Where only the end changed, copy {received} over tests/Flyback.Plugins.Tests/{Kept}.");
        gone.ShouldBeEmpty($"A module has gone from {Kept}.");
    }

    /// <summary>One line per module, by type id: <c>typeId | inputs… | outputs…</c>.</summary>
    private static string Listing() => string.Concat(
        ShippedPlugins.Loaded.Modules.All
            .OrderBy(m => m.TypeId, StringComparer.Ordinal)
            .Select(m => $"{m.TypeId} | {Names(m.Inputs)} | {Names(m.Outputs)}\n"));

    private static string Names(IReadOnlyList<PortSpec> sockets) => string.Join(", ", sockets.Select(s => s.Name));
}
