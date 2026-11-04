namespace Flyback.Editor.Canvas;

/// <summary>
/// What the canvas worked out last time it drew, kept by what it was worked out from, so a
/// pan or a zoom, which moves nothing in graph units, redraws without working it out again.
/// </summary>
/// <remarks>
/// One store per thread, so two canvases drawing at once never share a half-made value.
/// Emptied whole once it holds <c>most</c>: whatever is still on screen is made again on the next frame.
/// </remarks>
internal sealed class Remembered<TKey, TValue>(int most) : IDisposable where TKey : notnull
{
    private readonly ThreadLocal<Dictionary<TKey, TValue>> kept = new(() => []);

    public TValue Get(TKey key, Func<TKey, TValue> make)
    {
        var store = kept.Value!;

        if (store.TryGetValue(key, out var known)) return known;
        if (store.Count >= most) store.Clear();

        return store[key] = make(key);
    }

    public void Dispose() => kept.Dispose();
}
