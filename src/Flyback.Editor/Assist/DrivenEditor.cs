using Avalonia.Threading;
using Flyback.Editor.Notices;
using Flyback.Editor.Shell;
using Flyback.Host.Driving;
using Flyback.Plugins.Hosting;

namespace Flyback.Editor.Assist;

/// <summary>
/// Opens the editor's channel while "Let a program on this computer edit the open patch" is
/// on, and runs what comes over it on the canvas (ADR-0202).
/// </summary>
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

    public Task On()
    {
        if (folders.SettingsPath is { } path && DrivenSettings.Load(path).LetProgramsEdit) Open(EditorChannel.Name);

        return Task.CompletedTask;
    }

    /// <summary>Listens on <paramref name="name"/>.</summary>
    internal void Open(string name)
    {
        if (server is not null) return;

        var bench = new DrivenBench(
            editor,
            plugins.Modules,
            settings.GetProsePolicy,
            () => editor.Presets() ?? plugins.Presets);

        server = new ChannelServer(name, (request, cancel) => Dispatcher.UIThread.InvokeAsync(() => Answer(bench, request, cancel)));
        server.Connected += client => Dispatcher.UIThread.Post(() => Client = client);
        server.Disconnected += () => Dispatcher.UIThread.Post(() => Client = null);
        server.Start();
    }

    private async Task<ChannelReply> Answer(DrivenBench bench, ChannelRequest request, CancellationToken cancel)
    {
        if (request.Say is { } said)
        {
            editor.Report(said, null);
            return new ChannelReply(request.Id, true, "said.");
        }

        if (request.Tool is not { } tool) return new ChannelReply(request.Id, false, "a request names a tool, or says something.");

        if (request.Arguments is not { ValueKind: System.Text.Json.JsonValueKind.Object } arguments)
            return new ChannelReply(request.Id, false, "a tool's arguments are a JSON object.");

        var outcome = await bench.Call(tool, arguments, cancel);

        return new ChannelReply(request.Id, outcome.Ok, outcome.Text, outcome.Png is { } png ? Convert.ToBase64String(png) : null);
    }

    /// <summary>Stops listening. Synchronous, since the editor's container is disposed with its window.</summary>
    public void Dispose() => server?.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
