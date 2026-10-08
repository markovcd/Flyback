using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Controls;
using Flyback.Editor.Inspect;
using Flyback.Editor.Settings;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Settings;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Decide;

/// <summary>
/// The Decisions section of the settings window: which decision model the editor asks,
/// how it is set, for every use or for one, its key, its download, and a box to try it with.
/// </summary>
internal sealed class DecisionsSection : ISettingsSection
{
    /// <summary>The named client downloads go over.</summary>
    public const string Client = "decision-models";

    /// <summary>What the try box asks about what is typed in it: the question the assistant is routed by.</summary>
    public const string TryQuestion = "Does this ask for a change to the patch?";

    private const string NoModel = "None";

    /// <summary>The picker's first row: the model's own settings, which every use starts from.</summary>
    private const string EveryUse = "Every use";

    /// <summary>The uses in the picker's order, after <see cref="EveryUse"/>.</summary>
    private static readonly KeyValuePair<string, string>[] Uses = [.. DecisionUse.All];

    private readonly Decisions decisions;
    private readonly DecisionSettingRepository settings;
    private readonly IHttpClientFactory clients;
    private readonly ReportLine report;

    private readonly StackPanel rows = new() { Spacing = 8, Width = SettingsSession.SectionWidth };

    private readonly ComboBox modelBox = new Picker { Name = "decisionModel", FontSize = Text.Body, HorizontalAlignment = HorizontalAlignment.Stretch };

    private readonly ComboBox useBox = new Picker { Name = "decisionUse", FontSize = Text.Body, HorizontalAlignment = HorizontalAlignment.Stretch };

    private readonly SettingsForm form = new() { Beside = true };

    /// <summary>What each use lays over the model's settings, under the form, so it is seen whichever row the picker is on.</summary>
    private readonly TextBlock laid = Note("decisionUses");

    /// <summary>
    /// What the form has been changed to since the saved settings were shown: the model's
    /// own values under "", and under each use only what it lays over them. Saved by
    /// <see cref="Save"/>, dropped by <see cref="Show"/>.
    /// </summary>
    private readonly Dictionary<string, SettingValues> drafts = new(StringComparer.Ordinal);

    private readonly KeyRows key;

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
        key = new KeyRows(decisions.Credentials, "decisionKey");

        rows.Children.Add(Note(text:
            "A decision model answers small questions about words with a probability: whether a message asks for an edit, "
            + "which module a phrase means. Whatever asks works as it did without one."));
        rows.Children.Add(InspectorRows.Field("Model", modelBox));
        rows.Children.Add(InspectorRows.Field("For", useBox));
        rows.Children.Add(form);
        rows.Children.Add(laid);

        laid.Margin = new Avalonia.Thickness(InspectorRows.SettingsGutter, 0, 0, 0);
        useBox.ItemsSource = new[] { EveryUse }.Concat(Uses.Select(u => Sentence(u.Value))).ToList();
        useBox.SelectedIndex = 0;
        ToolTip.SetTip(useBox, "A use may lay a setting of its own over the model's, such as a checkpoint it does better on.");

        rows.Children.Add(key.View);

        rows.Children.Add(status);
        rows.Children.Add(download);

        var trying = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(tryButton, Dock.Right);
        tryButton.Margin = new Avalonia.Thickness(6, 0, 0, 0);
        trying.Children.Add(tryButton);
        trying.Children.Add(tryBox);
        rows.Children.Add(InspectorRows.Field("Try it", trying));
        rows.Children.Add(tried);

        ToolTip.SetTip(tryBox, $"Asks the model on the form: {TryQuestion}");

