namespace Flyback.Core.Graph;

/// <summary>Shortens typed text without splitting a character in two.</summary>
internal static class TextLimit
{
    /// <summary>
    /// At most <paramref name="limit"/> UTF-16 units of <paramref name="text"/>, one
    /// fewer where the cut would leave half a surrogate pair.
    /// </summary>
    public static string Clip(string text, int limit) =>
        text.Length <= limit ? text
        : limit > 0 && char.IsHighSurrogate(text[limit - 1]) ? text[..(limit - 1)]
        : text[..Math.Max(limit, 0)];
}
