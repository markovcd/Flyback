using Flyback.Core.Compile;
using static Flyback.Core.Graph.Sockets;

namespace Flyback.Core.Graph;

/// <summary>The Interval: how long one evaluation lasts, and whether the program remembers across them.</summary>
internal static class IntervalModule
{
    /// <summary>The type id of the Interval module.</summary>
    internal const string TypeId = "interval";

    /// <summary>
    /// What a filter, an envelope or a glide written by hand needs: the time between two
    /// evaluations, and the answer a module in C# gets from <see cref="Emitter.HasMemory"/>.
    /// </summary>
    internal static NodeDef Def() => new(
        TypeId, "Interval", ModuleCategories.Sources,
        [],
        [
            Num("seconds", 0f, 0f, 0.001f) with
            {
                Help = "The time since the evaluation before: one sample on the sound. Read it through 'remembers', since without memory it is the clock.",
            },
            Num("remembers", 0f, 0f, 1f) with
            {
                Help = "1 where the program remembers from one evaluation to the next: the sound, from its second sample on. 0 on the picture.",
            },
        ],
        (em, _) => [em.Interval(), em.HasMemory()],
        "How long one evaluation lasts, and whether the program remembers across them: what a "
        + "filter or a glide built from a cycle turns hertz and seconds with. Mix on 'remembers' "
        + "so the picture gets a wire rather than whatever the arithmetic leaves. Audio only.")
    {
        Words = "sample rate, sample period, delta time, memory",
        Sinks = ModuleSinks.Audio,
    };
}
