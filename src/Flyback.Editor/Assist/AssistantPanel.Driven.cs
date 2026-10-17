using Avalonia.Controls;
using Flyback.Assist;
using Flyback.Plugins.Assist;

namespace Flyback.Editor.Assist;

/// <summary>The column while a program on this computer drives the editor (ADR-0202): its calls as steps, what it says as the reply, and no box to type in.</summary>
internal sealed partial class AssistantPanel
{
    /// <summary>The program driving the editor, or null.</summary>
    private string? driver;

    /// <summary>The message box and the bar it sits in, hidden while a program drives.</summary>
    private Control composing = null!;

    /// <summary>Whether the last thing the program did was say something, so the next thing it says starts a paragraph.</summary>
    private bool lastSaid;

    private void Follow(DrivenEditor driven)
    {
        driven.Held = () => asking ? "the assistant in Flyback's own column is in the middle of a turn; call again once it finishes." : null;
        driven.Driving += Driven;
        driven.Happened += Shown;
        driven.Released += Undriven;
    }

    private void Driven(string program)
    {
        driver = program;
        lastSaid = false;
        composing.IsVisible = false;
        transcript.Put(Voice.Note, $"{program} is driving the editor. Its calls show here, and each edit undoes on the canvas.");
        ShowSendState();
        ShowSpent();
    }

    private void Shown(PatchEvent happened)
    {
        if (happened is PatchEvent.Said said && lastSaid) happened = new PatchEvent.Said("\n\n" + said.Text);

        lastSaid = happened is PatchEvent.Said;
        transcript.Put(Spoken.Of(happened));
        ShowSendState();
    }

    private void Undriven(string program)
    {
        driver = null;
        composing.IsVisible = true;
        transcript.Put(Voice.Note, $"{program} stopped driving the editor.");
        ShowSendState();
        ShowSpent();
    }

    /// <summary>The header's pill while a program drives.</summary>
    private void ShowDriven()
    {
        beacon.Opacity = 1;
        beacon.Fill = Live;
        progress.Text = "Driven";
        progress.Foreground = LiveInk;
        status.Background = LiveGround;
        ToolTip.SetTip(status, $"{driver} is editing the open patch through flyback-cli mcp. Close it there to type here again.");
    }
}
