namespace Flyback.Plugins.Hosting;

/// <summary>
/// Which plugin folders load: the ones this copy of Flyback was built with, and the ones
/// somebody said yes to, each only while its files are as they were.
/// </summary>
/// <remarks>
/// A build that checks no package signatures (<see cref="PackageSigner.Checked"/>) checks
/// no folders either, so a Debug build loads whatever is in <c>plugins/</c>.
/// </remarks>
/// <param name="check">Whether to check at all.</param>
internal sealed class PluginTrust(bool check, ShippedList shipped, PluginAllowances allowances)
{
    public static PluginTrust Unchecked { get; } = new(false, ShippedList.Empty, PluginAllowances.None);

    /// <summary>What loads from <paramref name="directory"/> on what it was built with alone, whoever allowed what: the tests' trust.</summary>
    public static PluginTrust Shipped(string directory) => new(PackageSigner.Checked, ShippedList.Beside(directory), PluginAllowances.None);

    /// <summary>The trust a plugins folder is loaded with here.</summary>
    public static PluginTrust For(string directory)
    {
        var allowances = new PluginAllowances(PluginAllowances.DefaultFile);

        if (PackageSigner.Checked) allowances.Adopt(directory);

        return new(PackageSigner.Checked, ShippedList.Beside(directory), allowances);
    }

    public bool Checks => check;

    public PluginAllowances Allowances => allowances;

    public PluginVerdict Judge(string folder)
    {
        if (!check) return new PluginVerdict(PluginStanding.Unchecked, true, null);

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        var files = PluginFiles.Of(folder);
        var allowed = allowances.For(folder);
        var built = shipped.Of(name);

        var fromAllowance = allowed is null ? null : files.Changed(allowed.Files);
        var fromBuild = built is null ? null : files.Changed(built);

        if (allowed is not null && fromAllowance is null)
            return new PluginVerdict(PluginStanding.Allowed, allowed.Secrets || (built is not null && fromBuild is null), null);

        if (built is not null && fromBuild is null) return new PluginVerdict(PluginStanding.Shipped, true, null);

        if (allowed is not null)
        {
            return new PluginVerdict(
                PluginStanding.Changed,
                false,
                $"{fromAllowance} has changed since this plugin was allowed, so it is not loaded. {Command(folder)} allows it as it is now.");
        }

        if (built is not null)
        {
            return new PluginVerdict(
                PluginStanding.Changed,
                false,
                $"{fromBuild} is not as Flyback shipped it, so this plugin is not loaded.");
        }

        return new PluginVerdict(
            PluginStanding.NotAllowed,
            false,
            $"not yet allowed, so not loaded. Install it from its package, or {Command(folder)} allows it.");
    }

    private static string Command(string folder) => $"flyback-cli plugin allow \"{Path.GetFullPath(folder)}\"";
}
