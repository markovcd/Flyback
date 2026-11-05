using System.Reflection;
using Flyback.Engine.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// What the engine makes the JIT inline whatever its own judgment says, which it then
/// copies into every method the IL backend emits that calls it.
/// </summary>
public class ForcedInliningTests
{
    /// <summary>
    /// The JIT's own ceiling for an inline candidate, in bytes of IL. A larger body forced
    /// inline is pasted into every op that calls it, and the build after an edit pays to
    /// optimize each copy: a forced <see cref="DelayState.Advance"/> made Plasma's picture
    /// take twice as long to build, for no faster frame.
    /// </summary>
    private const int LargestInlined = 100;

    [Fact]
    public void Nothing_the_emitted_code_calls_is_forced_inline_past_the_JITs_own_ceiling()
    {
        var oversized =
            from type in typeof(IlOps).Assembly.GetTypes()
            where type.Namespace == typeof(IlOps).Namespace
            from method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            where method.MethodImplementationFlags.HasFlag(MethodImplAttributes.AggressiveInlining)
            let size = method.GetMethodBody()?.GetILAsByteArray()?.Length ?? 0
            where size > LargestInlined
            select $"{type.Name}.{method.Name} ({size} bytes)";

        oversized.ShouldBeEmpty();
    }
}
