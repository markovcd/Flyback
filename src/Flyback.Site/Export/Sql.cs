using System.Globalization;
using System.Text;

namespace Flyback.Site;

/// <summary>Values written as SQLite literals, one statement to a line, as D1 runs a file.</summary>
internal static class Sql
{
    /// <summary>
    /// Plain where it is plain text, and hex otherwise, so no newline, quote, semicolon or
    /// comment marker in a description can end a statement early in anything that splits them.
    /// </summary>
    public static string Of(object? value) => value switch
    {
        null => "NULL",
        bool flag => flag ? "1" : "0",
        long number => number.ToString(CultureInfo.InvariantCulture),
        int number => number.ToString(CultureInfo.InvariantCulture),
        byte[] bytes => "X'" + Convert.ToHexString(bytes) + "'",
        string text when Plain(text) => "'" + text + "'",
        string text => "CAST(X'" + Convert.ToHexString(Encoding.UTF8.GetBytes(text)) + "' AS TEXT)",
        _ => throw new ArgumentException($"There is no SQL literal for a {value.GetType().Name}.", nameof(value)),
    };

    private static bool Plain(string text) =>
        text.All(c => c is >= ' ' and <= '~' and not '\'' and not ';')
        && !text.Contains("--", StringComparison.Ordinal)
        && !text.Contains("/*", StringComparison.Ordinal);

    public static string Insert(string table, IReadOnlyList<(string Column, object? Value)> row) =>
        $"INSERT INTO {table} ({string.Join(", ", row.Select(c => c.Column))}) VALUES ({string.Join(", ", row.Select(c => Of(c.Value)))});";
}
