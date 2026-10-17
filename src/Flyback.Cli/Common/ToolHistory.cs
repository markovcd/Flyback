using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flyback.Cli.Models;
using Flyback.Core;

namespace Flyback.Cli.Common;

/// <summary>
/// The undo history of <c>flyback-cli tool</c>, one file for each patch file it has edited,
/// in the application's own folder rather than beside the patch.
/// </summary>
/// <param name="folder">Somewhere other than the usual place, for the tests.</param>
internal sealed class ToolHistory(string? folder = null)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string root = folder ?? Folder;

    public static string Folder => Path.Combine(GlobalConstants.DataFolder, "tools");

    /// <summary>The history kept for the patch file at <paramref name="path"/>, or null where none is, or it cannot be read.</summary>
    public ToolLog? Find(string path)
    {
        try
        {
            var file = FileFor(path);

            if (!File.Exists(file)) return null;

            var log = JsonSerializer.Deserialize<ToolLog>(File.ReadAllText(file), Options);

            return log is not null
                && string.Equals(log.Patch, Path.GetFullPath(path), Comparison)
                && log.Steps is not null
                && log.At >= 0 && log.At <= log.Steps.Count
                ? log
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Keeps <paramref name="log"/>, or says why it could not.</summary>
    public string? Keep(ToolLog log)
    {
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(FileFor(log.Patch), JsonSerializer.Serialize(log, Options));

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"the undo history could not be kept: {ex.Message}";
        }
    }

    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private string FileFor(string path)
    {
        var named = Path.GetFullPath(path);

        if (Comparison == StringComparison.OrdinalIgnoreCase) named = named.ToUpperInvariant();

        return Path.Combine(root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(named)))[..32] + ".json");
    }
}
