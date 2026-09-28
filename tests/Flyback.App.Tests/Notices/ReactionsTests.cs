using Flyback.App.Notices;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Notices;

/// <summary>
/// A notice reaches every reactor the container holds, in priority order, one at a
/// time, and a raiser that waits gets each reaction's task in turn (ADR-0148).
/// </summary>
public class ReactionsTests
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task A_reaction_that_waits_finishes_before_the_next_one_starts()
    {
        var log = new List<string>();
        var reactions = new Reactions();
        reactions.Add(new Slow(log, "one", 0));
        reactions.Add(new Counted(log, "two", 1));

        await reactions.RaiseAsync(new Ping());

        log.ShouldBe(["one in", "one out", "two"]);
    }

    [Fact]
    public void A_part_registered_as_a_part_reacts_to_what_it_declares()
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

    [Fact]
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

    [Fact]
    public void A_fault_that_finishes_at_once_reaches_the_raiser()
    {
        var reactions = new Reactions();
        reactions.Add(new Faulty());

        Should.Throw<InvalidOperationException>(() => reactions.Raise(new Ping()));
    }

    [Fact]
    public async Task A_raiser_that_waits_gets_the_fault()
    {
        var reactions = new Reactions();
        reactions.Add(new Faulty());

        await Should.ThrowAsync<InvalidOperationException>(() => reactions.RaiseAsync(new Ping()));
    }

    private sealed class Declaring(List<string> log) : IReactTo<Ping>
    {
        public Task On(Ping notice)
        {
            log.Add("declared");
            return Task.CompletedTask;
        }
    }
}
