using Flyback.Editor.Notices;

namespace Flyback.Editor.Settings;

/// <summary>
/// Puts what every settings section last saved in force as the editor is built: the
/// preview needs its resolution and its backend whether or not anybody looks at them.
/// </summary>
internal sealed class SettingsStart(IEnumerable<ISettingsSection> sections) : IStartAt
{
    public StartPhase Phase => StartPhase.Built;

    public Task On()
    {
        foreach (var section in sections) section.Start();

        return Task.CompletedTask;
    }
}
