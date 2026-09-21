using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.Plugins.Assist;

namespace Flyback.App.Controls;

/// <summary>The assistant settings' probe button, what it warns and what it found.</summary>
/// <remarks>
/// Hidden for a provider that cannot be asked — most of them can, and the one
/// that cannot has nothing to say about why a dead button is there.
/// </remarks>
internal sealed class ProbeSection : StackPanel
{
    private static readonly IBrush Amber = new SolidColorBrush(Colors.Attention);

    private readonly Func<IPatchAssistant?> assistant;

    /// <summary>The key the probe would go with, which may be typed and not yet saved.</summary>
    private readonly Func<string?> keyOnTheForm;

    private readonly SettingsForm form;

    private readonly Action refresh;

    /// <summary>
    /// Asks the endpoint about the model on the form. The same survey the
    /// <c>probe</c> command runs, narrowed to the one model and pointed at what is
    /// on this form rather than at what was last saved.
    /// </summary>
    private readonly Button probe = new() { Name = "probe", FontSize = Text.Body };

    /// <summary>
    /// What a probe costs, said in amber above the button rather than in the grey
    /// paragraph over it.
    /// </summary>
    /// <remarks>
    /// The one control in this window that spends money on being clicked, and the
    /// amount is not small: three requests a model against a list that runs to
    /// dozens. Somebody about to press it has to have been told, and a sentence in
    /// the middle of an explanation is a sentence nobody read.
    /// </remarks>
    private readonly TextBlock probeWarning = new()
    {
        Name = "probeWarning",
        Text = "Every question is a real request on your key: this costs money, "
            + "three requests for the one model.",
        FontSize = Text.Small,
        Foreground = Amber,
        Width = 260,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>
    /// What the probe is doing, or what it found. Speaks only once one has been
    /// run: the standing explanation is the lines above it, which do not move.
    /// </summary>
    private readonly TextBlock probeNote = new()
    {
        Name = "probeNote",
        FontSize = Text.Small,
        Foreground = Text.Muted,
        Width = 260,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>The probe going on, if one is. Its presence is what "running" means.</summary>
    private CancellationTokenSource? probing;

    /// <param name="assistant">The provider picked on the form, as it is now.</param>
    /// <param name="key">The key the probe would go with.</param>
    /// <param name="form">The provider's form, read for the model and written with what was found.</param>
    /// <param name="refresh">Told when a probe ends, so the window around it catches up.</param>
    public ProbeSection(Func<IPatchAssistant?> assistant, Func<string?> key, SettingsForm form, Action refresh)
    {
        this.assistant = assistant;
        keyOnTheForm = key;
        this.form = form;
        this.refresh = refresh;

        Spacing = 8;

        Children.Add(new TextBlock
        {
            Text = "Asks the endpoint whether the model above is really there and what it will take, using "
                + "the provider, the form and the key as they stand — saved or not. What it finds is "
                + "written down when you Save. The flyback-cli probe command asks about every model the "
                + "endpoint has, which is what fills the list.",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
        });
        Children.Add(probeWarning);
        Children.Add(probe);
        Children.Add(probeNote);

        probe.Click += (_, _) => _ = ProbeAsync();
    }

    /// <summary>
    /// The probe button, in whichever of its two jobs applies, and dead where
    /// there is no key to go with.
    /// </summary>
    public void ShowState()
    {
        var asking = assistant();

        IsVisible = asking is IModelSurvey;

        if (asking is not IModelSurvey) return;

        var running = probing is not null;
        var key = keyOnTheForm();

        probe.Content = running ? "Stop" : "Probe this model";
        probe.IsEnabled = running || key is not null;

        ToolTip.SetTip(probe, running
            ? "Stop — it ends at the next thing it asks"
            : key is null
                ? $"No key for {asking.Name}. Paste one above; it does not have to be saved first."
                : null);
    }

    /// <summary>Ends a probe the settings window is closing out from under.</summary>
    public void Stop() => probing?.Cancel();

    /// <summary>
    /// Asks the endpoint about the model on the form, with what is on the form
    /// rather than with what was last saved.
    /// </summary>
    /// <remarks>
    /// The same survey <c>flyback-cli probe</c> runs, and the one button here that
    /// spends money on being pressed — which is why it asks about one model rather
    /// than about a list. Which setting names that model is the provider's business,
    /// so this asks for "the chosen one" and the provider works out what it means
    /// (ADR-0069). What comes back goes onto the form and nowhere else: this window
    /// keeps nothing until Save.
    /// </remarks>
    private async Task ProbeAsync()
    {
        if (probing is { } running)
        {
            running.Cancel();
            return;
        }

        // Read now rather than on the other side of the await. The form stays
        // live under a probe, and what comes back is about what was on it when
        // the button went down.
        var asked = assistant();

        if (asked is not IModelSurvey survey) return;
        if (keyOnTheForm() is not { } key) return;

        var config = new AssistantConfig(key, form.Values);

        using var stopping = new CancellationTokenSource();

        probing = stopping;

        Say("Asking the endpoint…");
        ShowState();

        try
        {
            var found = await survey.Survey(
                config,
                new SurveyOptions { Chosen = true },
                new Commentary(Say),
                stopping.Token);

            // Somebody picked another provider while this ran. One provider's
            // models on another's form would be worse than the answer being
            // lost, and the answer is already lost either way.
            if (!ReferenceEquals(assistant(), asked))
            {
                Say($"{asked.Name} answered after the provider changed, so nothing was kept.", amber: true);
                return;
            }

            if (found.Count == 0)
            {
                Say("The endpoint did not answer for that model. Nothing was changed.", amber: true);
                return;
            }

            var said = string.Join(" ", found.Select(Says));
            var known = Survey.Read(form.Values.Text(Survey.Key, string.Empty));

            // A probe of one model is a fact about that model and about nothing
            // else. Written over the list a full survey left, where there is one;
            // never written down as a list of its own, which would take every
            // other name off the box on the strength of never having asked.
            if (known.Count == 0)
            {
                Say(said);
                return;
            }

            form.Put(Survey.Key, Survey.Write(Merged(known, found)));
            Say($"{said} Save to keep it.");
        }
        catch (OperationCanceledException)
        {
            Say("Stopped. Nothing was kept.");
        }
        catch (Exception ex)
        {
            // A provider's own failure. The window survives it, and the sentence
            // is the whole of what anybody can act on.
            Say(ex.Message, amber: true);
        }
        finally
        {
            probing = null;

            refresh();
        }
    }

    /// <summary>What one model answered, in the words the command uses for it.</summary>
    private static string Says(ModelReport model)
    {
        var senses = new List<string>();

        if (model.Vision) senses.Add("sees");
        if (model.Hearing) senses.Add("hears");

        return $"{model.Id} answers: {(senses.Count == 0 ? "text only" : string.Join(", ", senses))}.";
    }

    /// <summary>
    /// What was written down, with each fresh report put over the model it is
    /// about. One nobody had asked about before goes on the end, so the box keeps
    /// the order the survey that filled it left.
    /// </summary>
    private static IReadOnlyList<ModelReport> Merged(
        IReadOnlyList<ModelReport> known,
        IReadOnlyList<ModelReport> found)
    {
        var merged = known.ToList();

        foreach (var model in found)
        {
            var at = merged.FindIndex(one => string.Equals(one.Id, model.Id, StringComparison.OrdinalIgnoreCase));

            if (at < 0) merged.Add(model);
            else merged[at] = model;
        }

        return merged;
    }

    /// <summary>What the probe has to say, or nothing at all.</summary>
    private void Say(string line, bool amber = false)
    {
        probeNote.Text = line;
        probeNote.IsVisible = line.Length > 0;
        probeNote.Foreground = amber ? Amber : Text.Muted;
    }

    /// <summary>
    /// A survey's running commentary, put back on the UI thread. It is reported
    /// from whichever thread the last request came back on, and a control may not
    /// be touched from there.
    /// </summary>
    /// <remarks>
    /// Order holds: every line is posted before the survey's own task completes,
    /// so the last of them is queued ahead of what this method says afterwards.
    /// </remarks>
    private sealed class Commentary(Action<string, bool> say) : IProgress<string>
    {
        public void Report(string value) => Dispatcher.UIThread.Post(() => say(value, false));
    }
}
