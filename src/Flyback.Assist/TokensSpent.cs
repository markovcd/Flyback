using System.Globalization;
using Flyback.Plugins.Assist;

namespace Flyback.Assist;

/// <summary>What a conversation has cost in tokens, as its provider reported each request.</summary>
/// <param name="Requests">How many requests reported a cost.</param>
/// <param name="Input">Tokens sent, cached ones included.</param>
/// <param name="CacheRead">How many of those were read from the provider's cache.</param>
/// <param name="Output">Tokens written.</param>
/// <param name="Context">Tokens the newest request sent: how large the conversation has grown.</param>
internal sealed record TokensSpent(int Requests = 0, int Input = 0, int CacheRead = 0, int Output = 0, int Context = 0)
{
    public bool None => Requests == 0;

    public TokensSpent Plus(PatchEvent.Cost cost) =>
        new(Requests + 1, Input + cost.Input, CacheRead + cost.CacheRead, Output + cost.Output, cost.Input);

    /// <summary>The footer's line: <c>3 turns · 87k in (80k cached) · 3.1k out · 36k of 100k context</c>.</summary>
    public string Told(int turns, int contextLimit) =>
        $"{turns} {(turns == 1 ? "turn" : "turns")} · {Short(Input)} in ({Short(CacheRead)} cached) · {Short(Output)} out"
        + $" · {Short(Context)} of {Short(contextLimit)} context";

    private static string Short(int count) => count switch
    {
        >= 1_000_000 => (count / 1_000_000d).ToString("0.##", CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (count / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "k",
        _ => count.ToString(CultureInfo.InvariantCulture),
    };
}
