using System.Text;
using Flyback.App.Statistics;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.App;

/// <summary>The presets shared on the preset site, which the gallery lists after everything on this machine.</summary>
public sealed partial class MainWindow
{
    /// <summary>What the gallery asks for shared presets, or null where this window has no site.</summary>
    private PresetSite? PresetSite() => presetSite is null ? null : new PresetSite(SiteHttp ?? SiteClient.Value, presetSite);

    /// <summary>
    /// Downloads a shared preset and opens it as a document named after it, with no
    /// folder of its own, as a preset is. Asks about unsaved work first.
    /// </summary>
    private async Task OpenSharedPresetAsync(SitePreset shared)
    {
        if (PresetSite() is not { } site || !await MayReplaceThePatchAsync()) return;

        Report($"Downloading “{shared.Name}” from the preset site…");

        byte[] bytes;

        try
        {
            bytes = await site.DownloadAsync(shared, CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Report($"Could not download “{shared.Name}”: {ex.Message}", site.Root.ToString());
            return;
        }

        try
        {
            Patch patch;
            string? conversation = null;

            if (PatchFileKinds.Bundled(shared.FileName))
            {
                var bundle = PatchBundle.Read(new MemoryStream(bytes, writable: false), plugins.Modules);

                if (bundle.Load is { IsComplete: false } lacking)
                {
                    Report($"Not opened. {lacking.Summary}", lacking.Detail);
                    await OfferMissingPluginsAsync(lacking);
                    return;
                }

                Became(shared.Name, beside: null, new BundleFiles(bundle.Files, soundFolder, pictureFolder));
                patch = bundle.Patch;
                conversation = bundle.Conversation;
            }
            else
            {
                var loaded = PatchIO.Read(Encoding.UTF8.GetString(bytes));

                if (!loaded.IsComplete)
                {
                    Report($"Not opened. {loaded.Summary}", loaded.Detail);
                    await OfferMissingPluginsAsync(loaded);
                    return;
                }

                Became(shared.Name, beside: null);
                patch = loaded.Patch;
            }

            ClearPresetSelection();

            usage.Count(Used.Opened);

            editor.Patch = patch;
            RewindToZero();
            DropSource();
            assistant?.Open(conversation);

            // Read into text where it was picked from the text view, as a preset is.
            if (showingCode) ReadIntoText();

            Report($"Opened “{shared.Name}” from the preset site.");
        }
        catch (Exception ex)
        {
            Report($"Could not open “{shared.Name}”: {ex.Message}");
        }
    }
}
