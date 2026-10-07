using System.Text.Json;
using Flyback.Assist;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;

namespace Flyback.Cli.Commands;

/// <summary>
/// <c>ask --expand</c>: the assistant writes a short message out in full, as the editor's Expand does,
/// and the result is printed. Nothing is built, saved or carried into a conversation.
/// </summary>
internal static class AskExpansion
{
    private static readonly JsonSerializerOptions Line = new(Writing.Json) { WriteIndented = false };

    public static async Task<int> Run(
        IPatchAssistant assistant,
        AssistantConfig config,
        AssistantSettings settings,
        string? settingsPath,
        ModuleCatalog modules,
        IReadOnlyList<PatchPreset> presets,
        AskedPatch about,
        AskOptions options,
        string message,
        TextWriter output,
        TextWriter error,
        CancellationToken cancel)
    {
        var writing = config with { Values = AssistantSchema.Expanding(config.Values) };

        if (AssistantRun.Unready(assistant, writing) is { } excuse)
        {
            error.WriteLine(AskedPatch.Complaint(excuse));
            return Exit.Failed;
        }

        var over = about.Opened.Patch;

        using var run = new AssistantRun(
            assistant,
            writing,
            modules,
            over,
            options.Context ?? settings.ContextLimit,
            samples: about.Opened.Samples,
            pictures: about.Opened.Pictures,
            prose: settings.Prose(settingsPath),
            presets: presets);

        var (brief, failure) = await PromptExpansion.ExpandAsync(run, message, over, cancel).ConfigureAwait(false);

        if (brief is null)
        {
            error.WriteLine(AskedPatch.Complaint(failure ?? "it wrote nothing."));
            return Exit.Failed;
        }

        output.WriteLine(options.Json
            ? JsonSerializer.Serialize(new { kind = "brief", text = brief }, Line)
            : brief);

        return Exit.Ok;
    }
}
