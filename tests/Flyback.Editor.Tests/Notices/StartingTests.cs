using Avalonia.Headless.XUnit;
using Flyback.Editor.Notices;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Flyback.Editor.Tests.Notices;

/// <summary>
/// The editor starts in three phases, each part at the one it declares, in the order
/// the parts were registered; a part that starts may raise a notice.
/// </summary>
public class StartingTests : EditorTest
{
    private sealed record Ping : INotice;

    private sealed class Part(List<string> log, string name, StartPhase phase) : IStartAt
    {
        public StartPhase Phase => phase;

        public Task On()
        {
            log.Add(name);
            return Task.CompletedTask;
        }
    }

    [AvaloniaFact]
    public async Task A_phase_runs_its_own_parts_in_order_and_only_once()
    {
        List<string> log = [];
        var starting = new Starting(
        [
            new Part(log, "settings", StartPhase.Built),
            new Part(log, "layout", StartPhase.Shown),
            new Part(log, "dialog", StartPhase.Opened),
            new Part(log, "title", StartPhase.Shown),
        ]);

        await starting.RunAsync(StartPhase.Shown);
        await starting.RunAsync(StartPhase.Shown);

        log.ShouldBe(["layout", "title"]);

        starting.Run(StartPhase.Built);

        log.ShouldBe(["layout", "title", "settings"]);
    }

    [AvaloniaFact]
    public void What_starts_once_the_editor_is_built_may_raise_a_notice()
    {
        List<string> log = [];

        using var provider = EditorServices.Provider(replace: services =>
            services.AddSingleton<IStartAt>(sp => new Raising(sp.GetRequiredService<Reactions>())));
        using var heard = provider.GetRequiredService<Reactions>().Add<Ping>(_ => log.Add("heard"));

        provider.View();

        log.ShouldBe(["heard"]);
    }

    private sealed class Raising(Reactions reactions) : IStartAt
    {
        public StartPhase Phase => StartPhase.Built;

        public Task On()
        {
            reactions.Raise(new Ping());
            return Task.CompletedTask;
        }
    }
}
