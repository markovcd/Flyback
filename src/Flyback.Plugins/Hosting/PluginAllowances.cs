using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Core;

namespace Flyback.Plugins.Hosting;

/// <summary>
/// The plugin folders somebody said yes to, kept in the data folder: never in
/// <c>plugins/</c>, which is the folder a stranger's zip is extracted into.
/// </summary>
/// <remarks>
/// Written by the install dialog and <c>flyback-cli plugin allow</c>. Read afresh on every
/// question, since the CLI may have changed it since this process last looked.
/// </remarks>
internal sealed class PluginAllowances(string file)
{
    public static string DefaultFile => Path.Combine(GlobalConstants.DataFolder, "allowed-plugins.json");

    /// <summary>Nobody's list: nothing allowed, and nothing written.</summary>
    public static PluginAllowances None { get; } = new("");

    public string File { get; } = file;

    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public IReadOnlyList<PluginAllowance> All() => Load().Plugins;

    /// <summary>What was allowed for <paramref name="folder"/>, or null where nothing was.</summary>
    public PluginAllowance? For(string folder)
    {
        var path = Normal(folder);

        return All().FirstOrDefault(a => string.Equals(a.Folder, path, PathComparison));
    }

    /// <summary>Says yes to <paramref name="folder"/> as it stands now.</summary>
    public PluginAllowance Allow(string folder, bool secrets) => Allow(Allowance(folder, secrets));

    /// <summary>Keeps <paramref name="allowance"/> in place of whatever was allowed for its folder.</summary>
    public PluginAllowance Allow(PluginAllowance allowance)
    {
        var kept = allowance with { Folder = Normal(allowance.Folder) };
        var (adopted, plugins) = Load();

        Write(adopted, [.. plugins.Where(a => !string.Equals(a.Folder, kept.Folder, PathComparison)), kept]);

        return kept;
    }

    /// <summary>Takes back the yes to <paramref name="folder"/>. False where there was none.</summary>
    public bool Deny(string folder)
    {
        var path = Normal(folder);
        var (adopted, plugins) = Load();
        var left = plugins.Where(a => !string.Equals(a.Folder, path, PathComparison)).ToList();

        if (left.Count == plugins.Count) return false;

        Write(adopted, left);

        return true;
    }

    /// <summary>
    /// Says yes, once per plugins folder, to every folder in it a package installed: each
    /// was installed with a yes before there was a list to keep it in.
    /// </summary>
    public void Adopt(string directory)
    {
        var root = Normal(directory);
        var (adopted, plugins) = Load();

        if (File.Length == 0 || adopted.Contains(root, StringComparer.FromComparison(PathComparison)) || !Directory.Exists(root)) return;

        try
        {
            var installed = PluginHost.Folders(root)
                .Where(folder => System.IO.File.Exists(Path.Combine(folder, PluginPackage.MarkerName)))
                .Select(folder => Allowance(folder, secrets: false))
                .Where(a => !plugins.Any(p => string.Equals(p.Folder, a.Folder, PathComparison)));

            Write([.. adopted, root], [.. plugins, .. installed]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing is adopted, so those folders are listed as not yet allowed.
        }
    }

    private static PluginAllowance Allowance(string folder, bool secrets)
    {
        var entry = PluginHost.EntryAssemblies(folder).FirstOrDefault();
        var key = Path.Combine(folder, PluginPackage.KeyMarkerName);
        var signer = System.IO.File.Exists(key) ? PackageSigner.Parse(System.IO.File.ReadAllText(key))?.Key : null;

        return new PluginAllowance(
            Normal(folder),
            entry is null ? Path.GetFileName(Normal(folder)) : Path.GetFileNameWithoutExtension(entry),
            signer,
            secrets,
            PluginFiles.Of(folder));
    }

    private static string Normal(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

    private (IReadOnlyList<string> Adopted, IReadOnlyList<PluginAllowance> Plugins) Load()
    {
        try
        {
            if (File.Length == 0 || !System.IO.File.Exists(File) || JsonNode.Parse(System.IO.File.ReadAllText(File)) is not JsonObject root) return ([], []);

            var adopted = root["adopted"] is JsonArray folders
                ? folders.OfType<JsonValue>().Select(f => f.GetValue<string>()).ToList()
                : [];

            var plugins = root["plugins"] is JsonArray list
                ? list.OfType<JsonObject>().Select(Read).OfType<PluginAllowance>().ToList()
                : [];

            return (adopted, plugins);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return ([], []);
        }
    }

    private void Write(IReadOnlyList<string> adopted, IReadOnlyList<PluginAllowance> allowances)
    {
        if (File.Length == 0) throw new InvalidOperationException("This list is nobody's, and nothing is written to it.");

        var list = new JsonArray();

        foreach (var a in allowances)
        {
            var files = new JsonObject();

            foreach (var (path, hash) in a.Files.Hashes) files[path] = hash;

            list.Add(new JsonObject
            {
                ["folder"] = a.Folder,
                ["assembly"] = a.Assembly,
                ["signer"] = a.Signer,
                ["secrets"] = a.Secrets,
                ["files"] = files,
            });
        }

        var root = new JsonObject
        {
            ["adopted"] = new JsonArray([.. adopted.Select(f => JsonValue.Create(f))]),
            ["plugins"] = list,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(File))!);

        // Written beside and moved over, so a reader never sees half a list.
        var temporary = File + ".new";
        System.IO.File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        System.IO.File.Move(temporary, File, overwrite: true);
    }

    private static PluginAllowance? Read(JsonObject entry)
    {
        if (entry["folder"]?.GetValue<string>() is not { } folder || entry["files"] is not JsonObject files) return null;

        return new PluginAllowance(
            folder,
            entry["assembly"]?.GetValue<string>() ?? Path.GetFileName(folder),
            entry["signer"]?.GetValue<string>(),
            entry["secrets"]?.GetValue<bool>() ?? false,
            PluginFiles.From(files
                .Where(f => f.Value is JsonValue)
                .Select(f => KeyValuePair.Create(f.Key, f.Value!.GetValue<string>()))));
    }
}
