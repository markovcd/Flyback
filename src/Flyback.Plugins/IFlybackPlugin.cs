namespace Flyback.Plugins;

/// <summary>
/// The entry point of a plugin assembly. One public parameterless class per
/// assembly implementing this is what the host looks for.
/// </summary>
/// <remarks>
/// A plugin is constructed on the UI thread during startup, so
/// <see cref="Register"/> must be cheap and must not touch a device. Deciding
/// whether a backend can actually run belongs on the thing being registered —
/// see <see cref="Audio.IAudioOutput.IsSupported"/>.
/// </remarks>
public interface IFlybackPlugin
{
    /// <summary>Who wrote the plugin and what it is called.</summary>
    PluginInfo Info { get; }

    /// <summary>Adds what the plugin provides to <paramref name="registry"/>.</summary>
    void Register(IPluginRegistry registry);
}