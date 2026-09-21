using Flyback.App.Statistics;

namespace Flyback.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// What this run says about itself, which for every test and for a build with
    /// nowhere to send anything is nothing at all.
    /// </summary>
    private readonly Usage usage;

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
}
