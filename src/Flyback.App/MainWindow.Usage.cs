using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.App.Controls;
using Flyback.App.Statistics;

namespace Flyback.App;

public sealed partial class MainWindow
{
    /// <summary>The Usage section of the settings window: whether Flyback counts how it is used (ADR-0094).</summary>
    private readonly StackPanel usageSection = new() { Spacing = 10, Width = 280 };

    private readonly CheckBox sendUsageStatistics = new()
    {
        Name = "sendUsageStatistics",
        Content = "Count how Flyback is used",
        FontSize = Text.Body,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>What the Usage section was last saved as, and so what closing without Save puts it back to.</summary>
    private UsageSettings usageSettings = new();

    /// <summary>Where <see cref="usageSettings"/> is kept, or null to keep it nowhere.</summary>
    private readonly string? usageSettingsPath;

    /// <summary>
    /// What this run says about itself, which for every test and for a build with
    /// nowhere to send anything is nothing at all.
    /// </summary>
    private readonly Usage usage;

    /// <remarks>
    /// The whole of what is counted is written out here rather than summarised,
    /// because a count nobody asked for is only fair while the person can read what
    /// it is. It is the same list as ADR-0094's and ADR-0103's, in the order the events happen.
    /// </remarks>
    private void BuildUsageSection()
    {
        ToolTip.SetTip(sendUsageStatistics,
            "Send an anonymous count of what this run of Flyback is made of, so that what "
            + "is worth working on is known.");

        usageSection.Children.Add(sendUsageStatistics);

        usageSection.Children.Add(new TextBlock
        {
            Text = "What is counted: this version, the operating system, which of Flyback's own "
                + "plugins loaded and which sound backend opened; roughly how many cores, how much "
                + "memory and how tall a screen this machine has; whether this is its first start "
                + "or the first after an update; how many of each kind of module and how many "
                + "wires are in a patch when it plays, and which of Flyback's presets it came from; "
                + "whether an assistant was asked and which provider it was; and when the run "
                + "ends, roughly how long it lasted, how often things like recording, saving or "
                + "going full screen were done, and how fast the picture was drawn. If Flyback "
                + "crashes, the kind of error and where in Flyback it happened, without its "
                + "message. Nothing else — no patch, no file, no knob, nothing typed, and no name "
                + "of a plugin Flyback does not ship.",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
        });

        usageSection.Children.Add(new TextBlock
        {
            Text = "Nothing identifies you or this machine, so two runs cannot be told apart from "
                + "one, and there is nothing to ask to be forgotten. The counts go to Aptabase. "
                + "Clearing the box stops it straight away.",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
        });
    }

    /// <summary>
    /// How tall each screen is in pixels, for what a run started as to put in a band;
    /// empty where the platform will not say.
    /// </summary>
    private IReadOnlyList<int> ScreenHeights()
    {
        try
        {
            return Screens.All.Select(screen => screen.Bounds.Height).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private void ShowUsageSettings(UsageSettings settings) =>
        sendUsageStatistics.IsChecked = settings.SendUsageStatistics;

    /// <remarks>
    /// Switching it off takes effect now rather than at the next start: there is a
    /// run still going, and a box cleared has to mean this one too. Switching it on
    /// waits for the next start, because this run's own "started" has already gone
    /// unsaid and half a run is worse than none (ADR-0094).
    /// </remarks>
    private void SaveUsageSettings()
    {
        usageSettings = new UsageSettings { SendUsageStatistics = sendUsageStatistics.IsChecked == true };

        if (!usageSettings.SendUsageStatistics) usage.Stop();

        if (usageSettingsPath is null) return;

        try
        {
            usageSettings.Save(usageSettingsPath);
        }
        catch (Exception ex)
        {
            Report($"Could not save the usage settings: {ex.Message}", usageSettingsPath);
        }
    }
}
