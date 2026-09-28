using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Flyback.App.Notices;

/// <summary>
/// Hands each notice to the parts that react to it, in <see cref="IReactTo{T}.Priority"/>
/// order (ADR-0148).
/// </summary>
/// <remarks>
/// The reactors are looked up in the container the first time their notice is raised,
/// never while the container is being built, which is what keeps a reaction from
/// being a constructor dependency. So nothing raises a notice from a constructor:
/// a part that has something to say at start says it from <c>Start</c>.
/// </remarks>
/// <param name="provider">The container the reactors are registered in, or null for one that holds only what <see cref="Add{T}(IReactTo{T})"/> gives it.</param>
internal sealed class Reactions(IServiceProvider? provider = null)
{
    private readonly ConcurrentDictionary<Type, object> ordered = new();
    private readonly List<(Type Notice, object Reactor)> added = [];
    private readonly Lock gate = new();

    /// <summary>Runs every reactor to <paramref name="notice"/> in turn, waiting for each before the next.</summary>
    public async Task RaiseAsync<T>(T notice) where T : notnull
    {
        foreach (var reactor in Reactors<T>()) await reactor.On(notice);
    }

    /// <summary>
    /// The same, for a raiser that cannot wait. Whatever finishes at once has finished
    /// when this returns; a fault after that is thrown on the UI thread, as an unhandled
    /// one out of an event handler is.
    /// </summary>
    public void Raise<T>(T notice) where T : notnull
    {
        var run = RaiseAsync(notice);

        if (run.IsCompleted)
        {
            run.GetAwaiter().GetResult();
            return;
        }

        run.ContinueWith(
            faulted => Dispatcher.UIThread.Post(() => ExceptionDispatchInfo.Capture(faulted.Exception!.GetBaseException()).Throw()),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// A reactor that does not live for the window, such as a test's or a window opened
    /// later, until what this returns is disposed.
    /// </summary>
    public IDisposable Add<T>(IReactTo<T> reactor) where T : notnull
    {
        lock (gate) added.Add((typeof(T), reactor));

        ordered.TryRemove(typeof(T), out _);

        return new Added(() =>
        {
            lock (gate) added.Remove((typeof(T), reactor));

            ordered.TryRemove(typeof(T), out _);
        });
    }

    /// <inheritdoc cref="Add{T}(IReactTo{T})"/>
    public IDisposable Add<T>(Func<T, Task> on, int priority = 0) where T : notnull => Add(new Reaction<T>(on, priority));

    /// <inheritdoc cref="Add{T}(IReactTo{T})"/>
    public IDisposable Add<T>(Action<T> on, int priority = 0) where T : notnull => Add<T>(
        notice =>
        {
            on(notice);
            return Task.CompletedTask;
        },
        priority);

    private IReactTo<T>[] Reactors<T>() where T : notnull =>
        (IReactTo<T>[])ordered.GetOrAdd(typeof(T), _ => Sorted<T>());

    private IReactTo<T>[] Sorted<T>() where T : notnull
    {
        IEnumerable<IReactTo<T>> registered = provider?.GetServices<IReactTo<T>>() ?? [];
        List<IReactTo<T>> late;

        lock (gate) late = added.Where(a => a.Notice == typeof(T)).Select(a => (IReactTo<T>)a.Reactor).ToList();

        return registered.Concat(late).OrderBy(reactor => reactor.Priority).ToArray();
    }

    private sealed class Reaction<T>(Func<T, Task> on, int priority) : IReactTo<T> where T : notnull
    {
        public int Priority => priority;

        public Task On(T notice) => on(notice);
    }

    private sealed class Added(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }
}
