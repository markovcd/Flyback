using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Flyback.Editor.Assist;
using Flyback.Editor.Notices;
using Flyback.Editor.Windows;
using Flyback.Assist;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Secrets;
using Flyback.Plugins.Settings;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Assist;

/// <summary>
/// The one button on the instruction box, which sends a message and stops the run
/// it started.
/// </summary>
/// <remarks>
/// Mostly with no plugins, which is the state every machine is in until one is
/// installed. Where a provider is needed one is handed in — the half of this panel
/// that reacts to what a provider can do cannot be looked at in front of none.
/// Driving an actual run is the plugin tests' job.
/// </remarks>
public sealed partial class AssistantPanelTests : EditorTest
{
    /// <summary>
    /// Where a panel under test writes settings to, so pressing the real Save
    /// button in a test cannot land on the machine's own <c>assistant.json</c>.
    /// </summary>
    private readonly string settingsPath = Path.Combine(
        Path.GetTempPath(),
        "flyback-panel-settings-" + Guid.NewGuid().ToString("N"),
        "settings.json");

    private EditorFolders Kept => new() { SettingsPath = settingsPath };

    public override void Dispose()
    {
        base.Dispose();

        var folder = Path.GetDirectoryName(settingsPath);

        if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    /// <summary>
    /// The editor's window on Plasma with the assistant's column open. Only the
    /// settings it opens on, and a conversation where one is given, are the test's own.
    /// </summary>
    private MainWindow Showing(
        PluginCatalog? plugins = null,
        AssistantSettings? saved = null,
        string? logs = null,
        AssistantConversation? conversation = null)
    {
        var folders = Kept with { ConversationLogFolder = logs };

        return Column(Open(
            Presets.Plasma(NodeCatalog.BuiltIn),
            new EditorSetup { Plugins = plugins ?? PluginCatalog.Empty, Folders = folders },
            services =>
            {
                services.AddSingleton(new AssistantSettingRepository(folders, saved ?? new AssistantSettings()));
                if (conversation is not null) services.AddSingleton(conversation);
            }));
    }

    /// <summary>Opens the assistant's column in <paramref name="window"/>.</summary>
    private MainWindow Column(MainWindow window)
    {
        Service<Reactions>(window).Raise(new AssistantAsked(true));
        Settle(window);

        return window;
    }

    /// <summary>The assistant's column in <paramref name="window"/>.</summary>
    private static AssistantPanel PanelOf(Window window) => All<AssistantPanel>(window).Single();

    /// <summary>
    /// A catalog holding one provider and the shipped presets, as every scan
    /// does, which is the only way to see the half of this panel that reacts to
    /// what a provider can do.
    /// </summary>
    private static PluginCatalog With(IPatchAssistant assistant) =>
        new([], [], NodeCatalog.BuiltIn, [.. Presets.All], [], [assistant]);
}
