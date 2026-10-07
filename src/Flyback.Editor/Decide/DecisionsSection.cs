using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Controls;
using Flyback.Editor.Inspect;
using Flyback.Editor.Settings;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Decide;

/// <summary>
/// The Decisions section of the settings window: which decision model the editor asks,
/// how it is set, its key, its download, and a box to try it with.
/// </summary>
internal sealed class DecisionsSection : ISettingsSection
{
    /// <summary>The named client downloads go over.</summary>
    public const string Client = "decision-models";

    /// <summary>What the try box asks about what is typed in it: the question the assistant is routed by.</summary>
    public const string TryQuestion = "Does this ask for a change to the patch?";

    private const string NoModel = "None";

    private readonly Decisions decisions;
    private readonly DecisionSettingRepository settings;
    private readonly IHttpClientFactory clients;
    private readonly ReportLine report;

    private readonly StackPanel rows = new() { Spacing = 8, Width = SettingsSession.SectionWidth };

    private readonly ComboBox modelBox = new Picker { Name = "decisionModel", FontSize = Text.Body, HorizontalAlignment = HorizontalAlignment.Stretch };

    private readonly SettingsForm form = new() { Beside = true };

    private readonly TextBox keyBox = new() { Name = "decisionKey", PasswordChar = '•', FontSize = Text.Body, HorizontalAlignment = HorizontalAlignment.Stretch };

    private readonly CheckBox keepBox = new() { Name = "keepDecisionKey", Content = "Keep this key", FontSize = Text.Body };

    private readonly Button forget = new() { Content = "Forget key", FontSize = Text.Body };

    private readonly TextBlock keyNote = Note();

    private readonly StackPanel keyRows = new() { Spacing = 8 };

    private readonly TextBlock status = Note("decisionStatus");

    private readonly Button download = new() { Name = "downloadModel", Content = "Download", FontSize = Text.Body };

    private readonly TextBox tryBox = new() { Name = "tryDecision", FontSize = Text.Body, PlaceholderText = "make the bass slower", HorizontalAlignment = HorizontalAlignment.Stretch };

    private readonly Button tryButton = new() { Name = "askDecision", Content = "Ask", FontSize = Text.Body };

    private readonly TextBlock tried = Note("decisionAnswer");

    private CancellationTokenSource? downloading;

    /// <summary>
    /// Where the picker stood when the saved choice was put on it. A choice nobody made is
    /// saved only once somebody moves the picker, so a model installed later can still be the default.
    /// </summary>
    private int shown;

    public DecisionsSection(Decisions decisions, DecisionSettingRepository settings, IHttpClientFactory clients, ReportLine report)
    {
        this.decisions = decisions;
        this.settings = settings;
        this.clients = clients;
        this.report = report;

        rows.Children.Add(Note(text:
            "A decision model answers small questions about words with a probability: whether a message asks for an edit, "
            + "which module a phrase means. Whatever asks works as it did without one."));
        rows.Children.Add(InspectorRows.Field("Model", modelBox));
        rows.Children.Add(form);

        keyRows.Children.Add(InspectorRows.Field("API key", keyBox));
        keyRows.Children.Add(keyNote);
        keyRows.Children.Add(keepBox);
        keyRows.Children.Add(forget);
        rows.Children.Add(keyRows);

        rows.Children.Add(status);
        rows.Children.Add(download);

        var trying = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(tryButton, Dock.Right);
        tryButton.Margin = new Avalonia.Thickness(6, 0, 0, 0);
        trying.Children.Add(tryButton);
        trying.Children.Add(tryBox);
        rows.Children.Add(InspectorRows.Field("Try it", trying));
        rows.Children.Add(tried);

        keepBox.IsEnabled = decisions.Credentials.CanKeep;
        ToolTip.SetTip(tryBox, $"Asks the model on the form: {TryQuestion}");

        modelBox.SelectionChanged += (_, _) => ShowModel();
        form.Changed += (_, _) => ShowState();
        download.Click += async (_, _) => await Download();
        tryButton.Click += async (_, _) => await Try();
        forget.Click += (_, _) =>
        {
            if (Picked is { } model) decisions.Credentials.Forget(Decisions.Account(model));
            ShowState();
        };
    }

