using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Assist;
using Flyback.Core;
using Flyback.Editor.Inspect;
using Flyback.Editor.Settings;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Assist;

/// <summary>
/// The Assistant page of the settings window: who is being talked to, what that
/// one has to be told, the key, and the rest. Its fields exist from the start,
/// since what is on them says whether a message can be sent at all.
/// </summary>
internal sealed class AssistantSettingsPage
{
    private readonly ChosenAssistant chosenAssistant;
    private readonly PluginCatalog plugins;
    private readonly Credentials credentials;
    private readonly AssistantSettingRepository settingsRepository;
    private readonly IAssistantEditor editor;

    /// <summary>Where the settings are written, for saying so when they cannot be.</summary>
    private readonly string? settingsPath;

    /// <summary>Something on the page moved that the panel shows: the provider, the form, or the key.</summary>
    public event EventHandler? Changed;

    private readonly TextBox keyBox = new()
    {
        PasswordChar = '•',
        FontSize = Text.Body,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// What is under the key field, since the field itself cannot say it. A key
    /// that is set is never read back into the box (ADR-0034), and blank is also
    /// what "no key" looks like — so without this the one screen somebody opens to
    /// check cannot answer the question.
    /// </summary>
    private readonly TextBlock keyNote = new()
    {
        FontSize = Text.Small,
        Foreground = Text.Muted,
        TextWrapping = TextWrapping.Wrap,

        // Under the box rather than under its caption, as a declared row's note is.
        Margin = new Thickness(InspectorRows.SettingsGutter, 0, 0, 0),
    };

    private readonly Button forget = new() { Content = "Forget key", FontSize = Text.Body };
    private readonly CheckBox rememberBox = new() { Content = "Keep this key", FontSize = Text.Body };

    /// <summary>
    /// The key label, box, note, "keep" box and "forget" button, together —
    /// every row here only means something in relation to a provider, so with
    /// none picked there is nothing for any of them to say.
    /// </summary>
    private readonly StackPanel keySection = new() { Spacing = 8 };

    /// <summary>The probe button and everything under it.</summary>
    private readonly ProbeSection probeSection;

    /// <summary>
    /// Whether to keep a file of what gets sent and said. Off by default, since
    /// that is a second copy of everything a turn already sends somewhere else —
    /// see <see cref="AssistantSettings.LogConversations"/>.
    /// </summary>
    private readonly CheckBox logBox = new() { Content = "Log conversations to disk", FontSize = Text.Body };

    /// <summary>Whether the briefing is shown — see <see cref="AssistantSettings.ShowBriefing"/>.</summary>
    private readonly CheckBox briefingBox = new()
    {
        Name = "showBriefing",
        Content = "Show the briefing it is handed",
        FontSize = Text.Body,
    };

    /// <summary>Whether what it looks up is shown — see <see cref="AssistantSettings.ShowLookups"/>.</summary>
    private readonly CheckBox lookupsBox = new()
    {
        Name = "showLookups",
        Content = "Show the handbook text it looks up",
        FontSize = Text.Body,
    };

    /// <summary>
    /// How many tokens a request may send — see
    /// <see cref="AssistantSettings.ContextLimit"/>. Stepped in tens of thousands,
    /// and a value typed with a fraction is rounded rather than refused.
    /// </summary>
    private readonly NumericUpDown contextBox = new()
    {
        Name = "contextLimit",
        Minimum = AssistantSettings.LeastContext,
        Maximum = AssistantSettings.MostContext,
        Increment = 10_000,
        FormatString = "0",
        FontSize = Text.Body,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// How long the briefing may run before module descriptions are left out of
    /// it — see <see cref="AssistantSettings.ProseBudget"/>. Stepped in tens of
    /// thousands, since a few characters either way changes nothing.
    /// </summary>
    private readonly NumericUpDown proseBox = new()
    {
        Name = "proseBudget",
        Minimum = AssistantSettings.LeastProse,
        Maximum = AssistantSettings.MostProse,
        Increment = 10_000,
        FormatString = "0",
        FontSize = Text.Body,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// Everything the chosen provider says it has, drawn from its own declaration.
    /// This panel does not know what is on it: which model, which endpoint,
    /// whether there is an ear at all are the provider's questions (ADR-0069), and
    /// what arrives here is a bag of strings to hand back.
    /// </summary>
    private readonly SettingsForm form = new() { Beside = true };

    private readonly ComboBox providerBox = new() { FontSize = Text.Body, HorizontalAlignment = HorizontalAlignment.Stretch, Name = "provider" };

    /// <summary>
    /// The row that means no provider at all. First in the box and first among
    /// equals: picking it is a choice in its own right, not what is left when
    /// nothing else is, so it sits beside the real ones rather than replacing an
    /// empty selection.
    /// </summary>
    private const string NoProvider = "None";

    /// <summary>Built the first time the settings window asks for it, and kept.</summary>
    private Control? section;

    public AssistantSettingsPage(
        ChosenAssistant chosenAssistant,
        PluginCatalog plugins,
        Credentials credentials,
        AssistantSettingRepository settingsRepository,
        IAssistantEditor editor,
        EditorFolders? folders = null)
    {
        this.chosenAssistant = chosenAssistant;
        this.plugins = plugins;
        this.credentials = credentials;
        this.settingsRepository = settingsRepository;
        this.editor = editor;
        settingsPath = folders?.SettingsPath;

        // The choice is the page's to show from the start, so it is read before the box is.
        chosenAssistant.Load();

        probeSection = new ProbeSection(() => chosenAssistant.Value, KeyOnTheForm, form, () => Changed?.Invoke(this, EventArgs.Empty));

        // Filled in here rather than where the window is put together. That is
        // built the first time somebody opens it, and what is set on the form is
        // what says whether a message can be sent at all, which the panel's footer
        // has to answer from the moment it exists.
        rememberBox.IsChecked = settingsRepository.Current.RememberKey;
        logBox.IsChecked = settingsRepository.Current.LogConversations;
        briefingBox.IsChecked = settingsRepository.Current.ShowBriefing;
        lookupsBox.IsChecked = settingsRepository.Current.ShowLookups;
        contextBox.Value = settingsRepository.Current.ContextLimit;
        proseBox.Value = settingsRepository.Current.ProseBudget;

        // The list before what is chosen in it, and both before the handler that
        // watches it: a box with no rows in it cannot be told which row to show,
        // and a selection made now is a restoration rather than a choice.
        providerBox.ItemsSource = new[] { NoProvider }.Concat(plugins.Assistants.Select(a => a.Name)).ToList();

        ShowProviderForm();

        form.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The page, kept and lent out rather than built afresh, because these are
    /// fields with handlers already on them.
    /// </summary>
    /// <remarks>
    /// The credential state is read again every time it is asked for: a key can
    /// arrive or leave without this page touching anything, and this is the one
    /// screen that claims to say which. What provider was in force is noted on the
    /// way out, so a window closed without Save can be put back to it — see
    /// <see cref="Discard"/>.
    /// </remarks>
    public Control Section()
    {
        chosenAssistant.Load();
        section ??= Build();

        // Taken back from whoever last borrowed it, rather than left to them to
        // return. A control has one parent and a closed window still holds the
        // one it was showing, so without this the settings would open exactly
        // once a session and throw on the second try.
        switch (section.Parent)
        {
            case ContentControl lender: lender.Content = null; break;
            case Panel lender: lender.Children.Remove(section); break;
        }

        return section;
    }

    /// <summary>
    /// The window's contents: who is being talked to, what that one has to be
    /// told, and the key — in that order, because the middle changes entirely with
    /// the first. The provider and the key are the two the host owns; everything
    /// between them is the provider's own form.
    /// </summary>
    private Control Build()
    {
        providerBox.SelectionChanged += (_, _) =>
        {
            // Row 0 is always "None"; a real provider is one row below its own
            // place in plugins.Assistants because of it.
            var row = providerBox.SelectedIndex;
            if (row < 0 || row > plugins.Assistants.Count) return;

            chosenAssistant.Choose(row);
            
            // What the last provider was set to is kept rather than carried
            // over. A setting means whatever the provider that declared it says
            // it means, and the two need not agree about anything but the name.
            ShowProviderForm();
            Changed?.Invoke(this, EventArgs.Empty);
        };

        // No padding of its own: it is one section of the settings window, which
        // pads the whole of it.
        var fields = new StackPanel { Spacing = 8, Width = SettingsSession.SectionWidth };

        keySection.Children.Add(InspectorRows.Field("API key", keyBox));
        keySection.Children.Add(keyNote);
        keySection.Children.Add(rememberBox);
        keySection.Children.Add(forget);

        fields.Children.Add(InspectorRows.Field("Provider", providerBox));

        // The probe goes on what is on the form, so a key typed and not yet
        // saved is a key it can use — and the button has to notice it arrive.
        keyBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) probeSection.ShowState();
        };

        fields.Children.Add(form);
        fields.Children.Add(keySection);
        fields.Children.Add(probeSection);
        ToolTip.SetTip(contextBox, "How many tokens a conversation may grow to before it stops.");
        fields.Children.Add(InspectorRows.Field("Context", contextBox));
        fields.Children.Add(logBox);
        fields.Children.Add(briefingBox);
        fields.Children.Add(lookupsBox);
        fields.Children.Add(InspectorRows.Field("Briefing", proseBox));
        fields.Children.Add(Note(
            "How many characters the briefing may run to. Past this, the modules on the priority list keep their descriptions, the rest keep "
            + "theirs while there is room, and the ones left out are marked on the canvas. The "
            + "list is a file, one type id a line, read again whenever settings are saved:"));

        // Selectable, so the path can be copied into a file manager or an editor.
        fields.Children.Add(new SelectableTextBlock
        {
            Name = "priorityFile",
            Text = settingsRepository.PriorityFile,
            FontSize = Text.Small,
            TextWrapping = TextWrapping.Wrap,
        });

        // Forgetting a key does not close the window the way Save does: somebody
        // who has just taken one out is as likely as not about to put another in.
        forget.Click += (_, _) =>
        {
            if (chosenAssistant.Value is null) return;

            credentials.Forget(chosenAssistant.Value.Id);
            keyBox.Text = string.Empty;
            Changed?.Invoke(this, EventArgs.Empty);
        };

        return fields;

        static TextBlock Note(string text) => new()
        {
            Text = text,
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
        };
    }
    
    /// <summary>
    /// Puts back whatever was in force when the settings were opened, for a window
    /// closed some way other than Save.
    /// </summary>
    /// <remarks>
    /// Only the provider needs restoring by hand: picking one sets
    /// <see cref="AssistantSettings.Provider"/> immediately, so the form under it
    /// can change with it. Everything else is not written until
    /// <see cref="Save"/> runs, or was never kept at all — a key typed into
    /// <see cref="keyBox"/> only has to be blanked (ADR-0034).
    /// </remarks>
    public void Discard()
    {
        probeSection.Stop();

        chosenAssistant.Load();

        keyBox.Text = string.Empty;
        rememberBox.IsChecked = settingsRepository.Current.RememberKey;
        logBox.IsChecked = settingsRepository.Current.LogConversations;
        briefingBox.IsChecked = settingsRepository.Current.ShowBriefing;
        lookupsBox.IsChecked = settingsRepository.Current.ShowLookups;
        contextBox.Value = settingsRepository.Current.ContextLimit;
        proseBox.Value = settingsRepository.Current.ProseBudget;

        ShowProviderForm();
    }

    /// <summary>
    /// Puts the chosen provider's form up, holding what that provider was last set
    /// to. The provider box is set from here too, so the two cannot disagree about
    /// who is being configured; nothing else is, since the fields are asked for
    /// again after every answer.
    /// </summary>
    private void ShowProviderForm()
    {
        providerBox.SelectedIndex = chosenAssistant.Value is null
            ? 0
            : plugins.Assistants
                .Select((a, i) => (a, i))
                .Where(pair => pair.a.Id == chosenAssistant.Value.Id)
                .Select(pair => pair.i + 1)
                .DefaultIfEmpty(0)
                .First();

        form.Show(
            chosenAssistant.Value is null ? null : chosenAssistant.Value.Form, 
            settingsRepository.Current.Of(chosenAssistant.Value?.Id ?? string.Empty));
    }

    /// <summary>Makes what is on the page the settings', and writes them out.</summary>
    public void Save()
    {
        // What a probe still running would have found is not on the form yet, so
        // it is not what is being saved. It ends here rather than outliving the
        // window it was started from.
        probeSection.Stop();

        settingsRepository.Current.RememberKey = rememberBox.IsChecked == true;
        settingsRepository.Current.LogConversations = logBox.IsChecked == true;
        settingsRepository.Current.ShowBriefing = briefingBox.IsChecked == true;
        settingsRepository.Current.ShowLookups = lookupsBox.IsChecked == true;

        // An emptied box keeps what was saved rather than becoming nought.
        if (contextBox.Value is { } context)
            settingsRepository.Current.ContextLimit = Math.Clamp((int)Math.Round(context), AssistantSettings.LeastContext, AssistantSettings.MostContext);

        contextBox.Value = settingsRepository.Current.ContextLimit;

        if (proseBox.Value is { } prose)
            settingsRepository.Current.ProseBudget = Math.Clamp((int)Math.Round(prose), AssistantSettings.LeastProse, AssistantSettings.MostProse);

        proseBox.Value = settingsRepository.Current.ProseBudget;

        settingsRepository.Current.Provider = chosenAssistant.Value?.Id ?? string.Empty;

        
        if (chosenAssistant.Value is not null)
        {
            settingsRepository.Current.Provider = chosenAssistant.Value.Id;
            settingsRepository.Current.Remember(chosenAssistant.Value.Id, form.Values);

            var keep = settingsRepository.Current.RememberKey && credentials.CanKeep;

            var origin = KeyedTransport.OriginOf(chosenAssistant.Value, form.Values);

            if (!string.IsNullOrWhiteSpace(keyBox.Text) && origin is null)
            {
                // Left in the box: a key is kept for the address it goes to, and there is none yet.
                editor.Report("Key not taken: the endpoint is not an address yet, and a key is kept for the one it goes to.", null);
            }
            else if (KeySafety.Refused(keyBox.Text) is { } refused)
            {
                editor.Report($"Key not taken: {refused}", null);
            }
            else if (!string.IsNullOrWhiteSpace(keyBox.Text))
            {
                credentials.Accept(chosenAssistant.Value.Id, keyBox.Text, origin!, keep);

                // Emptied once it has been taken. Left there it would hold the
                // secret in a control for the life of the window, and the line
                // that says where the key actually lives could never appear.
                keyBox.Text = string.Empty;
                SayWhereTheKeyWent();
            }
            else if (keep && credentials.SourceOf(chosenAssistant.Value.Id, chosenAssistant.Value.Credential.EnvironmentVariable) == CredentialSource.Session)
            {
                // Ticking the box after the fact, with nothing typed. The key is
                // already in hand and the field is empty because this emptied
                // it, so asking for the secret again would be this program's
                // fault presented as the person's problem. Session rather than
                // HasEntered: a key already Kept from an earlier save has
                // nothing left to do here, and saying so again on every later
                // Save would announce a change that did not happen.
                credentials.KeepWhatIsHeld(chosenAssistant.Value.Id);
                SayWhereTheKeyWent();
            }
        }

        try
        {
            settingsRepository.Save();
        }
        catch (Exception ex)
        {
            editor.Report($"Could not save the assistant settings: {ex.Message}", settingsPath);
        }
    }

    /// <summary>
    /// The provider, its form as it stands, and what it sends over — which is all a
    /// configuration is. Nothing is interpreted on the way past: what a set of
    /// answers means is the provider's to work out, on the other side of this call.
    /// </summary>
    public AssistantConfig? Configured() =>
        chosenAssistant.Value is { } assistant
            ? new AssistantConfig(credentials.Transport(assistant, form.Values), form.Values)
            : null;

    /// <summary>
    /// Says whether there is a key without ever showing one.
    /// </summary>
    /// <remarks>
    /// The three sources differ in what they promise and in what can be done
    /// about them, so each is named rather than reduced to a tick: one from the
    /// environment cannot be removed from here at all, and saying "forget it"
    /// under a button that would not is worse than saying nothing.
    /// </remarks>
    public void ShowState()
    {
        probeSection.ShowState();
        keySection.IsVisible = chosenAssistant.Value is { NeedsKey: true };

        if (chosenAssistant.Value is null)
        {
            keyNote.Text = string.Empty;
            forget.IsEnabled = false;
            return;
        }

        var variable = chosenAssistant.Value.Credential.EnvironmentVariable;
        var source = credentials.SourceOf(chosenAssistant.Value.Id, variable);

        keyBox.PlaceholderText = source switch
        {
            CredentialSource.Environment => $"A key is set, from {variable}",
            CredentialSource.Kept => "A key is set, and kept",
            CredentialSource.Session => "A key is set, for this window",
            _ => "Paste a key",
        };

        var overruled = credentials.HasEntered(chosenAssistant.Value.Id)
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable));

        // What a key entered here is standing on top of, since it is the reason
        // "Forget key" does something other than leave nothing behind.
        var falls = overruled ? $" Forget it to go back to {variable}." : string.Empty;

        keyNote.Text = source switch
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

            _ => chosenAssistant.Value.Credential.Help + " Make one for Flyback alone, with a spending limit.",
        };

