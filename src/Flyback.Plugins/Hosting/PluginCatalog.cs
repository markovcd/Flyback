using Flyback.Plugins.Audio;

namespace Flyback.Plugins.Hosting;

/// <summary>A plugin that loaded, and where it came from.</summary>
public sealed record LoadedPlugin(PluginInfo Info, string AssemblyPath);

/// <summary>
/// Something that went wrong with one plugin. Collected rather than thrown: a
/// broken plugin must not stop the program starting, and the person who has to
/// fix it needs to be told which file it was.
/// </summary>
public sealed record PluginProblem(string Source, string Message)
{
    public override string ToString() => $"{Source}: {Message}";
}

/// <summary>Everything one scan of the plugin directory found.</summary>
public sealed class PluginCatalog
{
    public static PluginCatalog Empty { get; } = new([], [], []);

    internal PluginCatalog(
        IReadOnlyList<LoadedPlugin> plugins,
        IReadOnlyList<IAudioOutput> audioOutputs,
        IReadOnlyList<PluginProblem> problems)
    {
        Plugins = plugins;
        AudioOutputs = audioOutputs;
        Problems = problems;
    }

    public IReadOnlyList<LoadedPlugin> Plugins { get; }

    public IReadOnlyList<IAudioOutput> AudioOutputs { get; }

    public IReadOnlyList<PluginProblem> Problems { get; }

    /// <summary>
    /// The backend to use here: supported, highest priority, ties broken on id
    /// so the choice is the same on every run. Null when nothing can play.
    /// </summary>
    public IAudioOutput? PreferredAudioOutput => AudioOutputs
        .Where(o => Supported(o))
        .OrderByDescending(o => o.Priority)
        .ThenBy(o => o.Id, StringComparer.Ordinal)
        .FirstOrDefault();

    /// <summary>
    /// A backend that throws while answering whether it is supported has
    /// answered no. Nothing a plugin does should be able to take the shell down.
    /// </summary>
    private static bool Supported(IAudioOutput output)
    {
        try
        {
            return output.IsSupported;
        }
        catch
        {
            return false;
        }
    }
}
