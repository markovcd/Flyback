using System.Collections.Immutable;
using Flyback.App.Controls;
using Flyback.App.Notices;
using Flyback.App.Statistics;

namespace Flyback.App.Settings;

/// <summary>Shows the settings sections together and commits or restores their drafts.</summary>
internal sealed class SettingsSession(
    IEnumerable<ISettingsSection> sections,
    IDialog dialog,
    Usage usage)
    : IReactTo<SettingsAsked>
{
    /// <summary>Every tab by name, in the order the window lists them.</summary>
    private static readonly ImmutableArray<string> Order = ["Picture", "Sound", "MIDI", "Recording", "Canvas", "Files", "Assistant", "Privacy"];

    /// <summary>The tabs, in <see cref="Order"/>.</summary>
    private readonly ImmutableArray<ISettingsSection> sections = [.. sections.OrderBy(Place)];

    /// <summary>Where a section's tab goes, refusing one <see cref="Order"/> does not list rather than putting it first.</summary>
    private static int Place(ISettingsSection section) =>
        Order.IndexOf(section.Name) is var place and >= 0
            ? place
            : throw new InvalidOperationException($"The settings section '{section.Name}' has no place in SettingsSession.Order.");

    /// <summary>Whether the settings sheet is waiting for an answer.</summary>
    public bool IsShowing { get; private set; }

    public Task On(SettingsAsked notice) => ShowAsync();

    /// <summary>Shows all sections, then saves their drafts or restores the last saved values.</summary>
    private async Task ShowAsync()
    {
        foreach (var section in sections) section.Opening();

        IsShowing = true;
        usage.Count(Used.Settings);

        bool saved;

        try
        {
            saved = await SettingsDialog.ShowAsync(dialog, [.. sections.Select(s => (s.Name, s.View))]);
        }
        finally
        {
            IsShowing = false;
        }

        if (saved)
        {
            foreach (var section in sections) section.Save();
            return;
        }

        foreach (var section in sections) section.Show();
    }
}
