namespace Flyback.Plugins.Hosting;

/// <summary>
/// Keeps installed plugins out of a macOS bundle. Anything written into a signed bundle
/// breaks its signature, and <c>codesign</c> takes a folder in <c>Contents/MacOS</c> whose
/// name has a dot in it, as an installed plugin's does, for a nested bundle it cannot sign.
/// </summary>
internal static class BundlePlugins
{
    /// <summary>Whether this process runs from inside a macOS <c>.app</c>.</summary>
    public static bool Running { get; } = OperatingSystem.IsMacOS() && Around(AppContext.BaseDirectory) is not null;

    /// <summary>
    /// The <c>.app</c> whose <c>Contents/MacOS</c> is <paramref name="directory"/>, or null
    /// where it is not one.
    /// </summary>
    public static string? Around(string directory)
    {
        var macOs = Path.TrimEndingDirectorySeparator(directory);
        var contents = Path.GetDirectoryName(macOs);
        var bundle = contents is null ? null : Path.GetDirectoryName(contents);

        return Path.GetFileName(macOs) == "MacOS" && Path.GetFileName(contents) == "Contents"
            && bundle is not null && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            ? bundle
            : null;
    }

    /// <summary>
    /// Moves every plugin folder in <paramref name="shipped"/> that its build did not ship
    /// into <paramref name="installed"/>, and points what was allowed for it at where it went.
    /// </summary>
    /// <returns>What could not be moved, one line each.</returns>
    public static IReadOnlyList<string> MoveOut(string shipped, string installed, PluginAllowances allowances)
    {
        var problems = new List<string>();

        // Without the list nothing tells a shipped folder from an installed one.
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(shipped)));
        if (!Directory.Exists(shipped) || parent is null || !File.Exists(Path.Combine(parent, ShippedList.FileName))) return problems;

        var list = ShippedList.Beside(shipped);

        foreach (var folder in Directory.EnumerateDirectories(shipped).ToList())
        {
            var name = Path.GetFileName(folder);

            if (list.Of(name) is not null) continue;

            try
            {
                Directory.CreateDirectory(installed);

                var target = Path.Combine(installed, name);

                // One already there is what loads, so this copy never would.
                if (Directory.Exists(target)) Directory.Delete(folder, recursive: true);
                else Directory.Move(folder, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{name} could not be moved out of the bundle: {ex.Message}");
            }
        }

        Repoint(shipped, installed, allowances);

        return problems;
    }

    /// <summary>
    /// Points each allowance for a folder gone from <paramref name="shipped"/> at the folder of the
    /// same name in <paramref name="installed"/>. The files it lists are still checked there.
    /// </summary>
    private static void Repoint(string shipped, string installed, PluginAllowances allowances)
    {
        var from = Path.TrimEndingDirectorySeparator(Path.GetFullPath(shipped));
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (var allowance in allowances.All())
        {
            if (!string.Equals(Path.GetDirectoryName(allowance.Folder), from, comparison) || Directory.Exists(allowance.Folder)) continue;

            var target = Path.Combine(installed, Path.GetFileName(allowance.Folder));

            if (!Directory.Exists(target) || allowances.For(target) is not null) continue;

            allowances.Allow(allowance with { Folder = target });
            allowances.Deny(allowance.Folder);
        }
    }
}
