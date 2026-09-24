using Flyback.App.Controls;

namespace Flyback.App;

/// <summary>Writing to the author, which the status bar's last glyph opens (ADR-0136).</summary>
public sealed partial class MainWindow
{
    private async Task WriteToTheAuthorAsync()
    {
        if (presetSite is not { } root)
        {
            Report("There is nowhere to send a letter: this copy has no site.");
            return;
        }

        var http = SiteHttp ?? SiteClient.Value;

        // Built once and both shown and sent, so what was read is what goes.
        var about = SiteLetters.About(plugins, playback.Sound);

        var said = await LetterView.AskAsync(
            this,
            about,
            (mood, message, contact, cancel) => SiteLetters.SendAsync(http, root, mood, message, contact, about, cancel));

        if (said is not null) Report(said);
    }
}