        modelBox.SelectionChanged += (_, _) => ShowModel();
        useBox.SelectionChanged += (_, _) => ShowForm();
        form.Changed += (_, _) =>
        {
            Keep();
            ShowState();
        };
        download.Click += async (_, _) => await Download();
        tryButton.Click += async (_, _) => await Try();
        key.Forgotten += (_, _) => ShowState();
    }

    public string Name => "Decisions";

    public Control View => rows;

    /// <summary>The model on the picker, or null for none.</summary>
    internal IDecisionModel? Picked =>
        modelBox.SelectedIndex is var row && row > 0 && row <= decisions.Models.Count ? decisions.Models[row - 1] : null;

    /// <summary>The <see cref="DecisionUse"/> the form is shown for, or "" for the model's own settings.</summary>
    private string Use => useBox.SelectedIndex is var row && row > 0 && row <= Uses.Length ? Uses[row - 1].Key : "";

    public void Start() => Show();

    public void Opening() => Show();

    /// <summary>Puts the saved choice back on the controls.</summary>
    public void Show()
    {
        downloading?.Cancel();

        modelBox.ItemsSource = new[] { NoModel }.Concat(decisions.Models.Select(m => m.Name)).ToList();

        var chosen = decisions.Chosen;
        modelBox.SelectedIndex = shown = chosen is null ? 0 : IndexOf(chosen) + 1;

        key.Clear();
        tried.Text = string.Empty;
        ShowModel();
    }

    /// <summary>Picks the use the form is shown for, by the picker's own wording; <see cref="EveryUse"/> for the model's own settings.</summary>
    internal void ShowUse(string wording) => useBox.SelectedItem = wording;

    public void Save()
    {
        downloading?.Cancel();

        var model = Picked;

        if (settings.Current.Model is not null || modelBox.SelectedIndex != shown)
            settings.Current.Model = model?.Id ?? DecisionSettings.Off;

        shown = modelBox.SelectedIndex;

        if (model is not null)
        {
            Keep();
            settings.Current.Remember(model.Id, Base(model));

            foreach (var (use, over) in drafts)
                if (use.Length > 0)
                    settings.Current.Remember(model.Id, use, over);

            if (key.Take(Origin(model)) is { } said) report.Say(said);
        }

        drafts.Clear();

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
        drafts.Clear();

        if (useBox.SelectedIndex != 0) useBox.SelectedIndex = 0;

        ShowForm();
    }

    /// <summary>Puts the picked model's settings on the form as the picked use would ask with them.</summary>
    private void ShowForm()
    {
        var model = Picked;
        var use = Use;

        var values = model is null ? SettingValues.None
            : use.Length == 0 ? Base(model)
            : DecisionSettings.Laid(Base(model), Over(model, use));

        form.Show(model is null ? null : model.Form, values);
        ShowLaid();
        ShowState();
    }

    /// <summary>Takes the form as it stands into <see cref="drafts"/>: for a use, only what differs from the model's own.</summary>
    private void Keep()
    {
        if (Picked is not { } model) return;

        var use = Use;

        if (use.Length == 0) drafts[""] = form.Values;
        else drafts[use] = Differs(form.Values, Base(model));

        ShowLaid();
    }

    /// <summary>The model's own settings, as drafted or as saved.</summary>
    private SettingValues Base(IDecisionModel model) => drafts.GetValueOrDefault("") ?? settings.Current.Of(model.Id);

    /// <summary>What <paramref name="use"/> lays over the model's own settings, as drafted or as saved.</summary>
    private SettingValues Over(IDecisionModel model, string use) => drafts.GetValueOrDefault(use) ?? settings.Current.Over(model.Id, use);

    private void ShowLaid()
    {
        var model = Picked;

        var lines = model is null
            ? []
            : Uses.Select(u => (u.Value, Over: Over(model, u.Key)))
                .Where(u => u.Over.All.Count > 0)
                .Select(u => $"For {u.Value}: {string.Join(", ", u.Over.All.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => $"{v.Key}={v.Value}"))}.");

        laid.Text = string.Join('\n', lines);
        laid.IsVisible = laid.Text.Length > 0;
    }

    /// <summary>The pairs of <paramref name="values"/> that <paramref name="from"/> does not hold; a value absent and one empty are the same.</summary>
    private static SettingValues Differs(SettingValues values, SettingValues from) =>
        new(values.All.Where(p => !string.Equals(from.All.GetValueOrDefault(p.Key) ?? "", p.Value, StringComparison.Ordinal)));

    private static string Sentence(string words) => char.ToUpperInvariant(words[0]) + words[1..];

    private void ShowState()
    {
        var model = Picked;

        key.Show(model is null ? null : Decisions.Account(model), model?.Credential);
        tryButton.IsEnabled = model is not null && downloading is null;

        if (model is null)
        {
            status.Text = "Nothing is asked, and nothing is sent anywhere.";
            download.IsVisible = false;
            return;
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
        var origin = Origin(model);

        var transport = model.Credential is { } credential && !string.IsNullOrWhiteSpace(key.Entered)
            ? new KeyedTransport(key.Entered, origin, credential)
            : decisions.Credentials.Transport(Decisions.Account(model), model.Credential, origin);

        return new DecisionConfig(transport, form.Values, decisions.Store.FolderOf(model));
    }

    /// <summary>Where the model on the form sends, or null where it sends nowhere or the endpoint is no address yet.</summary>
    private string? Origin(IDecisionModel model)
    {
        try
        {
            return model.Endpoint(form.Values) is { IsAbsoluteUri: true } address ? KeyedTransport.OriginOf(address) : null;
        }
        catch
        {
            return null;
        }
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
