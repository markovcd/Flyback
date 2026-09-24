using Avalonia.Headless.XUnit;
using Flyback.App.Controls;
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

        // Not disposed: the window tears down what it holds when it closes.
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        var window = Owned(provider.GetRequiredService<MainWindow>());

        foreach (var registered in services.Where(d => !d.IsKeyedService && !d.ServiceType.IsGenericTypeDefinition))
            Should.NotThrow(() => provider.GetService(registered.ServiceType), registered.ServiceType.Name);

        window.CloseWithoutAsking();
    }

    [AvaloniaFact]
    public void The_regions_are_in_the_window_the_container_built()
    {
        var provider = new ServiceCollection().AddEditor(new EditorSetup()).BuildServiceProvider();

        var window = Owned(provider.GetRequiredService<MainWindow>());
        window.Show();
        Settle(window);

        provider.GetRequiredService<Shell>().Owner.ShouldBeSameAs(window);
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
