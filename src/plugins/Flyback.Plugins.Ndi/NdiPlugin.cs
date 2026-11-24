namespace Flyback.Plugins.Ndi;

/// <summary>
/// Entry point of the NDI plugin: a picture output that sends each frame over the network.
/// </summary>
/// <remarks>
/// Loading this assembly calls nothing in NDI, so the plugin lists itself harmlessly on a
/// machine without the runtime, and its output says it cannot send there.
/// </remarks>
public sealed class NdiPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "ndi",
        "NDI",
        "Sends the picture over the network to VJ software such as Resolume, through the NDI runtime installed on the machine.");

    public void Register(IPluginRegistry registry) => registry.AddVideoOutput(new NdiVideoOutput());
}
