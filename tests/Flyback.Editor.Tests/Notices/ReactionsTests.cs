using Avalonia.Headless.XUnit;
using Flyback.Editor.Tests.Ui;
using Flyback.Editor.Notices;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Flyback.Editor.Tests.Notices;

/// <summary>
/// A notice reaches every reactor the container holds, in priority order, one at a
/// time, and a raiser that waits gets each reaction's task in turn (ADR-0148). What
/// would make a reaction flaky is refused outright: a notice raised while the editor
/// is being built, one raised off the UI thread, and one raised after the window is gone.
/// </summary>
public class ReactionsTests : UiTest
{
    private sealed record Ping;

    private sealed class Counted(List<string> log, string name, int priority) : IReactTo<Ping>
    {
        public int Priority => priority;

        public Task On(Ping notice)
        {
            log.Add(name);
            return Task.CompletedTask;
        }
    }

    private sealed class Slow(List<string> log, string name, int priority) : IReactTo<Ping>
    {
        public int Priority => priority;

        public async Task On(Ping notice)
        {
            log.Add(name + " in");
            await Task.Yield();
            log.Add(name + " out");
        }
    }

    private sealed class Faulty : IReactTo<Ping>
    {
        public Task On(Ping notice) => throw new InvalidOperationException("no");
    }

    private sealed class Declaring(List<string> log) : IReactTo<Ping>
    {
        public Task On(Ping notice)
        {
            log.Add("declared");
            return Task.CompletedTask;
        }
    }

    /// <summary>A part that says something as it is made, which is the one thing a part may not do.</summary>
    private sealed class Talkative
    {
        public Talkative(Reactions reactions) => reactions.Raise(new Ping());
    }

    [AvaloniaFact]
    public void Reactors_run_lowest_priority_first_whatever_order_they_were_registered_in()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<Reactions>();
        services.AddSingleton<IReactTo<Ping>>(new Counted(log, "last", 10));
        services.AddSingleton<IReactTo<Ping>>(new Counted(log, "first", -10));
        services.AddSingleton<IReactTo<Ping>>(new Counted(log, "middle", 0));

        services.BuildServiceProvider().GetRequiredService<Reactions>().Raise(new Ping());

        log.ShouldBe(["first", "middle", "last"]);
    }

    [AvaloniaFact]
    public void Ties_keep_the_order_they_were_registered_in()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<Reactions>();
        services.AddSingleton<IReactTo<Ping>>(new Counted(log, "a", 0));
        services.AddSingleton<IReactTo<Ping>>(new Counted(log, "b", 0));

        services.BuildServiceProvider().GetRequiredService<Reactions>().Raise(new Ping());

        log.ShouldBe(["a", "b"]);
    }

    [AvaloniaFact]
    public async Task A_reaction_that_waits_finishes_before_the_next_one_starts()
    {
        var log = new List<string>();
        var reactions = new Reactions();
        reactions.Add(new Slow(log, "one", 0));
        reactions.Add(new Counted(log, "two", 1));

        await reactions.RaiseAsync(new Ping());

        log.ShouldBe(["one in", "one out", "two"]);
    }

    [AvaloniaFact]
    public void A_part_reacts_to_what_it_declares()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<Reactions>();
        services.AddSingleton(log);
        services.AddPart<Declaring>();

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<Reactions>().Raise(new Ping());

        log.ShouldBe(["declared"]);
        provider.GetRequiredService<IReactTo<Ping>>().ShouldBeSameAs(provider.GetRequiredService<Declaring>());
    }

    [AvaloniaFact]
    public void A_reactor_added_later_is_heard_until_it_is_taken_away()
    {
        var log = new List<string>();
        var reactions = new Reactions();

        var added = reactions.Add<Ping>(_ => log.Add("heard"));
        reactions.Raise(new Ping());
        added.Dispose();
        reactions.Raise(new Ping());

        log.ShouldBe(["heard"]);
    }

    [AvaloniaFact]
    public void A_fault_that_finishes_at_once_reaches_the_raiser()
    {
        var reactions = new Reactions();
        reactions.Add(new Faulty());

        Should.Throw<InvalidOperationException>(() => reactions.Raise(new Ping()));
    }

    [AvaloniaFact]
    public async Task A_raiser_that_waits_gets_the_fault()
    {
        var reactions = new Reactions();
        reactions.Add(new Faulty());

        await Should.ThrowAsync<InvalidOperationException>(() => reactions.RaiseAsync(new Ping()));
    }

    [AvaloniaFact]
    public void Every_declared_reactor_is_built_with_the_window()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<Reactions>();
        services.AddSingleton(log);
        services.AddPart<Declaring>();
        var provider = services.BuildServiceProvider();
        var built = false;

        provider.GetRequiredService<Reactions>().Building(() => built = true);

        built.ShouldBeTrue();
        provider.GetService<IReactTo<Ping>>().ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void A_notice_raised_while_the_editor_is_being_built_is_refused()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Reactions>();
        services.AddPart<Talkative>();
        var provider = services.BuildServiceProvider();

        Should.Throw<InvalidOperationException>(() => provider.GetRequiredService<Reactions>().Building(provider.GetRequiredService<Talkative>))
            .Message.ShouldContain("Ping");
    }

    [AvaloniaFact]
    public async Task A_notice_raised_off_the_UI_thread_is_refused()
    {
        var reactions = new Reactions();
        reactions.Add<Ping>(_ => { });

        await Should.ThrowAsync<InvalidOperationException>(() => Task.Run(() => reactions.Raise(new Ping())));
    }

    [AvaloniaFact]
    public void A_notice_raised_after_the_window_is_gone_goes_nowhere()
    {
        var log = new List<string>();
        var reactions = new Reactions();
        reactions.Add<Ping>(_ => log.Add("heard"));

        reactions.Dispose();
        reactions.Raise(new Ping());

        log.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task A_chain_still_running_when_the_window_goes_stops_there()
    {
        var log = new List<string>();
        var reactions = new Reactions();
        reactions.Add(new Slow(log, "one", 0));
        reactions.Add<Ping>(_ => reactions.Dispose(), 1);
        reactions.Add(new Counted(log, "three", 2));

        await reactions.RaiseAsync(new Ping());

        log.ShouldBe(["one in", "one out"]);
    }
}
