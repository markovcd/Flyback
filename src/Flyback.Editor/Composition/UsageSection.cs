using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Settings;
using Flyback.Editor.Shell;
using Flyback.Ui.Controls;
using Flyback.Editor.Statistics;

namespace Flyback.Editor.Composition;

/// <summary>The usage half of the settings window's Privacy section: whether Flyback counts how it is used (ADR-0094).</summary>
/// <remarks>
/// The whole of what is counted is written out here rather than summarized,
/// because a count is only fair to ask for while the person can read what it is. It is the same list as ADR-0094's and ADR-0103's, in the order the events happen.
/// </remarks>
internal sealed class UsageSection
{
    private readonly CheckBox countUsage = new()
    {
        Name = "countUsage",
        Content = "Count how Flyback is used",
        FontSize = Text.Body,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>What the section was last saved as, and so what closing without Save puts it back to.</summary>
    private UsageSettings saved = new();

    /// <summary>Where <see cref="saved"/> is kept, or null to keep it nowhere.</summary>
    private readonly string? path;

    private readonly Usage usage;

    private readonly Action<string, string?> report;

    /// <param name="usage">What this run says about itself, stopped the moment the box is cleared and saved.</param>
    public UsageSection(EditorFolders folders, Usage usage, ReportLine report)
    {
        path = folders.SettingsPath;
        this.usage = usage;
        this.report = (message, detail) => report.Say(message, detail);

        if (path is not null) saved = UsageSettings.Load(path);

        ToolTip.SetTip(countUsage,
            "Send an anonymous count of what this run of Flyback is made of, so that what "
            + "is worth working on is known.");

        View.Children.Add(countUsage);

        View.Children.Add(Muted(WhatIsCounted));
        View.Children.Add(Muted(Anonymous + " Clearing the box stops it straight away."));

        Show();
    }

    /// <summary>The whole of what is counted, for this section and the first-start question.</summary>
    internal const string WhatIsCounted =
        "What is counted: this version, the operating system, which of Flyback's own "
        + "plugins loaded and which sound backend opened; roughly how many cores, how much "
        + "memory and how tall a screen this machine has; whether this is its first start "
        + "or the first after an update; how many of each kind of module and how many "
        + "wires are in a patch when it plays, and which of Flyback's presets it came from; "
        + "whether an assistant was asked and which provider it was; and when the run "
        + "ends, how many of each kind of module were picked from the module list, "
        + "roughly how long it lasted, how often each of the editor's features, like "
        + "recording, undo, grouping or going full screen, was used, and how fast the "
        + "picture was drawn. If Flyback "
        + "crashes, the kind of error and where in Flyback it happened, without its "
        + "message. Nothing else — no patch, no file, no knob, nothing typed, and no name "
        + "of a plugin Flyback does not ship.";

    internal const string Anonymous =
        "Nothing identifies you or this machine, so two runs cannot be told apart from "
        + "one, and there is nothing to ask to be forgotten. The counts go to Aptabase.";

    internal static TextBlock Muted(string text) => new()
    {
        Text = text,
        FontSize = Text.Small,
        Foreground = Text.Muted,
        TextWrapping = TextWrapping.Wrap,
    };

    internal StackPanel View { get; } = new() { Spacing = 10, Width = SettingsSession.SectionWidth };

    /// <summary>Puts what was last saved back on the controls.</summary>
    internal void Show() => countUsage.IsChecked = saved.CountUsage;

    /// <remarks>
    /// Switching it off takes effect now rather than at the next start: there is a
    /// run still going, and a box cleared has to mean this one too. Switching it on
    /// waits for the next start, because this run's own "started" has already gone
    /// unsaid and half a run is worse than none (ADR-0094), unless the run is still
    /// holding its events for the first-start question.
    /// </remarks>
    internal void Save() =>
        Keep(new UsageSettings { CountUsage = countUsage.IsChecked == true, Asked = saved.Asked });

    /// <summary>The answer to the first-start question, kept as the box would keep it.</summary>
    internal void Answer(bool count)
    {
        Keep(new UsageSettings { CountUsage = count, Asked = true });
        Show();
    }

    private void Keep(UsageSettings settings)
    {
        saved = settings;

        usage.Answer(saved.CountUsage);

        if (path is null) return;

        try
        {
            saved.Save(path);
        }
        catch (Exception ex)
        {
            report($"Could not save the usage settings: {ex.Message}", path);
        }
    }
}
