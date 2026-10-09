namespace Flyback.Specs.Support;

/// <summary>A folder put first on the process's path until disposed, by one scenario at a time.</summary>
/// <remarks>
/// The path is one for the whole process and features run in parallel, so a second scenario
/// waits for the first to put it back rather than restoring a copy taken while the first held it.
/// </remarks>
internal sealed class ScenarioPath : IDisposable
{
    private static readonly SemaphoreSlim Held = new(1, 1);
    private static readonly TimeSpan Cap = TimeSpan.FromMinutes(1);

    private readonly string? before;
    private bool disposed;

    public ScenarioPath(string folder)
    {
        if (!Held.Wait(Cap))
            throw new TimeoutException($"Another scenario has held the path for {Cap.TotalSeconds:0} s.");

        before = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", folder + Path.PathSeparator + before);
    }

    public void Dispose()
    {
        if (disposed) return;

        disposed = true;
        Environment.SetEnvironmentVariable("PATH", before);
        Held.Release();
    }
}
