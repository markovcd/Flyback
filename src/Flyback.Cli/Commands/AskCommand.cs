using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;

namespace Flyback.Cli.Commands;

/// <summary>
/// Asks an assistant to change a patch and writes its answer back, with the
/// conversation, where the editor would find both.
/// </summary>
/// <remarks>
/// A message on the line is one turn. None, and standard input is the message
/// where it is redirected, or a prompt that keeps asking where it is a terminal.
/// </remarks>
internal static class AskCommand
{
    /// <param name="settingsPath">Somewhere other than the usual assistant settings, for the tests.</param>
    /// <param name="store">Where conversations about loose files are kept, for the tests.</param>
    /// <param name="logFolder">Where a logged conversation is written, for the tests.</param>
    /// <param name="console">Where a prompt is answered, or null where nobody is there to answer one.</param>
    public static async Task<int> Run(
        PluginCatalog plugins,
        AskedPatch about,
        AskOptions options,
        TextWriter output,
        TextWriter error,
        TextReader input,
        TextReader? console,
        CancellationToken cancel,
        string? settingsPath = null,
        ConversationStore? store = null,
        string? logFolder = null)
    {
        var settings = AssistantSettings.Load(settingsPath);

        var wanted = options.Provider ?? (string.IsNullOrWhiteSpace(settings.Provider) ? null : settings.Provider);
        var assistant = wanted is null ? plugins.PreferredAssistant : plugins.Assistant(wanted);

        if (assistant is null)
        {
            error.WriteLine(AskedPatch.Complaint(wanted is null
                ? "no assistant is installed."
                : $"no assistant called '{wanted}' is installed."));

            if (plugins.Assistants.Count > 0)
                error.WriteLine($"Installed: {string.Join(", ", plugins.Assistants.Select(a => a.Id))}.");

            return Exit.Failed;
        }

        if (Values(settings.Of(assistant.Id), options.Set, error) is not { } values) return Exit.Failed;

        var config = new AssistantConfig(new Credentials(plugins.PreferredSecretStore).Transport(assistant, values), values);

        if (AssistantRun.Unready(assistant, config) is { } excuse)
        {
            error.WriteLine(AskedPatch.Complaint(excuse));
            return Exit.Failed;
        }

        var message = options.Message;

        // Read before anything is started, so a pipe with nothing in it costs nothing.
        if (message is null && console is null)
        {
            message = await input.ReadToEndAsync(cancel).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(message))
            {
                error.WriteLine(AskedPatch.Complaint("say what to ask: a message after the patch, or one on standard input."));
                return Exit.Failed;
            }
        }

        using var conversation = new AskConversation(
            assistant,
            config,
            settings,
            settingsPath ?? AssistantSettings.File,
            plugins.Modules,
            plugins.Presets,
            about,
            options,
            store ?? new ConversationStore(),
            logFolder,
            output,
            error);

        if (message is not null)
        {
            await conversation.Ask(message.Trim(), cancel).ConfigureAwait(false);

            return conversation.Failed ? Exit.Failed : Exit.Ok;
        }

        output.WriteLine("Ask away. An empty line or end of input stops.");

        while (!cancel.IsCancellationRequested)
        {
            output.Write("> ");

            if (console!.ReadLine()?.Trim() is not { Length: > 0 } line) break;

            await conversation.Ask(line, cancel).ConfigureAwait(false);
        }

        return conversation.Failed ? Exit.Failed : Exit.Ok;
    }

    /// <summary>
    /// The patch, the file it goes back to, and what was said about it before; or
    /// null with the reason already written to <paramref name="error"/>.
    /// </summary>
    /// <remarks>A file that does not exist yet starts from the empty patch, and is written on the first answer.</remarks>
    public static AskedPatch? Open(
        PluginCatalog plugins,
        FileInfo? file,
        string? preset,
        FileInfo? into,
        TextWriter error,
        ConversationStore? store = null)
    {
        if ((file is null) == (preset is null))
        {
            error.WriteLine(AskedPatch.Complaint("say what to ask about: a patch, or --preset and its name."));
            return null;
        }

        var target = into ?? file;

        if (target is null)
        {
            error.WriteLine(AskedPatch.Complaint($"--out says where to write what it makes of the preset, {AskedPatch.Formats}."));
            return null;
        }

        if (!AskedPatch.Writable(target))
        {
            error.WriteLine(AskedPatch.Complaint($"{target.Name}: the extension says what to write, {AskedPatch.Formats}."));
            return null;
        }

        if (preset is not null)
        {
            if (ShippedPresets.Open(plugins, preset, error) is not { } shipped) return null;

            var carried = (shipped.Opened.Samples as BundleFiles)?.Bytes;

            return new AskedPatch(shipped.Opened, target, null, null, path => carried?.GetValueOrDefault(path));
        }

        if (!file!.Exists)
        {
            var empty = Presets.Empty(plugins.Modules);

            return new AskedPatch(
                new Opened(
                    empty,
                    new SampleLibrary { Beside = file.DirectoryName },
                    new ImageLibrary { Beside = file.DirectoryName }),
                target,
                null,
                null,
                AskedPatch.Beside(file));
        }

        if (PatchFile.Bundled(file)) return Bundle(plugins, file, target, error);

        if (Patches.Open(file, error) is not { } opened) return null;

        var conversation = (store ?? new ConversationStore()).Find(file.FullName, File.ReadAllText(file.FullName));

        return new AskedPatch(opened, target, file, conversation, AskedPatch.Beside(file));
    }

    private static AskedPatch? Bundle(PluginCatalog plugins, FileInfo file, FileInfo target, TextWriter error)
    {
        LoadedBundle bundle;

        try
        {
            using var archive = File.OpenRead(file.FullName);

            bundle = PatchBundle.Read(archive, plugins.Modules);
        }
        catch (Exception ex)
        {
            error.WriteLine(AskedPatch.Complaint($"{file.Name}: {ex.Message}"));
            return null;
        }

        if (bundle.Load is { IsComplete: false } lacking)
        {
            error.WriteLine(AskedPatch.Complaint($"{file.Name}: {lacking.Summary}"));
            error.WriteLine(lacking.Detail);
            return null;
        }

        var files = BundleFiles.Of(bundle);

        return new AskedPatch(new Opened(bundle.Patch, files, files), target, file, bundle.Conversation, AskedPatch.Within(files, file));
    }

    /// <summary>The provider's saved settings with <paramref name="set"/> laid over them, or null having said which pair is not one.</summary>
    private static SettingValues? Values(SettingValues saved, IReadOnlyList<string> set, TextWriter error)
    {
        var values = saved;

        foreach (var pair in set)
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);

            if (equals <= 0)
            {
                error.WriteLine(AskedPatch.Complaint($"--set {pair}: write it as key=value, such as model=gpt-5."));
                return null;
            }

            values = values.With(pair[..equals].Trim(), pair[(equals + 1)..].Trim());
        }

        return values;
    }
}
