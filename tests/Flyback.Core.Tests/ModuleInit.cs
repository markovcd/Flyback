using System.Runtime.CompilerServices;

namespace Flyback.Core.Tests;

internal static class ModuleInit
{
    /// <summary>
    /// Snapshots are compared as decoded pixels, not as file bytes. Two things
    /// make byte comparison the wrong choice here: PngWriter compresses through
    /// DeflateStream, whose output may change across runtime versions, and MathF
    /// results can differ slightly across architectures. Neither is a behaviour
    /// change, and neither should fail a test.
    /// </summary>
    [ModuleInitializer]
    public static void Initialize() => VerifyImageMagick.Initialize();
}
