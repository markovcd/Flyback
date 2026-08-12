namespace Flyback.Plugins;

/// <summary>
/// What a plugin says about itself. <paramref name="Id"/> is stable and
/// machine-readable; it is what a setting or a log line names.
/// </summary>
public sealed record PluginInfo(string Id, string Name, string Description = "");

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
    PluginInfo Info { get; }

    void Register(IPluginRegistry registry);
}

/// <summary>
/// What a plugin may contribute. New kinds of extension are new methods here;
/// existing plugins only ever call this interface, so adding one does not
/// invalidate them.
/// </summary>
public interface IPluginRegistry
{
    /// <summary>Offers a sound backend. Registering it does not open a device.</summary>
    void AddAudioOutput(Audio.IAudioOutput output);
}
