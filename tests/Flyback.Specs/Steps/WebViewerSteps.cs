using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Reqnroll;
using Reqnroll.UnitTestProvider;
using Shouldly;
using Flyback.App;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Hosting;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>
/// The web viewer's build, run under Node by its own <c>hear.mjs</c>, against the
/// same preset rendered here the way the desktop renders it.
/// </summary>
[Binding]
public sealed class WebViewerSteps(Session session, IUnitTestRuntimeProvider runtime) : IDisposable
{
    private static readonly Lazy<PluginCatalog> Installed = new(PluginHost.Load);

    /// <summary>The size the viewer opens a patch at, which a Scan hears as its aspect.</summary>
    private const int Width = 960, Height = 540;

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-web-");
    private float[] heard = [];
    private double seconds;

    [When("it plays in the web viewer for {float} second(s)")]
    public void WhenPlayedInTheBrowser(float length)
    {
        var node = Node();
        if (node is null) runtime.TestIgnore("no Node on this machine to run the web viewer with.");

        var build = typeof(WebViewerSteps).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "WebViewer").Value!;

        var output = Path.Combine(folder.FullName, "heard.f32");
        var start = new ProcessStartInfo(node!, [
            Path.Combine(build, "hear.mjs"),
            "--preset", session.Presets.Single().Name,
            "--seconds", length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--size", $"{Width}x{Height}",
            "--out", output,
        ])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = build,
        };

        using var process = Process.Start(start)!;
        var said = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.ShouldBe(0, said.Result);

        heard = MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(output)).ToArray();
        seconds = length;
    }

    /// <summary>
    /// One step of 16 bits is the most a sample may be off by: the maths library
    /// under a browser rounds its last bit where the desktop's may not, and a WAV
    /// cannot hear a difference smaller than that.
    /// </summary>
    [Then("its sound is the desktop's to within one step of 16 bits")]
    public void ThenTheDesktopsSound()
    {
        var modules = Installed.Value.Modules;
        var (patch, samples, pictures) = PresetLibrary.Open(session.Presets.Single(), null, modules);

        var program = patch.CompileForAudio(modules, samples: samples, pictures: pictures, played: true).Program;
        var live = new LiveValues(program.LiveInputs);
        patch.Seed(live);

        var speakers = new AudioRenderer { Aspect = SynthRenderer.AspectOf(Width, Height) };
        var memory = speakers.DelayMemoryFor(program);
        var desktop = new float[heard.Length];

        // In the viewer's buffers, since the knobs and keys are read once a buffer.
        for (var at = 0; at < desktop.Length; at += 2048)
            speakers.Render(program, desktop.AsSpan(at, Math.Min(2048, desktop.Length - at)), memory, live);

        heard.Length.ShouldBe((int)Math.Round(seconds * speakers.SampleRate) * 2);
        desktop.ShouldContain(sample => sample != 0f, "the desktop heard silence, so agreeing with it proves nothing");

        var worst = desktop.Zip(heard, (a, b) => Math.Abs(a - b)).Max();
        worst.ShouldBeLessThanOrEqualTo(1f / 32768f);
    }

    /// <summary>Node on the path, or the one the WebAssembly workload brings with it.</summary>
    private static string? Node()
    {
        var name = OperatingSystem.IsWindows() ? "node.exe" : "node";

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (directory.Length > 0 && File.Exists(Path.Combine(directory, name)))
                return Path.Combine(directory, name);

        var packs = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "packs"));
        if (!Directory.Exists(packs)) return null;

        return Directory.EnumerateDirectories(packs, "Microsoft.NET.Runtime.Emscripten.*.Node.*")
            .SelectMany(pack => Directory.EnumerateFiles(pack, name, SearchOption.AllDirectories))
            .FirstOrDefault();
    }

    public void Dispose() => folder.Delete(recursive: true);
}