    public string Name => "Decisions";

    public Control View => rows;

    /// <summary>The model on the picker, or null for none.</summary>
    internal IDecisionModel? Picked =>
        modelBox.SelectedIndex is var row && row > 0 && row <= decisions.Models.Count ? decisions.Models[row - 1] : null;

    public void Start() => Show();

    public void Opening() => Show();

    /// <summary>Puts the saved choice back on the controls.</summary>
    public void Show()
    {
        downloading?.Cancel();

        modelBox.ItemsSource = new[] { NoModel }.Concat(decisions.Models.Select(m => m.Name)).ToList();

        var chosen = decisions.Chosen;
        modelBox.SelectedIndex = shown = chosen is null ? 0 : IndexOf(chosen) + 1;

        keyBox.Text = string.Empty;
        tried.Text = string.Empty;
        ShowModel();
    }

    public void Save()
    {
        downloading?.Cancel();

        var model = Picked;

        if (settings.Current.Model is not null || modelBox.SelectedIndex != shown)
            settings.Current.Model = model?.Id ?? DecisionSettings.Off;

        shown = modelBox.SelectedIndex;

        if (model is not null)
        {
            settings.Current.Remember(model.Id, form.Values);
            TakeKey(model);
        }

        try
        {
            settings.Save();
        }
        catch (Exception ex)
        {
            report.Say($"Could not save the decision settings: {ex.Message}", settings.Path);
        }

        ShowState();
    }

    private int IndexOf(IDecisionModel model)
    {
        for (var i = 0; i < decisions.Models.Count; i++)
            if (ReferenceEquals(decisions.Models[i], model))
                return i;

        return -1;
    }

    private void ShowModel()
    {
        var model = Picked;

        form.Show(model is null ? null : model.Form, model is null ? Plugins.Settings.SettingValues.None : settings.Current.Of(model.Id));
        ShowState();
    }

    private void ShowState()
    {
        var model = Picked;

        keyRows.IsVisible = model?.Credential is not null;
        tryButton.IsEnabled = model is not null && downloading is null;

        if (model is null)
        {
            status.Text = "Nothing is asked, and nothing is sent anywhere.";
            download.IsVisible = false;
            return;
        }

        if (model.Credential is { } credential)
        {
            var source = decisions.Credentials.SourceOf(Decisions.Account(model), credential.EnvironmentVariable);

            keyBox.PlaceholderText = source switch
            {
                CredentialSource.Environment => $"A key is set, from {credential.EnvironmentVariable}",
                CredentialSource.Kept => "A key is set, and kept",
                CredentialSource.Session => "A key is set, for this window",
                _ => "Paste a key",
            };

            keyNote.Text = source == CredentialSource.None ? credential.Help : "It is never shown back here; type a new one to replace it.";
            forget.IsEnabled = decisions.Credentials.HasEntered(Decisions.Account(model));
        }

        var missing = decisions.Store.Missing(model);
        var prepared = decisions.Store.Prepared(model);

        download.IsVisible = model is IPreparedModel && !prepared;
        download.IsEnabled = downloading is null && decisions.Store.Root is not null;

        if (downloading is not null) return;

        status.Text = !prepared && model is IPreparedModel needs
            ? $"{model.Name} needs {Decisions.Megabytes(missing)} downloaded from {string.Join(", ", needs.Needs.Select(f => f.Address.Host).Distinct())}, into {decisions.Store.FolderOf(model)}."
            : Unavailable(model) ?? $"{model.Name} is ready{Sends(model)}";
    }

