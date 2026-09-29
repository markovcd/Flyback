using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Flyback.Core.Tests.Compile;

/// <summary>Node, for running what <c>JsEmitter</c> writes: the one on the path, or the one the WebAssembly workload brings.</summary>
internal static class NodeJs
{
    public static string? Path { get; } = Find();

    private static string? Find()
    {
        var name = OperatingSystem.IsWindows() ? "node.exe" : "node";

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator))
            if (directory.Length > 0 && File.Exists(System.IO.Path.Combine(directory, name)))
                return System.IO.Path.Combine(directory, name);

        var packs = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "packs"));

        if (!Directory.Exists(packs)) return null;

        return Directory.EnumerateDirectories(packs, "Microsoft.NET.Runtime.Emscripten.*.Node.*")
            .SelectMany(pack => Directory.EnumerateFiles(pack, name, SearchOption.AllDirectories))
            .FirstOrDefault();
    }

    /// <summary>Runs <paramref name="script"/> with <paramref name="arguments"/>, and throws with what it said if it fails.</summary>
    public static void Run(string script, params string[] arguments)
    {
        var start = new ProcessStartInfo(Path!, [script, .. arguments])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(start)!;
        var said = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0) throw new InvalidOperationException($"node failed: {said.Result}");
    }
}
