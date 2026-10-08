using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Core;
using Flyback.Editor.Inspect;
using Flyback.Plugins.Assist;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Settings;

/// <summary>
/// The rows a settings page takes a key with: the box, where the key in force came from,
/// keep and forget. A key is never shown back (ADR-0034), so the note under the box is
/// the one place that says whether there is one.
/// </summary>
internal sealed class KeyRows
{
    private readonly Credentials credentials;

    private readonly StackPanel rows = new() { Spacing = 8 };

    private readonly TextBox keyBox = new()
    {
        PasswordChar = '•',
        FontSize = Text.Body,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private readonly TextBlock note = new()
    {
        FontSize = Text.Small,
        Foreground = Text.Muted,
        TextWrapping = TextWrapping.Wrap,

        // Under the box rather than under its caption, as a declared row's note is.
        Margin = new Thickness(InspectorRows.SettingsGutter, 0, 0, 0),
    };

    private readonly CheckBox keepBox = new() { Content = "Keep this key", FontSize = Text.Body };

    private readonly Button forget = new() { Content = "Forget key", FontSize = Text.Body };

    private string? account;

    private AssistantCredential? credential;

    /// <param name="name">What the key box is called, for finding it; its keep box is the same with <c>keep</c> before it.</param>
    public KeyRows(Credentials credentials, string? name = null)
    {
        this.credentials = credentials;

        if (name is not null)
        {
            keyBox.Name = name;
            keepBox.Name = "keep" + char.ToUpperInvariant(name[0]) + name[1..];
        }

        rows.Children.Add(InspectorRows.Field("API key", keyBox));
        rows.Children.Add(note);
        rows.Children.Add(keepBox);
        rows.Children.Add(forget);

        keyBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) Typed?.Invoke(this, EventArgs.Empty);
        };

        // Forgetting does not wait for Save: somebody who has just taken a key out is as
        // likely as not about to put another in.
        forget.Click += (_, _) =>
        {
            if (account is null) return;

            credentials.Forget(account);
            keyBox.Text = string.Empty;
            Forgotten?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Something was typed into the box, or it was emptied.</summary>
    public event EventHandler? Typed;

    /// <summary>The key entered here was forgotten.</summary>
    public event EventHandler? Forgotten;

    public Control View => rows;

    /// <summary>What is in the box, not yet taken.</summary>
    public string Entered => keyBox.Text ?? string.Empty;

    /// <summary>Whether a key taken is to be kept past this window.</summary>
    public bool Keep
    {
        get => keepBox.IsChecked == true;
        set => keepBox.IsChecked = value;
    }

    /// <summary>Empties the box without taking what was in it.</summary>
    public void Clear() => keyBox.Text = string.Empty;

    /// <summary>
    /// Shows the rows for the key filed under <paramref name="filedUnder"/>, or hides them
    /// where <paramref name="needs"/> is null.
    /// </summary>
    /// <param name="advice">Said after the credential's own help while there is no key.</param>
    public void Show(string? filedUnder, AssistantCredential? needs, string advice = "")
    {
        account = filedUnder;
        credential = needs;
        rows.IsVisible = filedUnder is not null && needs is not null;

        if (filedUnder is null || needs is null)
        {
            note.Text = string.Empty;
            forget.IsEnabled = false;
            return;
        }

        var variable = needs.EnvironmentVariable;
        var source = credentials.SourceOf(filedUnder, variable);

        keyBox.PlaceholderText = source switch
        {
            CredentialSource.Environment => $"A key is set, from {variable}",
            CredentialSource.Kept => "A key is set, and kept",
            CredentialSource.Session => "A key is set, for this window",
            _ => "Paste a key",
        };

        // What a key entered here stands on, since it is why "Forget key" leaves something behind.
        var falls = credentials.HasEntered(filedUnder) && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable))
            ? $" Forget it to go back to {variable}."
            : string.Empty;

        note.Text = source switch
        {
            CredentialSource.Session =>
                "In force, held for this window only and gone when it closes. It is never shown back "
                + "here — type a new one to replace it." + falls,

            CredentialSource.Kept =>
                $"In force, kept by {credentials.Store?.Name}. It is never shown back here — type a new "
                + "one to replace it." + falls,

            CredentialSource.Environment =>
                $"In force, from {variable}. {GlobalConstants.ApplicationName} never wrote it and never will. A key entered here "
                + "takes precedence over it, and forgetting that one comes back to this.",

            _ => needs.Help + advice,
        };

        // An environment variable is not this application's to remove.
        forget.IsEnabled = credentials.HasEntered(filedUnder);
    }

    /// <summary>
    /// Takes the key in the box for <paramref name="origin"/>, or keeps the one in hand where the
    /// keep box was ticked after it. What happened, to report, or null where nothing did.
    /// </summary>
    /// <param name="origin">Where the key goes, or null where the endpoint is no address yet.</param>
    public string? Take(string? origin)
    {
        if (account is null || credential is null) return null;

        var keep = Keep && credentials.CanKeep;

        if (!string.IsNullOrWhiteSpace(Entered))
        {
            // Left in the box: a key is kept for the address it goes to, and there is none yet.
            if (origin is null) return "Key not taken: the endpoint is not an address yet, and a key is kept for the one it goes to.";

            if (KeySafety.Refused(Entered) is { } refused) return $"Key not taken: {refused}";

            credentials.Accept(account, Entered, origin, keep);

            // Emptied once taken, so the secret is not held in a control for the life of the window.
            keyBox.Text = string.Empty;
            return Where(origin);
        }

        // The box ticked after the key was taken: asking for the secret again would be this
        // program's fault presented as the person's problem.
        if (keep && credentials.SourceOf(account, credential.EnvironmentVariable) == CredentialSource.Session)
        {
            credentials.KeepWhatIsHeld(account);
            return Where(origin);
        }

        return null;
    }

    /// <summary>Where the key went, read back rather than assumed: kept and held look the same until the next launch.</summary>
    private string Where(string? origin)
    {
        var said = credentials.SourceOf(account!, credential!.EnvironmentVariable) switch
        {
            CredentialSource.Kept => $"Key saved, and kept by {credentials.Store?.Name}.",
            _ when !credentials.CanKeep => "Key saved, for this window only — nothing installed can keep one.",
            _ when Keep => "Key saved, but it could not be kept — it will last this window only.",
            _ => "Key saved, for this window only.",
        };

        if (KeySafety.Cleartext(origin))
            said += $" It goes to {origin} over plain http, readable on the way: fine for a server that takes any value, not for a real key.";

        return said;
    }
}
