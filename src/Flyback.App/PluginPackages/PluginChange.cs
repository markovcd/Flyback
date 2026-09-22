using System.Globalization;
using Flyback.Plugins.Hosting;

namespace Flyback.App.PluginPackages;

/// <summary>What installing a package does to the plugin already in its folder.</summary>
internal enum PluginChange
{
    Install,
    Update,
    Reinstall,
    Downgrade,

    /// <summary>Replaces a plugin whose version cannot be put in order with the package's.</summary>
    Replace,
}

internal static class PluginChanges
{
    /// <summary>What installing <paramref name="incoming"/> over <paramref name="installed"/> is.</summary>
    public static PluginChange Of(PluginDescription? installed, PluginDescription incoming) =>
        installed is null ? PluginChange.Install
        : Compare(incoming.Version, installed.Version) switch
        {
            > 0 => PluginChange.Update,
            0 => PluginChange.Reinstall,
            < 0 => PluginChange.Downgrade,
            null => PluginChange.Replace,
        };

    /// <summary>The word on the dialog's button and in its title.</summary>
    public static string Verb(this PluginChange change) => change switch
    {
        PluginChange.Update => "Update",
        PluginChange.Reinstall => "Reinstall",
        PluginChange.Downgrade => "Downgrade",
        PluginChange.Replace => "Replace",
        _ => "Install",
    };

    /// <summary>
    /// How two versions such as <c>1.2.0</c> and <c>1.10.0-beta.2</c> are ordered, by
    /// Semantic Versioning's rules, or null where either is not one.
    /// </summary>
    public static int? Compare(string left, string right)
    {
        if (Parse(left) is not var (leftNumbers, leftLabel) || Parse(right) is not var (rightNumbers, rightLabel)) return null;

        for (var i = 0; i < Math.Max(leftNumbers.Length, rightNumbers.Length); i++)
        {
            var order = (i < leftNumbers.Length ? leftNumbers[i] : 0).CompareTo(i < rightNumbers.Length ? rightNumbers[i] : 0);

            if (order != 0) return Math.Sign(order);
        }

        // A prerelease comes before its release.
        if (leftLabel is null || rightLabel is null) return (leftLabel is null ? 1 : 0) - (rightLabel is null ? 1 : 0);

        var leftParts = leftLabel.Split('.');
        var rightParts = rightLabel.Split('.');

        for (var i = 0; i < Math.Min(leftParts.Length, rightParts.Length); i++)
        {
            var leftNumber = long.TryParse(leftParts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var l);
            var rightNumber = long.TryParse(rightParts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var r);

            var order = leftNumber && rightNumber ? l.CompareTo(r)
                : leftNumber ? -1
                : rightNumber ? 1
                : string.CompareOrdinal(leftParts[i], rightParts[i]);

            if (order != 0) return Math.Sign(order);
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private static (long[] Numbers, string? Label)? Parse(string version)
    {
        var dash = version.IndexOf('-');
        var core = dash < 0 ? version : version[..dash];
        var label = dash < 0 ? null : version[(dash + 1)..];
        var parts = core.Split('.');

        if (parts.Length is < 1 or > 4 || label is "") return null;

        var numbers = new long[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            if (!long.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return null;
        }

        return (numbers, label);
    }
}
