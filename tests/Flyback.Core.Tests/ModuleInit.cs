using System.Runtime.CompilerServices;

namespace Flyback.Core.Tests;

internal static class ModuleInit
{
    /// <summary>
    /// PNG snapshots are compared as decoded pixels, exactly: DeflateStream's bytes
    /// differ between runtimes built against different zlibs, and the picture does not.
    /// </summary>
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifierSettings.RegisterStreamComparer("png", PngPixelComparer.Compare);

        // Shader sources are approved as text, not as an opaque blob. Verify
        // assumes an unknown extension is binary, and a binary diff of GLSL is
        // exactly the thing those snapshots exist to avoid.
        EmptyFiles.FileExtensions.AddTextExtension("glsl");
    }
}
