using Flyback.Plugins.Hosting;

namespace Flyback.Server;

internal static class PluginSubmissions
{
    /// <summary>Half what the editor accepts: the site holds every package in its database.</summary>
    public static PackageLimits Limits { get; } = new(64L << 20, 256L << 20, 4096);

    /// <summary>The package in <paramref name="file"/>, read the way the editor reads it and without running it.</summary>
    /// <param name="checkKeys">Whether to refuse an unsigned package; <see cref="PackageSigner.Checked"/> unless said otherwise.</param>
    /// <exception cref="InvalidDataException">Where the editor would refuse it, saying why.</exception>
    public static PluginSubmission Read(string fileName, byte[] file, bool? checkKeys = null)
    {
        if (!PluginPackage.Named(fileName))
            throw new InvalidDataException($"That is not a plugin package. Send a {PluginPackage.Extension} file.");

        var package = PluginPackage.Read(file, Limits);

        if (package.Builds.Count == 0) throw new InvalidDataException("It holds no plugin for any system.");

        if (package.Signer is null && (checkKeys ?? PackageSigner.Checked))
            throw new InvalidDataException("It is not signed, and Flyback installs only signed plugins. Pack it with flyback-cli pack-plugin --key.");

        var descriptions = package.Builds.Select(package.Description).ToList();
        var first = descriptions[0];

        if (descriptions.Any(d => !string.Equals(d.Assembly, first.Assembly, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Its builds are of different plugins.");

        return new PluginSubmission(
            first.Assembly,
            first.Name,
            first.Version,
            first.Author,
            first.Description,
            first.Tags,
            first.Preview,
            [.. descriptions.SelectMany(d => d.Adds).Distinct().Order(StringComparer.Ordinal)],
            [.. descriptions.SelectMany(d => d.Reaches).Distinct().Order(StringComparer.Ordinal)],
            package.Builds,
            first.Compiled[0].References
                .Where(r => ContractVersion.IsContract(r.Name))
                .ToDictionary(r => r.Name!, r => r.Version?.ToString(3) ?? "", StringComparer.Ordinal),
            [.. descriptions.SelectMany(d => d.Modules).DistinctBy(m => m.TypeId)],
            package.Sha256,
            package.Signer,
            file);
    }
}