    /// <summary>What the picker's model would say about the form as it stands.</summary>
    private string? Unavailable(IDecisionModel model)
    {
        try
        {
            return model.Unavailable(Configured(model));
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string Sends(IDecisionModel model) =>
        model.Credential is null ? ", and sends nothing anywhere." : ". What is asked is sent to it.";

    /// <summary>The model as the form stands, with the key in the box ahead of the one in hand.</summary>
    private DecisionConfig Configured(IDecisionModel model)
    {
        var values = form.Values;
        var origin = model.Endpoint(values) is { IsAbsoluteUri: true } address ? KeyedTransport.OriginOf(address) : null;

        var transport = model.Credential is { } credential && !string.IsNullOrWhiteSpace(keyBox.Text)
            ? new KeyedTransport(keyBox.Text, origin, credential)
            : decisions.Credentials.Transport(Decisions.Account(model), model.Credential, origin);

        return new DecisionConfig(transport, values, decisions.Store.FolderOf(model));
    }

    private void TakeKey(IDecisionModel model)
    {
        if (string.IsNullOrWhiteSpace(keyBox.Text) || model.Credential is null) return;

        var origin = model.Endpoint(form.Values) is { IsAbsoluteUri: true } address ? KeyedTransport.OriginOf(address) : null;

        if (origin is null)
            report.Say("Key not taken: the endpoint is not an address yet, and a key is kept for the one it goes to.");
        else if (KeySafety.Refused(keyBox.Text) is { } refused)
            report.Say($"Key not taken: {refused}");
        else
        {
            var keep = keepBox.IsChecked == true && decisions.Credentials.CanKeep;
            decisions.Credentials.Accept(Decisions.Account(model), keyBox.Text, origin, keep);
            report.Say(keep ? $"Key saved, and kept by {decisions.Credentials.Store?.Name}." : "Key saved, for this window only.");
        }

        keyBox.Text = string.Empty;
    }

    /// <summary>Downloads what the picked model needs; the press is the yes.</summary>
    private async Task Download()
    {
        if (Picked is not { } model || downloading is not null) return;

        using var stop = new CancellationTokenSource();
        downloading = stop;
        ShowState();

        var progress = new Progress<(long Done, long Total)>(p =>
            status.Text = $"Downloading {model.Name}: {Decisions.Megabytes(p.Done)} of {Decisions.Megabytes(p.Total)}.");

        try
        {
            await decisions.Store.Prepare(model, clients.CreateClient(Client), progress, stop.Token);
            downloading = null;
            ShowState();
        }
        catch (OperationCanceledException)
        {
            downloading = null;
            ShowState();
            status.Text = "The download stopped. What arrived is kept aside and not used.";
        }
        catch (Exception ex)
        {
            downloading = null;
            ShowState();
            status.Text = ex.Message;
        }
    }

    /// <summary>Asks the model on the form the routing question about what is in the box.</summary>
    private async Task Try()
    {
        if (Picked is not { } model || string.IsNullOrWhiteSpace(tryBox.Text)) return;

        tried.Text = "Asking…";

        try
        {
            var request = DecisionRequest.One(tryBox.Text, "edit", new Plugins.Decide.Question.YesNo(TryQuestion));

            if (request.Problem() is { } problem)
            {
                tried.Text = problem;
                return;
            }

            if (!decisions.Store.Prepared(model) || Unavailable(model) is not null)
            {
                tried.Text = status.Text;
                return;
            }

            using var late = new CancellationTokenSource(Decisions.Deadline);
            var decision = await model.DecideAsync(request, Configured(model), late.Token);

            tried.Text = decision.Answers.GetValueOrDefault("edit") is Answer.YesNo yes
                ? $"{(yes.Probability >= 0.5 ? "Yes" : "No")}, {yes.Probability:0.00} that it asks for a change."
                : "It answered something other than a yes or a no.";
        }
        catch (Exception ex)
        {
            tried.Text = ex is OperationCanceledException ? $"{model.Name} did not answer within {Decisions.Deadline.TotalSeconds:0} seconds." : ex.Message;
        }
    }

    private static TextBlock Note(string? name = null, string text = "") => new()
    {
        Name = name,
        Text = text,
        FontSize = Text.Small,
        Foreground = Text.Muted,
        TextWrapping = TextWrapping.Wrap,
    };
}
