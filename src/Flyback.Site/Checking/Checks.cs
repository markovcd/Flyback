using System.Text.Json;
using System.Text.Json.Serialization;
using Flyback.Plugins.Hosting;
using Flyback.Site.Reading;

namespace Flyback.Site.Checking;

/// <summary>The check of whichever kind of file a submission is, and the JSON it is sent to the site as.</summary>
internal static class Checks
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Whether a file named so is a plugin package rather than a preset, for a file that came without saying.</summary>
    public static bool IsPlugin(string fileName) => PluginPackage.Named(fileName);

    /// <summary>Whether a file named so is a module pack.</summary>
    public static bool IsPack(string fileName) => fileName.EndsWith(InstalledPacks.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The check of <paramref name="file"/> as a plugin package or a preset. A file the
    /// readers fail on in a way they do not report is refused as unreadable, so one
    /// hostile file cannot stall the queue.
    /// </summary>
    public static object Of(bool plugin, string fileName, byte[] file, string? name, BrowserPlugins browser, TextWriter error)
    {
        try
        {
            return plugin ? PluginCheck.Of(fileName, file) : PresetCheck.Of(fileName, file, name, browser);
        }
        catch (Exception unexpected)
        {
            error.WriteLine($"{fileName}: the readers failed: {unexpected.GetType().Name}: {unexpected.Message}");
            const string unreadable = "Flyback could not read it.";
            return plugin ? new PluginCheck(false, unreadable) : new PresetCheck(false, unreadable);
        }
    }

    /// <summary>The check of <paramref name="file"/> as a module pack, refused as unreadable where the reader fails unreported.</summary>
    public static PackCheck OfPack(string fileName, byte[] file, TextWriter error)
    {
        try
        {
            return PackCheck.Of(fileName, file);
        }
        catch (Exception unexpected)
        {
            error.WriteLine($"{fileName}: the readers failed: {unexpected.GetType().Name}: {unexpected.Message}");
            return new PackCheck(false, "Flyback could not read it.");
        }
    }

    public static bool Accepted(object check) => check is PresetCheck { Accepted: true } or PluginCheck { Accepted: true } or PackCheck { Accepted: true };

    public static string? Reason(object check) => check switch
    {
        PresetCheck preset => preset.Reason,
        PluginCheck plugin => plugin.Reason,
        PackCheck pack => pack.Reason,
        _ => null,
    };

    public static string ToJson(object check) => JsonSerializer.Serialize(check, check.GetType(), Json);
}
