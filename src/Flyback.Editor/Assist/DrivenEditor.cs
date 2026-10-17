using Avalonia.Threading;
using Flyback.Editor.Notices;
using Flyback.Editor.Shell;
using Flyback.Host.Driving;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;

namespace Flyback.Editor.Assist;

/// <summary>
/// Opens the editor's channel while "Let a program on this computer edit the open patch" is
/// on, and runs what comes over it on the canvas (ADR-0202).
/// </summary>
/// <remarks>Its events are raised on the UI thread, for the assistant's column to show.</remarks>
internal sealed class DrivenEditor(
    EditorFolders folders,
    IAssistantEditor editor,
    PluginCatalog plugins,
    AssistantSettingRepository settings) : IStartAt, IDisposable
{
    private ChannelServer? server;

    public StartPhase Phase => StartPhase.Opened;

    /// <summary>The program driving the editor, by the name it gave, or null.</summary>
    public string? Client { get; private set; }

    /// <summary>A program began driving the editor, by the name it gave.</summary>
    public event Action<string>? Driving;

    /// <summary>What a call did, or what the program said.</summary>
    public event Action<PatchEvent>? Happened;

    /// <summary>The program stopped driving the editor.</summary>
    public event Action<string>? Released;

    /// <summary>Why a call cannot run now, or null when it can; asked before each one.</summary>
    public Func<string?> Held { get; set; } = () => null;

    /// <summary>Whether this platform can host the channel: a desktop, not a phone or a page.</summary>
    public static bool Possible => !OperatingSystem.IsBrowser() && !OperatingSystem.IsAndroid();

    /// <summary>The pipe or socket listened on.</summary>
    internal string Channel { get; set; } = EditorChannel.Name;

    public Task On()
    {
        if (Possible && folders.SettingsPath is { } path && DrivenSettings.Load(path).LetProgramsEdit) Open();

        return Task.CompletedTask;
    }

    /// <summary>Opens the channel or closes it, as the setting now says.</summary>
    public void Allow(bool on)
    {
        if (on && Possible) Open();
        else Close();
    }

    /// <summary>Stops listening and lets any program driving the editor go.</summary>
    private void Close()
    {
        server?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        server = null;
    }

    private void Open()
    {
        if (server is not null) return;

        var bench = new DrivenBench(
            editor,
            plugins.Modules,
            settings.GetProsePolicy,
            () => editor.Presets() ?? plugins.Presets);

        server = new ChannelServer(Channel, (request, cancel) => Dispatcher.UIThread.InvokeAsync(() => Answer(bench, request, cancel)));
        server.Connected += client => Dispatcher.UIThread.Post(() =>
        {
            Client = client;
            Driving?.Invoke(client);
        });
        server.Disconnected += () => Dispatcher.UIThread.Post(() =>
        {
            if (Client is not { } client) return;

            Client = null;
            Released?.Invoke(client);
        });
        server.Start();
    }

    private async Task<ChannelReply> Answer(DrivenBench bench, ChannelRequest request, CancellationToken cancel)
    {
        if (request.Say is { } said)
        {
            editor.Report(said, null);
            Happened?.Invoke(new PatchEvent.Said(said));
            return new ChannelReply(request.Id, true, "said.");
        }

        if (request.Tool is not { } tool) return new ChannelReply(request.Id, false, "a request names a tool, or says something.");

        if (request.Arguments is not { ValueKind: System.Text.Json.JsonValueKind.Object } arguments)
            return new ChannelReply(request.Id, false, "a tool's arguments are a JSON object.");

        if (Held() is { } why) return new ChannelReply(request.Id, false, why);

        var outcome = await bench.Call(tool, arguments, cancel);

        Happened?.Invoke(Event(outcome));

        return new ChannelReply(request.Id, outcome.Ok, outcome.Text, outcome.Png is { } png ? Convert.ToBase64String(png) : null);
    }

    /// <summary>A call's outcome as a plugin turn's would read in the column.</summary>
    private static PatchEvent Event(ToolOutcome outcome) => outcome switch
    {
        { Png: { } png } => new PatchEvent.Saw(png, outcome.Text),
        { Wav: { } wav } => new PatchEvent.Heard(wav, outcome.Text),
        { Reference: true } => new PatchEvent.Read(outcome.Text),
        _ => new PatchEvent.Did(outcome.Text),
    };

    /// <summary>Stops listening. Synchronous, since the editor's container is disposed with its window.</summary>
    public void Dispose() => Close();
}
