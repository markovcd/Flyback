using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Controls;
using Flyback.Editor.Hubs;
using Flyback.Editor.Notices;
using Flyback.Editor.Shell;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Files;

/// <summary>
/// Asks whether to start Flyback again now, for a sound device that plays at another rate.
/// Only that is found once Save has been pressed, too late for the settings window's own offer.
/// </summary>
internal sealed class RestartQuestion(EditorHost host, IDialog dialog, PatchFiles files, Reactions reactions)
    : IReactTo<DeviceRateDiffers>
{
    public const string Title = "Restart Flyback?";

    public async Task On(DeviceRateDiffers notice)
    {
        if (host.Relaunch is null) return;

        if (await dialog.Show<bool>(Title, answer => View(files.Reopen is not null, answer)))
            await reactions.RaiseAsync(new RestartAsked(null, SameDocument: true));
    }

    /// <param name="reopens">Whether the patch on screen is one a restart can open again.</param>
    internal static Control View(bool reopens, Action<bool> answer)
    {
        var page = new StackPanel { Width = 420, Margin = new Thickness(20, 12, 20, 0) };

        page.Children.Add(new TextBlock
        {
            Text = "That device plays at another rate, so Flyback has to start again to use it."
                + (reopens ? " The patch you have open opens again." : ""),
            FontSize = Text.Body,
            TextWrapping = TextWrapping.Wrap,
        });

        var restart = new Button { Name = "restartNow", Content = "Restart now", MinWidth = 96, Classes = { "accent" } };
        var later = new Button { Name = "later", Content = "Later", MinWidth = 96 };

        restart.Click += (_, _) => answer(true);
        later.Click += (_, _) => answer(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(20, 16, 20, 20),
            Children = { restart, later },
        };

        return new DialogBody(page, buttons) { Name = "restartQuestion" };
    }
}