        // Nothing to forget, or nothing this could reach if it tried: an
        // environment variable is not this application's to remove.
        forget.IsEnabled = credentials.HasEntered(chosenAssistant.Value.Id);
    }

    /// <summary>
    /// The key the probe would go with: the one in the box before the one in
    /// hand, since a key typed here is not saved until Save and may under
    /// ADR-0034 never be written down at all.
    /// </summary>
    private IAssistantTransport? KeyOnTheForm()
    {
        if (chosenAssistant.Value is not { } assistant) return null;

        var transport = string.IsNullOrWhiteSpace(keyBox.Text)
            ? credentials.Transport(assistant, form.Values)
            : new KeyedTransport(keyBox.Text, KeyedTransport.OriginOf(assistant, form.Values), assistant.Credential);

        return transport.HasKey ? transport : null;
    }

    /// <summary>
    /// Which of the two happened, read back rather than assumed. ADR-0034's own
    /// warning is that "held for this run" and "saved" look identical until the
    /// next launch, and a program that appeared to save something and did not is
    /// worse than one that never offered.
    /// </summary>
    private void SayWhereTheKeyWent()
    {
        if (chosenAssistant.Value is null) return;

        var source = credentials.SourceOf(chosenAssistant.Value.Id, chosenAssistant.Value.Credential.EnvironmentVariable);
        var origin = credentials.Transport(chosenAssistant.Value, form.Values).Origin;

        var said = source switch
        {
            CredentialSource.Kept => $"Key saved, and kept by {credentials.Store?.Name}.",
            _ when !credentials.CanKeep =>
                "Key saved, for this window only — nothing installed can keep one.",
            _ when settingsRepository.Current.RememberKey =>
                "Key saved, but it could not be kept — it will last this window only.",
            _ => "Key saved, for this window only.",
        };

        if (KeySafety.Cleartext(origin))
            said += $" It goes to {origin} over plain http, readable on the way: fine for a server that takes any value, not for a real key.";

        editor.Report(said, null);
    }
}
