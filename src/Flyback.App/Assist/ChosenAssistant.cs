using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;

namespace Flyback.App.Assist;

internal sealed class ChosenAssistant
{
    private readonly AssistantSettingRepository settings;
    private readonly PluginCatalog plugins;

    public ChosenAssistant(AssistantSettingRepository settings, PluginCatalog plugins)
    {
        this.settings = settings;
        this.plugins = plugins;
    }
    
    /// <summary>The assistant Ask sends to, or null where none is chosen.</summary>
    public IPatchAssistant? Value { get; private set; }
    
    /// <summary>
    /// The provider a saved id names, or none — never a different one picked on
    /// its behalf. A provider that no longer loads is not the same as a provider
    /// nobody has chosen yet, but silently switching to whatever else happens to
    /// be installed would answer both the same way, which is worse than falling
    /// back to the one choice that always means what it says.
    /// </summary>
    public void Load() => Value = settings.Current.Provider.Length > 0
        ? plugins.Assistant(settings.Current.Provider)
        : null;
    
    /// <summary>
    /// row 0 means no assistant
    /// </summary>
    /// <param name="row"></param>
    public void Choose(int row)
    {
        Value = row == 0 ? null : plugins.Assistants[row - 1];
    }

    /// <summary>Where Ask sends the patch. Never names a key.</summary>
    public string Summary => Value is null
        ? "No assistant is chosen, so nothing is sent anywhere."
        : $"Ask sends the patch and pictures of it to {Value.Name}.";
}