using System.CommandLine;
using System.CommandLine.Parsing;
using Flyback.Assist;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Core;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using PluginRegistry = Flyback.Cli.Plugins;

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
    /// <summary>Asks the assistant the editor is set to about a patch, and writes its answer back.</summary>
    public static Command Build(PluginRegistry plugins, Option<bool> json)
    {
        var patch = new Argument<FileInfo?>("patch")
        {
            Description = "The patch to talk about, written back with each answer: "
                + $"{AskedPatch.Formats}. One that does not exist yet starts empty.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var message = new Argument<string[]>("message")
        {
            Description = "What to ask. Left out, it is read from standard input, "
                + "or asked for line by line at a terminal. One that starts with a dash goes after --.",
            Arity = ArgumentArity.ZeroOrMore,
        };

        var preset = new Option<string>("--preset")
        {
            Description = "Start from a shipped preset, by name, in place of a file. Needs --out.",
        };

        var output = new Option<FileInfo>("--out", "-o")
        {
            Description = $"Write the answers here rather than over the patch: {AskedPatch.Formats}.",
        };

        var provider = new Option<string>("--provider")
        {
            Description = "Which assistant, by id. Defaults to whichever the settings are on.",
        };

        var model = new Option<string>("--model")
        {
            Description = $"The model, for this run only. Short for --set {AssistantSchema.ModelKey}=NAME.",
        };

        var set = new Option<string[]>("--set")
        {
            Description = "A provider setting for this run only, as key=value. Repeatable.",
            AllowMultipleArgumentsPerToken = false,
        };

        var fresh = new Option<bool>("--fresh")
        {
            Description = "Start a new conversation rather than carry on the one saved with the patch.",
        };

        var seen = new Option<DirectoryInfo>("--seen")
        {
            Description = "Write each picture it looks at and sound it hears into this folder.",
        };

        var briefing = new Option<bool>("--briefing")
        {
            Description = "Print the briefing the assistant is handed when a conversation starts.",
        };

        var context = new Option<int?>("--context")
        {
            Description = $"How many tokens a request may send before the conversation stops, {AssistantSettings.LeastContext} to "
                + $"{AssistantSettings.MostContext}. Defaults to the editor's Settings → Assistant.",
        };

        var expand = new Option<bool>("--expand")
        {
            Description = "Print the message written out in full, as the editor's Expand does, over the patch as a change "
                + "to it, or over an empty one as a new patch's brief. Builds and writes nothing.",
        };

        var command = new Command(
            "ask",
            "Ask the assistant to change a patch, the way the editor's assistant column does, and write "
            + "the patch back with the conversation, so the next ask, or the editor, carries it on.")
        {
            patch, message, preset, output, provider, model, set, fresh, seen, briefing, context, expand, json,
        };

        command.Validators.Add(result =>
        {
            if (result.GetValue(context) is { } limit and (< AssistantSettings.LeastContext or > AssistantSettings.MostContext))
                result.AddError($"--context is {AssistantSettings.LeastContext} to {AssistantSettings.MostContext}.");
        });

        command.SetAction((result, cancellation) =>
        {
            var error = result.InvocationConfiguration.Error;

            if (AskCommand.Stray(result, [.. result.GetResult(patch)?.Tokens ?? [], .. result.GetResult(message)?.Tokens ?? []]) is { } stray)
            {
                error.WriteLine(AskedPatch.Complaint(
                    $"ask has no {stray}; `ask --help` lists what it takes. A message that starts with a dash goes after --."));

                return Task.FromResult(Exit.Failed);
            }

            plugins.Ready();

            var file = result.GetValue(patch);
            var named = result.GetValue(preset);
            var words = result.GetValue(message) ?? [];

            // With --preset there is no patch, so the first word of the message
            // landed where the patch would have.
            if (named is not null && file is not null)
            {
                words = [result.GetResult(patch)!.Tokens[0].Value, .. words];
                file = null;
            }

            if (AskCommand.Open(plugins.Catalog, file, named, result.GetValue(output), error, writing: !result.GetValue(expand)) is not { } about)
                return Task.FromResult(Exit.Failed);

            var settings = (result.GetValue(set) ?? []).ToList();

            if (result.GetValue(model) is { } chosen) settings.Add($"{AssistantSchema.ModelKey}={chosen}");

            return AskCommand.Run(
                plugins.Catalog,
                about,
                new AskOptions(
                    words.Length == 0 ? null : string.Join(' ', words),
                    result.GetValue(provider),
                    settings,
                    result.GetValue(fresh),
                    result.GetValue(json),
                    result.GetValue(seen),
                    result.GetValue(briefing),
                    result.GetValue(context),
                    result.GetValue(expand)),
                result.InvocationConfiguration.Output,
                error,
                Console.In,
                Console.IsInputRedirected ? null : Console.In,
                cancellation);
        });

        return command;
    }

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

        if (options.Expand && message is null && console is not null)
        {
            error.WriteLine(AskedPatch.Complaint("--expand needs the message to write out: after the patch, or on standard input."));
            return Exit.Failed;
        }

        // Read before anything is started, so a pipe with nothing in it costs nothing.
        if (message is null && (console is null || options.Expand))
        {
            message = await input.ReadToEndAsync(cancel).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(message))
            {
                error.WriteLine(AskedPatch.Complaint("say what to ask: a message after the patch, or one on standard input."));
                return Exit.Failed;
            }
        }

        if (options.Expand)
        {
            return await AskExpansion
                .Run(assistant, config, settings, settingsPath ?? SettingsFile.Path, plugins.Modules, plugins.Presets, about, options, message!.Trim(), output, error, cancel)
                .ConfigureAwait(false);
        }

        using var conversation = new AskConversation(
            assistant,
            config,
            settings,
            settingsPath ?? SettingsFile.Path,
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
    /// The first of <paramref name="words"/> that reads as a flag <c>ask</c> does not have, or null.
    /// </summary>
    /// <remarks>
    /// The parser hands an unknown flag to the message. Words after <c>--</c> are the message
    /// whatever they look like, and a dash before a digit is a number.
    /// </remarks>
    public static string? Stray(ParseResult parsed, IEnumerable<Token> words)
    {
        var tokens = parsed.Tokens;
        var end = tokens.Count;

        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Type != TokenType.DoubleDash) continue;

            end = i;
            break;
        }

        foreach (var word in words)
        {
            var at = 0;

            while (at < end && !ReferenceEquals(tokens[at], word)) at++;

            if (at < end && Flag(word.Value)) return word.Value;
        }

        return null;

        static bool Flag(string word) =>
            word.StartsWith("--", StringComparison.Ordinal) || (word.Length > 1 && word[0] == '-' && !char.IsDigit(word[1]) && word[1] != '.');
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
        ConversationStore? store = null,
        bool writing = true)
    {
        if ((file is null) == (preset is null))
        {
            error.WriteLine(AskedPatch.Complaint("say what to ask about: a patch, or --preset and its name."));
            return null;
        }

        // Nothing is written when the message is only being written out, so a preset needs no --out.
        var target = into ?? file ?? (writing ? null : new FileInfo($"{preset}.{PatchIO.FileExtension}"));

        if (target is null)
        {
            error.WriteLine(AskedPatch.Complaint($"--out says where to write what it makes of the preset, {AskedPatch.Formats}."));
            return null;
        }

        if (writing && !AskedPatch.Writable(target))
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
