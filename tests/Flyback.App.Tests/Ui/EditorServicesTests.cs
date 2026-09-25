using Avalonia.Headless.XUnit;
using Flyback.App.Audio;
using Flyback.App.Controls;
using Flyback.Core;
using Flyback.Plugins.Audio;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The container the editor's window is composed in (ADR-0150): everything it
/// registers can be built, one window's services are one of each, and a test can
/// swap any of them for its own.
/// </summary>
public class EditorServicesTests : UiTest
{
    [AvaloniaFact]
    public void Every_service_the_editor_registers_can_be_built()
    {
        var services = new ServiceCollection().AddEditor(new EditorSetup());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        var window = Owned(provider.GetRequiredService<MainWindow>());

        foreach (var registered in services.Where(d => !d.IsKeyedService && !d.ServiceType.IsGenericTypeDefinition))
            Should.NotThrow(() => provider.GetService(registered.ServiceType), registered.ServiceType.Name);

        window.CloseWithoutAsking();
    }

    [AvaloniaFact]
    public void Closing_the_window_lets_its_sound_device_go()
    {
        var device = new Speakers();
        var window = NewMainWindow(replace: services => services.AddSingleton(new AudioSetup(device)));
        window.Show();
        Settle(window);

        window.CloseWithoutAsking();

        device.Disposed.ShouldBeTrue();
    }

    /// <summary>A sound card that remembers being let go.</summary>
    private sealed class Speakers : IAudioDevice
    {
        public bool Disposed { get; private set; }

        public int SampleRate => GlobalConstants.SampleRate;

        public bool IsRunning { get; private set; }

        public void Start(AudioCallback fill) => IsRunning = true;

        public void Stop() => IsRunning = false;

        public void Dispose() => Disposed = true;
    }

    [AvaloniaFact]
    public void A_service_that_asks_for_the_window_while_it_is_built_is_refused()
    {
        var thrown = Should.Throw<Exception>(() => NewMainWindow(replace: services =>
            services.AddSingleton<IWindowFocus>(sp => new Impatient(sp.GetRequiredService<EditorWindow>()))));

        thrown.ToString().ShouldContain("asked for while it was being built");
    }

    /// <summary>Reads the window in its constructor, which is the mistake the guard is there for.</summary>
    private sealed class Impatient : IWindowFocus
    {
        public Impatient(EditorWindow window) => IsActive = window.Value.IsActive;

        public bool IsActive { get; }
    }

    [AvaloniaFact]
    public void The_regions_are_in_the_window_the_container_built()
    {
        using var provider = new ServiceCollection().AddEditor(new EditorSetup()).BuildServiceProvider();

        var window = Owned(provider.GetRequiredService<MainWindow>());
        window.Show();
        Settle(window);

        provider.GetRequiredService<EditorWindow>().Value.ShouldBeSameAs(window);
        All<NodeEditor>(window).Single().ShouldBeSameAs(provider.GetRequiredService<NodeEditor>());
    }

    [AvaloniaFact]
    public void A_canvas_is_built_from_its_services_alone()
    {
        var provider = new ServiceCollection().AddCanvas().BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        var editor = provider.GetRequiredService<NodeEditor>();

        editor.History.ShouldBeSameAs(provider.GetRequiredService<CanvasHistory>());
        editor.Selection.ShouldBeSameAs(provider.GetRequiredService<CanvasSelection>());
        editor.Edits.ShouldBeSameAs(provider.GetRequiredService<CanvasEdits>());
    }

    [AvaloniaFact]
    public void A_service_registered_again_takes_the_place_of_the_editors_own()
    {
        var mine = new CanvasReport();

        var canvas = NewCanvas(400, 300, services => services.AddSingleton(mine));

        canvas.Report.ShouldBeSameAs(mine);
    }
}
