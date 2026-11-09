using Flyback.Assist;

namespace Flyback.Editor.Assist;

internal sealed class ChosenAssistant
{
    private readonly AssistantSettingRepository settings;

    public ChosenAssistant(AssistantSettingRepository settings, Assistants offered)
    {
        this.settings = settings;
        Offered = offered;
    }

    /// <summary>Every assistant this window offers.</summary>
    public Assistants Offered { get; }

    /// <summary>The assistant Ask sends to, or null where none is chosen.</summary>
    public IPatchAssistant? Value { get; private set; }

    /// <summary>
    /// The assistant a saved id names, or none — never a different one picked on
    /// its behalf. An id this window does not offer is not the same as one nobody
    /// has chosen yet, but silently switching to another would answer both the same
    /// way, which is worse than falling back to the one choice that always means
    /// what it says.
    /// </summary>
    public void Load() => Value = settings.Current.Provider.Length > 0
        ? Offered.Find(settings.Current.Provider)
        : null;

    /// <summary>Chooses the assistant in <paramref name="row"/> of the settings' list, whose row 0 is none.</summary>
    public void Choose(int row)
    {
        Value = row == 0 ? null : Offered.All[row - 1];
    }

    /// <summary>Where Ask sends the patch. Never names a key.</summary>
    public string Summary => Value is null
        ? "No assistant is chosen, so nothing is sent anywhere."
        : $"Ask sends the patch and pictures of it to {Value.Name}.";
}
