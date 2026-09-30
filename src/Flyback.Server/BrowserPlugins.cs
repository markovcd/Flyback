using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;

namespace Flyback.Server;

/// <summary>
/// The modules the web viewer and editor link in, and what a shared preset needs beyond them.
/// </summary>
/// <remarks>
/// Each preset is read once and remembered by id: a stored file only changes as the
/// site starts, when the defaults are seeded.
/// </remarks>
internal sealed class BrowserPlugins(ModuleCatalog modules)
{
    private const char ByteOrderMark = (char)0xFEFF;

    private readonly ConcurrentDictionary<string, BrowserLack?> read = new();

    /// <summary>The plugins this build of the pages links, as the site's project names them.</summary>
    public static BrowserPlugins Linked() =>
        new(PluginHost.LoadLinked(typeof(BrowserPlugins).Assembly, "WebPlugin").Modules);

    /// <summary>What the pages lack to open the preset stored as <paramref name="id"/>, or null where they open it.</summary>
    public BrowserLack? Lacking(string id, Func<(string FileName, byte[] File)?> file) =>
        read.GetOrAdd(id, _ => file() is var (name, bytes) ? Lacking(name, bytes) : null);

    /// <summary>What the pages lack to open <paramref name="file"/>, or null where they open it or it is not a patch.</summary>
    public BrowserLack? Lacking(string fileName, byte[] file)
    {
        PatchLoad? load;

        try
        {
            load = string.Equals(Path.GetExtension(fileName), PatchBundle.Extension, StringComparison.OrdinalIgnoreCase)
                ? PatchBundle.Read(new MemoryStream(file, writable: false), modules, Submissions.BundleLimit).Load
                : PatchIO.Read(new UTF8Encoding(false, true).GetString(file).TrimStart(ByteOrderMark), modules);
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or IOException or DecoderFallbackException)
        {
            return null;
        }

        if (load is null || load.TooNew || load.MissingProviders.Count + load.UnknownModules.Count == 0) return null;

        return new BrowserLack(load.MissingProviders, load.UnknownModules.Count);
    }
}
