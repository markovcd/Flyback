using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Flyback.App.Assist;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;

namespace Flyback.App.Controls;

/// <summary>
/// Where you describe a patch and watch one get built.
/// </summary>
/// <remarks>
/// <para>
/// A control rather than a method on the window, for the reason
/// <see cref="NodeEditor"/> and <see cref="PreviewSurface"/> are: it has state
/// and behaviour of its own, and the window is long enough already.
/// </para>
/// <para>
/// It owns no patch. It is handed one when somebody asks a question and hands
/// one back when they accept a proposal — everything in between happens on
/// <see cref="AssistantRun"/>'s copy, so a run that is abandoned, cancelled or
/// simply bad costs nothing at all.
/// </para>
/// </remarks>
public sealed class AssistantPanel : UserControl
{
    private static readonly IBrush Dim = new SolidColorBrush(Colors.Muted);
    private static readonly IBrush Amber = new SolidColorBrush(Colors.Attention);

    /// <summary>The middle of <see cref="LogoMark"/>'s sweep, borrowed for the one thing here that is alive.</summary>
    private static readonly IBrush Live = new SolidColorBrush(Colors.Feedback);

    private readonly PluginCatalog plugins;
    private readonly Func<Patch> current;

    /// <summary>
    /// Where a Sample module's file is looked up, so what the assistant hears is
    /// what the editor plays. Null in a test, which makes every player silent.
    /// </summary>
    private readonly ISampleLibrary? samples;
    private readonly IImageLibrary? pictures;
    /// <summary>
    /// Hands a patch to the canvas, where it lands as an edit rather than as a
    /// new document. Nothing can refuse it and nothing needs to: an assistant's
    /// patch goes into the history like any other edit, so the way back out is
    /// the one every other edit already has.
    /// </summary>
    private readonly Action<Patch> apply;

    private readonly Action<string, string?> report;

    private readonly AssistantSettings settings;
    private readonly Credentials credentials;

    private readonly TextBox instruction = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        PlaceholderText = "Describe the patch you want. Enter to ask, Ctrl+Enter for a new line.",
        FontSize = 12,
        MinHeight = 68,

        // A strip along the bottom for the send button to sit in. Reserved as
        // padding rather than left to overlap, so no line of a long message ever
        // runs underneath it — and across the whole width rather than down the
        // right-hand side, so every other line still has the box to itself.
        Padding = new Thickness(8, 6, 8, 34),
    };

    private readonly StackPanel saidPanel = new() { Spacing = 4, Margin = new Thickness(10, 8) };
    private readonly ScrollViewer transcript = new();

    /// <summary>
    /// The last frame the assistant looked at, and nothing at all until it has
    /// looked at one. Hidden rather than merely empty: a fixed width in an Auto
    /// column holds its 160 pixels open whether or not there is a picture in it,
    /// and that is a strip of dead panel beside the transcript and the
    /// instruction box for the whole of every run that never renders.
    /// </summary>
    private readonly Image lastFrame = new()
    {
        Width = 160,
        Stretch = Stretch.Uniform,
        IsVisible = false,
    };
    private readonly TextBlock footer = new() { FontSize = 11, Foreground = Dim, TextWrapping = TextWrapping.Wrap };

    /// <summary>
    /// Proof that something is still happening.
    /// </summary>
    /// <remarks>
    /// A turn is minutes, not seconds, and most of it is spent waiting on a
    /// provider with nothing to show: the transcript only fills in when an edit
    /// lands, and a rate limit is waited out in silence by design. Without this
    /// the panel is indistinguishable from one that has died — which is what
    /// makes a beacon that moves the point of it, rather than a label that could
    /// as easily be stale.
    /// </remarks>
    private readonly Ellipse beacon = new()
    {
        Width = 7,
        Height = 7,
        Fill = Live,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock progress = new()
    {
        FontSize = 11,
        Foreground = Live,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly StackPanel working = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 7,
        Margin = new Thickness(0, 0, 0, 6),
        IsVisible = false,
    };

    private readonly DispatcherTimer heartbeat = new() { Interval = TimeSpan.FromMilliseconds(200) };

    /// <summary>What the one button shows in each of its two jobs.</summary>
    private const string SendGlyph = "⏎";

    private const string StopGlyph = "■";

    /// <summary>
    /// Send and stop, which are one button because they are never both offered:
    /// a turn is either wanted or under way. Putting them together also puts the
    /// way to interrupt a run exactly where the hand that started it last was.
    /// </summary>
    /// <remarks>
    /// Small, square and in the instruction box's own bottom-right corner rather
    /// than out in the column with Apply and Settings. This is part of writing
    /// the message rather than something done to a proposal afterwards — and it
    /// is the first thing here to say that a message can be sent at all, which
    /// until now was a keystroke mentioned in a placeholder and nowhere else.
    /// </remarks>
    private readonly Button send = new()
    {
        Content = SendGlyph,
        Width = 30,
        Height = 26,
        Padding = new Thickness(0),
        FontSize = 15,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 7, 7),
        IsEnabled = false,
    };

    private readonly TextBox keyBox = new() { PasswordChar = '•', FontSize = 12, Width = 260 };

    /// <summary>
    /// What is under the key field, since the field itself cannot say it.
    /// </summary>
    /// <remarks>
    /// A key that is set is never read back into the box — ADR-0034, and the
    /// reason the box is blank however many keys are in force. Blank is exactly
    /// what "no key" looks like too, though, so without this the one screen
    /// somebody opens to check cannot answer the question they opened it to ask.
    /// </remarks>
    private readonly TextBlock keyNote = new()
    {
        FontSize = 11,
        Foreground = Dim,
        Width = 260,
        TextWrapping = TextWrapping.Wrap,
    };

    private readonly Button forget = new() { Content = "Forget key", Width = 100 };
    /// <summary>
    /// The models the provider suggests, and anything else that can be typed
    /// over them. Editable rather than a plain list because the endpoint is a
    /// field — a suggestion is what a provider knows about, not what it allows.
    /// </summary>
    private readonly ComboBox modelBox = new()
    {
        FontSize = 12,
        Width = 260,
        Name = "model",
        IsEditable = true,
    };

    /// <summary>What the chosen model will and will not accept being handed.</summary>
    private readonly TextBlock modelNote = new()
    {
        FontSize = 11,
        Foreground = Dim,
        Width = 260,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBox baseUrlBox = new() { FontSize = 12, Width = 260 };
    private readonly CheckBox rememberBox = new() { Content = "Keep this key", FontSize = 12 };
    private readonly CheckBox visionBox = new() { Content = "Let it look at the picture", FontSize = 12 };
    private readonly CheckBox hearingBox = new() { Content = "Let it listen to the sound", FontSize = 12 };

    /// <summary>
    /// Which model is played the sound, which is never the one doing the
    /// building — see <see cref="AssistantConfig.EarModel"/>. A plain list
    /// rather than an editable one: this is a capability rather than a
    /// preference, and a name typed here that cannot hear fails one tool call
    /// at a time, in the middle of a run.
    /// </summary>
    private readonly ComboBox earBox = new() { FontSize = 12, Width = 260, Name = "ear" };
    private readonly ComboBox providerBox = new() { FontSize = 12, Width = 260 };
    private readonly ComboBox effortBox = new()
    {
        FontSize = 12,
        Width = 260,
        Name = "effort",
        ItemsSource = Enum.GetNames<AssistantEffort>(),
    };

    private IPatchAssistant? assistant;
    private AssistantRun? run;

    /// <summary>
    /// What the conversation in <see cref="run"/> was started with. A session is
    /// built around one model at one endpoint and cannot be moved to another, so
    /// these are what say whether it is still the right conversation to be
    /// having — see <see cref="Restarting"/>.
    /// </summary>
    private AssistantConfig? runConfig;

    private IPatchAssistant? runAssistant;

    /// <summary>
    /// The paragraph the assistant is in the middle of, or null when it is not
    /// in the middle of one. Held rather than found, because what "the last
    /// block" is changes the moment anything else is written.
    /// </summary>
    private SelectableTextBlock? saying;

    /// <summary>Built the first time the settings window asks for it, and kept.</summary>
    private Control? section;

    /// <summary>
    /// Whether a turn is in flight, as this panel knows it.
    /// </summary>
    /// <remarks>
    /// Not <see cref="AssistantRun.Running"/>, which is the run's own answer and
    /// arrives too late to be one: <c>Ask</c> is an async iterator, so its body
    /// does not run — and the flag inside it is not set — until the first
    /// <c>MoveNextAsync</c>, which happens after this panel has already refreshed
    /// its buttons. Asking the run left Stop dead for the whole of every turn.
    /// This is set the moment somebody presses Enter, which is the moment it
    /// becomes true from out here.
    /// </remarks>
    private bool asking;

    private bool stopping;

    /// <summary>
    /// Why a message cannot be sent, or null when one can. Kept rather than
    /// asked for, because the button is refreshed on every keystroke and the
    /// answer comes from the credential store.
    /// </summary>
    private string? blocked;
    private DateTime startedAt;
    private int pulse;

    /// <param name="report"></param>
    /// <param name="saved">
    /// The choices to open on, defaulting to the ones on this machine. Named
    /// only so a test can put a set in front of the panel without writing the
    /// file somebody is actually using — what these controls make of a given set
    /// of choices is most of what this class does.
    /// </param>
    /// <param name="plugins"></param>
    /// <param name="current"></param>
    /// <param name="apply"></param>
    /// <param name="samples"></param>
    /// <param name="pictures"></param>
    public AssistantPanel(
        PluginCatalog plugins,
        Func<Patch> current,
        Action<Patch> apply,
        Action<string, string?> report,
        AssistantSettings? saved = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null)
    {
        settings = saved ?? AssistantSettings.Load();
        this.samples = samples;
        this.pictures = pictures;
        this.plugins = plugins;
        this.current = current;
        this.apply = apply;
        this.report = report;

        credentials = new Credentials(plugins.PreferredSecretStore);
        assistant = Choose();

        Content = Build();

        // Subscribed here rather than where the settings window is put together.
        // That window is built the first time somebody opens one, which is long
        // after the saved choices are restored below — so a handler living there
        // would not run for the tick this restores, and the ear would sit greyed
        // out beside a box that says listening is on.
        hearingBox.IsCheckedChanged += (_, _) => ShowEarState();

        ReadSettingsIntoControls();
        Refresh();
    }

    /// <summary>What the status bar should say about this. Never names a key.</summary>
    public string Summary => assistant is null ? "assistant: none" : $"assistant: {assistant.Name}";

    // --- building -----------------------------------------------------------

    private Control Build()
    {
        transcript.Content = saidPanel;
        transcript.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;

        // Tunnelling, and both halves of the gesture answered here: the box would
        // otherwise take Enter for itself on the way back up, and what it does
        // with a modifier held is its business rather than something to bet the
        // one way of sending a message on.
        instruction.AddHandler(KeyDownEvent, Typed, RoutingStrategies.Tunnel);

        send.Click += (_, _) =>
        {
            if (!asking)
            {
                _ = AskAsync();
                return;
            }

            // Cancellation lands at the next thing the assistant does, which may
            // be a whole request away. Saying so beats a button that goes dead
            // and a panel that carries on as if nothing was asked of it.
            stopping = true;
            run?.Stop();
            Beat();
        };

        // Whether there is anything to send changes on every keystroke, and
        // asking the whole of Refresh that often would go to the credential
        // store for an answer it already has.
        instruction.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) ShowSendState();
        };

        working.Children.Add(beacon);
        working.Children.Add(progress);
        heartbeat.Tick += (_, _) => Beat();

        var left = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(working, Dock.Top);
        // The button floats over the corner of the box rather than sitting
        // beside it, so the two are one thing to lay out.
        var writing = new Panel();
        writing.Children.Add(instruction);
        writing.Children.Add(send);

        DockPanel.SetDock(writing, Dock.Bottom);
        DockPanel.SetDock(footer, Dock.Bottom);
        left.Children.Add(working);
        left.Children.Add(footer);
        left.Children.Add(writing);
        left.Children.Add(transcript);

        // Two columns, since the settings went to the toolbar and nothing else
        // here was ever a button: what is left is the conversation and, when
        // there has been one, the frame the assistant last looked at.
        var columns = new Grid
        {
            Margin = new Thickness(12, 10),
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            ],
        };

        Grid.SetColumn(left, 0);
        Grid.SetColumn(lastFrame, 1);

        columns.Children.Add(left);
        columns.Children.Add(lastFrame);

        // No height of its own. What this is worth is entirely a matter of what
        // is being read — a one-line refusal or forty turns of transcript — so
        // the window owns the split and this only says how small is too small
        // for the instruction box and a button to both still be there.
        return new Border
        {
            Background = new SolidColorBrush(Colors.Panel),
            BorderBrush = new SolidColorBrush(Colors.Edge),
            BorderThickness = new Thickness(0, 1, 0, 0),
            MinHeight = 140,
            Child = columns,
        };
    }

    /// <summary>
    /// This panel's part of the settings window. Kept and lent out rather than
    /// built afresh, because these are fields with handlers already on them and
    /// a second set would answer for a first that nothing can see.
    /// </summary>
    /// <remarks>
    /// The credential state is read again every time it is asked for. A key can
    /// arrive or leave without this panel touching anything — exported into the
    /// environment from another window, or dropped into the store by something
    /// else — and this is the one screen that claims to say which.
    /// <para>
    /// Lending the same controls out is also what keeps a half-typed endpoint or
    /// a changed provider on the form between one opening of the window and the
    /// next, which a set built fresh each time would lose.
    /// </para>
    /// </remarks>
    public Control SettingsSection()
    {
        Refresh();

        section ??= BuildSettings();

        // Taken back from whoever last borrowed it, rather than left to them to
        // return. A control has one parent and a closed window still holds the
        // one it was showing, so without this the settings would open exactly
        // once a session and throw on the second try.
        if (section.Parent is ContentControl lender) lender.Content = null;

        return section;
    }

    private Control BuildSettings()
    {
        providerBox.ItemsSource = plugins.Assistants.Select(a => a.Name).ToList();
        providerBox.SelectionChanged += (_, _) =>
        {
            if (providerBox.SelectedIndex < 0 || providerBox.SelectedIndex >= plugins.Assistants.Count) return;

            assistant = plugins.Assistants[providerBox.SelectedIndex];
            settings.Provider = assistant.Id;
            OfferModels(assistant);
            OfferEars(assistant);
            modelBox.Text = assistant.Schema.DefaultModel;
            baseUrlBox.Text = assistant.Schema.DefaultBaseUrl ?? string.Empty;
            Refresh();
        };

        // Typing is what actually decides the model — a name may be picked from
        // the list, typed over it, or arrive from the settings file — so what
        // the form says about the model follows the text rather than the
        // selection.
        modelBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == ComboBox.TextProperty) ShowModelState();
        };

        // Its own padding, because it is the whole of a window now rather than a
        // flyout hanging off the button that opened it.
        var fields = new StackPanel { Spacing = 8, Margin = new Thickness(18), Width = 280 };

        fields.Children.Add(Caption("Provider"));
        fields.Children.Add(providerBox);
        fields.Children.Add(Caption("Model"));
        fields.Children.Add(modelBox);
        fields.Children.Add(modelNote);
        fields.Children.Add(Caption("Endpoint"));
        fields.Children.Add(baseUrlBox);
        fields.Children.Add(Caption("API key"));
        fields.Children.Add(keyBox);
        fields.Children.Add(keyNote);
        fields.Children.Add(rememberBox);
        fields.Children.Add(visionBox);
        fields.Children.Add(hearingBox);
        fields.Children.Add(earBox);
        fields.Children.Add(Caption("Effort"));
        fields.Children.Add(effortBox);

        var save = new Button { Content = "Save", Width = 84 };
        save.Click += (_, _) =>
        {
            SaveSettings();

            // Saving is the end of the errand, so the window goes with it.
            // Forgetting a key is not: somebody who has just taken one out is as
            // likely as not about to put another in.
            DismissSettings();
        };

        forget.Click += (_, _) =>
        {
            if (assistant is null) return;

            credentials.Forget(assistant.Id);
            keyBox.Text = string.Empty;
            Refresh();
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(save);
        row.Children.Add(forget);
        fields.Children.Add(row);

        return fields;
    }

    /// <summary>
    /// Closes the window the settings are being shown in.
    /// </summary>
    /// <remarks>
    /// Found rather than held, because this section belongs to whichever window
    /// borrowed it — see <see cref="SettingsSection"/> — and the panel has no
    /// business keeping a reference to one it does not own.
    /// <para>
    /// The panel's own window is left alone on purpose. Nothing shows the
    /// settings there today, and the day something does, closing it would close
    /// the program.
    /// </para>
    /// <para>
    /// Internal rather than private because the UI tests need it: clicking Save
    /// writes the real settings file, so the half worth exercising is this one,
    /// which is also the half with somewhere to go wrong.
    /// </para>
    /// </remarks>
    internal void DismissSettings()
    {
        if (section is null) return;
        if (TopLevel.GetTopLevel(section) is not Window host) return;
        if (ReferenceEquals(host, TopLevel.GetTopLevel(this))) return;

        host.Close();
    }

    private static TextBlock Caption(string text) =>
        new() { Text = text, FontSize = 11, Foreground = Dim };

    /// <summary>
    /// Enter asks; Ctrl+Enter — and Shift+Enter, which every other message box
    /// in the world accepts — breaks the line.
    /// </summary>
    /// <remarks>
    /// The line break is put in by hand rather than left to the box. Whether a
    /// <see cref="TextBox"/> types a newline for a gesture with a modifier held
    /// is an implementation detail of Avalonia's, and the alternative to knowing
    /// is a message that silently refuses to grow a second line.
    /// </remarks>
    private void Typed(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        e.Handled = true;

        var breaking = e.KeyModifiers.HasFlag(KeyModifiers.Control)
            || e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (!breaking)
        {
            _ = AskAsync();
            return;
        }

        var text = instruction.Text ?? string.Empty;
        var from = Math.Clamp(Math.Min(instruction.SelectionStart, instruction.SelectionEnd), 0, text.Length);
        var to = Math.Clamp(Math.Max(instruction.SelectionStart, instruction.SelectionEnd), from, text.Length);

        instruction.Text = string.Concat(text.AsSpan(0, from), "\n", text.AsSpan(to));
        instruction.CaretIndex = from + 1;
    }

    // --- settings -----------------------------------------------------------

    private IPatchAssistant? Choose() =>
        (settings.Provider.Length > 0 ? plugins.Assistant(settings.Provider) : null)
        ?? plugins.PreferredAssistant;

    /// <summary>
    /// Fills the model list from whichever provider is chosen. Names only: what
    /// each one accepts is said in a sentence under the box rather than crammed
    /// into a row somebody is choosing from.
    /// </summary>
    private void OfferModels(IPatchAssistant from) =>
        modelBox.ItemsSource = from.Schema.SuggestedModels.Select(m => m.Id).ToList();

    /// <summary>
    /// Fills the list of models that can listen, and says whether there is any
    /// point in it. A provider with no ear at all disables the tick itself —
    /// there is nothing to turn on.
    /// </summary>
    private void OfferEars(IPatchAssistant from)
    {
        var ears = from.Schema.Ears.Select(m => m.Id).ToList();

        earBox.ItemsSource = ears;
        earBox.SelectedIndex = ears.Count == 0
            ? -1
            : Math.Max(0, ears.IndexOf(settings.EarModel));

        hearingBox.IsEnabled = ears.Count > 0;

        ToolTip.SetTip(
            hearingBox,
            ears.Count > 0 ? null : $"{from.Name} has no model that takes a sound.");

        ShowEarState();
    }

    /// <summary>
    /// The ear is only a question while the answer is wanted: shown where there
    /// is anything to listen with, and enabled while listening is on.
    /// </summary>
    /// <remarks>
    /// Read off the list and the tick rather than off the checkbox's own
    /// enabled state, which is another thing that has to have been set first.
    /// This is called from anywhere either could have changed, and has to be
    /// right whichever order they were set in.
    /// </remarks>
    private void ShowEarState()
    {
        var any = earBox.ItemCount > 0;

        earBox.IsVisible = any;
        earBox.IsEnabled = any && hearingBox.IsChecked == true;
    }

    /// <summary>What is known about the model in the box, or null when it is a stranger.</summary>
    private AssistantModel? Chosen() => assistant?.Schema.Known(ModelName());

    private string ModelName() => string.IsNullOrWhiteSpace(modelBox.Text)
        ? assistant?.Schema.DefaultModel ?? string.Empty
        : modelBox.Text;

    /// <summary>Which model listens, or null when this provider has none.</summary>
    private string? EarName() => earBox.SelectedItem as string;

    /// <summary>
    /// Puts what the model accepts in front of the person choosing it, and takes
    /// away the two switches it would refuse.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only sight is the model in the box's business. The sound never goes to
    /// it — it goes to the ear, which is a different model chosen separately —
    /// so hearing turns on whether the <em>provider</em> has one, not whether
    /// this model does.
    /// </para>
    /// <para>
    /// The tick itself is left alone, which is deliberate. A disabled box that
    /// keeps its state is a preference parked; one that clears itself is a
    /// preference destroyed by passing through a model on the way to another.
    /// <see cref="Configured"/> is what makes it safe — it sends no picture to a
    /// model recorded as refusing one, whatever the box still shows.
    /// </para>
    /// <para>
    /// A model nobody here has heard of is left alone rather than disabled. Not
    /// knowing is not the same as knowing it cannot, and the whole point of an
    /// editable endpoint is that it reaches things this was never told about.
    /// </para>
    /// </remarks>
    private void ShowModelState()
    {
        var known = Chosen();

        visionBox.IsEnabled = known?.Vision != false;
        ToolTip.SetTip(visionBox, visionBox.IsEnabled ? null : $"{known!.Id} does not take pictures.");

        modelNote.Text = known is null
            ? "Nothing is known about this one here, so the switch below is yours to set. An "
              + "endpoint that will not take a picture answers with a 400."
            : Handles(known);
    }

    /// <summary>
    /// One line naming what the model doing the building accepts. It names the
    /// model it matched rather than what was typed, so a dated snapshot says
    /// which family it was read as — see <see cref="AssistantSchema.Known"/>.
    /// </summary>
    private static string Handles(AssistantModel model) => (model.Vision, model.Hearing) switch
    {
        (true, _) => $"{model.Id} takes pictures.",

        // Worth saying rather than leaving somebody to wonder why they chose an
        // audio model and lost the ability to look at anything: this one belongs
        // in the ear below, where the sound actually goes.
        (false, true) => $"{model.Id} is a listener — it takes sound and not pictures, which is what "
            + "the ear below is for. Driving with it works, but it builds blind.",

        _ => $"{model.Id} takes no pictures, so it builds from the compiler alone.",
    };

    /// <summary>
    /// Puts the saved choices in the controls.
    /// </summary>
    /// <remarks>
    /// The switches go first, and the order is load-bearing rather than tidy:
    /// what the model note says and whether the ear may be chosen are both read
    /// off them, so anything filled in above a tick that has not been restored
    /// yet is showing the state of a session nobody had. That is one half of a
    /// box that opened greyed out and came right the moment its tick was
    /// touched; the other half is that the tick's handler is subscribed where
    /// this can reach it rather than in the settings window, which is not built
    /// until somebody opens one.
    /// </remarks>
    private void ReadSettingsIntoControls()
    {
        visionBox.IsChecked = settings.Vision;
        hearingBox.IsChecked = settings.Hearing;
        rememberBox.IsChecked = settings.RememberKey;
        effortBox.SelectedIndex = (int)settings.Effort;

        if (assistant is not null)
        {
            providerBox.SelectedIndex = plugins.Assistants
                .Select((a, i) => (a, i))
                .Where(pair => pair.a.Id == assistant.Id)
                .Select(pair => pair.i)
                .DefaultIfEmpty(-1)
                .First();

            OfferModels(assistant);
            OfferEars(assistant);
            modelBox.Text = settings.Model.Length > 0 ? settings.Model : assistant.Schema.DefaultModel;
            baseUrlBox.Text = settings.BaseUrl ?? assistant.Schema.DefaultBaseUrl ?? string.Empty;
            baseUrlBox.IsEnabled = assistant.Schema.BaseUrlEditable;
        }

        ShowModelState();
        ShowEarState();
    }

    private void SaveSettings()
    {
        settings.Model = modelBox.Text ?? string.Empty;
        settings.BaseUrl = string.IsNullOrWhiteSpace(baseUrlBox.Text) ? null : baseUrlBox.Text;
        settings.Vision = visionBox.IsChecked == true;
        settings.Hearing = hearingBox.IsChecked == true;
        settings.EarModel = EarName() ?? string.Empty;
        settings.RememberKey = rememberBox.IsChecked == true;
        settings.Effort = (AssistantEffort)Math.Max(0, effortBox.SelectedIndex);

        if (assistant is not null)
        {
            var keep = settings.RememberKey && credentials.CanKeep;

            if (!string.IsNullOrWhiteSpace(keyBox.Text))
            {
                credentials.Accept(assistant.Id, keyBox.Text, keep);

                // Emptied once it has been taken. Left there it would hold the
                // secret in a control for the life of the window, and the line
                // that says where the key actually lives could never appear.
                keyBox.Text = string.Empty;
                SayWhereTheKeyWent();
            }
            else if (keep && credentials.HasEntered(assistant.Id))
            {
                // Ticking the box after the fact, with nothing typed. The key is
                // already in hand and the field is empty because this emptied
                // it, so asking for the secret again would be this program's
                // fault presented as the person's problem.
                credentials.KeepWhatIsHeld(assistant.Id);
                SayWhereTheKeyWent();
            }
        }

        try
        {
            settings.Save();
        }
        catch (Exception ex)
        {
            report($"Could not save the assistant settings: {ex.Message}", AssistantSettings.File);
        }

        Refresh();
    }

    private AssistantConfig? Configured()
    {
        if (assistant is null) return null;

        var key = credentials.Of(assistant.Id, assistant.Schema.EnvironmentVariable) ?? string.Empty;
        var model = ModelName();
        var url = string.IsNullOrWhiteSpace(baseUrlBox.Text) ? assistant.Schema.DefaultBaseUrl : baseUrlBox.Text;

        // What the model is recorded as refusing is not sent, whatever the box
        // still shows. The tick is a preference and this is a fact about the
        // model, so the fact wins for this run and the preference survives to
        // mean something again at the next model — see ShowModelState.
        var known = assistant.Schema.Known(model);

        // Hearing without an ear is nothing to turn on rather than something
        // that fails later: the tool would be offered, called, and answered with
        // a sentence saying nobody heard it.
        var ear = EarName();

        return new AssistantConfig(
            key,
            model,
            url,
            visionBox.IsChecked == true && known?.Vision != false,
            hearingBox.IsChecked == true && ear is not null,
            ear,
            (AssistantEffort)Math.Max(0, effortBox.SelectedIndex));
    }

    /// <summary>
    /// Reworks what the buttons and the footer say. The footer is the standing
    /// disclosure: it names what leaves the machine and where the key came from,
    /// and it is never hidden behind a dialog nobody reads twice.
    /// </summary>
    private void Refresh()
    {
        var config = Configured();
        var excuse = assistant is null
            ? "No assistant plugin is installed. See the status bar for where plugins are looked for."
            : config is null ? null : Excuse(assistant, config);

        blocked = excuse;
        ShowSendState();

        // On the box, since the box is what sending is done from now. The footer
        // says the same thing in amber a few pixels below, so nothing is lost to
        // anybody who never hovers.
        ToolTip.SetTip(instruction, excuse);
        ShowKeyState();

        if (excuse is not null)
        {
            footer.Text = excuse;
            footer.Foreground = Amber;
            return;
        }
        
        footer.Foreground = Dim;
    }

    /// <summary>
    /// The one button, in whichever of its two jobs applies. Reads and does not
    /// ask, because it runs on every keystroke.
    /// </summary>
    /// <remarks>
    /// Dead until there is something to send, which is the button saying what
    /// the panel already knew and never showed: an empty box or a missing key is
    /// why pressing Enter appeared to do nothing at all.
    /// </remarks>
    private void ShowSendState()
    {
        send.Content = asking ? StopGlyph : SendGlyph;

        send.IsEnabled = asking
            || (blocked is null && !string.IsNullOrWhiteSpace(instruction.Text));

        ToolTip.SetTip(send, asking
            ? "Stop — it ends at the next thing the assistant does"
            : blocked ?? "Ask  (Enter)");
    }

    // --- showing that it is working -----------------------------------------

    /// <summary>
    /// One tick of the beacon. Everything here is read rather than remembered,
    /// so a tick missed while the dispatcher was busy costs nothing.
    /// </summary>
    private void Beat()
    {
        if (!asking) return;

        // A cosine rather than a blink: something that fades is obviously alive
        // and does not compete with the transcript for attention, which a thing
        // switching on and off twice a second would.
        pulse++;
        beacon.Opacity = 0.25d + 0.75d * ((Math.Cos(pulse * Math.PI / 5d) + 1d) / 2d);

        var elapsed = DateTime.UtcNow - startedAt;
        var bench = run?.Workbench;

        var done = bench is null || bench.ToolCalls == 0
            ? string.Empty
            : $" · {Tally(bench.ToolCalls, "tool call")}, {Tally(bench.Edits, "edit")}";

        progress.Text = stopping
            ? $"Stopping — it ends at the next thing the assistant does · {Spell(elapsed)}"
            : $"Working · {Spell(elapsed)}{done}";
    }

    private void StartWorking()
    {
        asking = true;
        stopping = false;
        startedAt = DateTime.UtcNow;
        pulse = 0;

        working.IsVisible = true;
        heartbeat.Start();
        Beat();
    }

    private void StopWorking()
    {
        heartbeat.Stop();
        asking = false;
        stopping = false;
        working.IsVisible = false;
    }

    private static string Spell(TimeSpan elapsed) => elapsed.TotalSeconds < 60d
        ? $"{elapsed.TotalSeconds:0}s"
        : $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:00}s";

    private static string Tally(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    /// <summary>
    /// Says whether there is a key without ever showing one.
    /// </summary>
    /// <remarks>
    /// The three sources differ in what they promise and in what can be done
    /// about them, so each is named rather than reduced to a tick: one from the
    /// environment cannot be removed from here at all, and saying "forget it"
    /// under a button that would not is worse than saying nothing.
    /// </remarks>
    private void ShowKeyState()
    {
        if (assistant is null)
        {
            keyNote.Text = string.Empty;
            forget.IsEnabled = false;
            return;
        }

        var variable = assistant.Schema.EnvironmentVariable;
        var source = credentials.SourceOf(assistant.Id, variable);

        keyBox.PlaceholderText = source switch
        {
            CredentialSource.Environment => $"A key is set, from {variable}",
            CredentialSource.Kept => "A key is set, and kept",
            CredentialSource.Session => "A key is set, for this window",
            _ => "Paste a key",
        };

        var overruled = credentials.HasEntered(assistant.Id)
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

            _ => assistant.Schema.CredentialHelp,
        };

        // Nothing to forget, or nothing this could reach if it tried: an
        // environment variable is not this application's to remove.
        forget.IsEnabled = credentials.HasEntered(assistant.Id);
    }

    /// <summary>
    /// Which of the two happened, read back rather than assumed. ADR-0034's own
    /// warning is that "held for this run" and "saved" look identical until the
    /// next launch, and a program that appeared to save something and did not is
    /// worse than one that never offered.
    /// </summary>
    private void SayWhereTheKeyWent()
    {
        if (assistant is null) return;

        var source = credentials.SourceOf(assistant.Id, assistant.Schema.EnvironmentVariable);

        report(
            source switch
            {
                CredentialSource.Kept => $"Key saved, and kept by {credentials.Store?.Name}.",
                _ when !credentials.CanKeep =>
                    "Key saved, for this window only — nothing installed can keep one.",
                _ when settings.RememberKey =>
                    "Key saved, but it could not be kept — it will last this window only.",
                _ => "Key saved, for this window only.",
            },
            null);
    }

    private static string? Excuse(IPatchAssistant assistant, AssistantConfig config)
    {
        try
        {
            return assistant.Unavailable(config);
        }
        catch (Exception ex)
        {
            // Answering this must not throw. One that does has said no.
            return $"{assistant.Name} could not say whether it is ready: {ex.Message}";
        }
    }

    // --- asking -------------------------------------------------------------

    /// <summary>
    /// Why this message has to begin a new conversation, or null to carry the
    /// one already going.
    /// </summary>
    /// <remarks>
    /// Three reasons, and each of them is a conversation that could not honestly
    /// continue rather than a tidy-up. A run holds a copy of the patch it
    /// started from, so a patch edited underneath it would be quietly discarded
    /// by the next proposal. A run holds a session built around one model at one
    /// endpoint, so changed settings are a different correspondent. And a run
    /// has a turn budget, which is there to stop a conversation growing without
    /// end.
    /// </remarks>
    private string? Restarting(AssistantConfig config)
    {
        if (run is null) return string.Empty;
        if (run.Exhausted) return "That conversation had its turns. Starting another.";
        if (!ReferenceEquals(runAssistant, assistant) || runConfig != config)
            return "The settings changed, so this is a new conversation.";

        return run.EditedUnderneath(current())
            ? "The patch changed underneath, so this is a new conversation about the one on screen."
            : null;
    }

    /// <summary>
    /// The conversation this message belongs to, which is the one already going
    /// unless it cannot be.
    /// </summary>
    /// <remarks>
    /// Keeping it is the whole of what a second message is for: the history, the
    /// workbench with everything built in it, and whatever prefix cache the
    /// provider holds for the briefing. A panel that started again per message
    /// is one where "you did not propose it" reaches a model with no memory of
    /// having built anything, and it answers — correctly, and uselessly — that
    /// it has not designed a patch yet.
    /// </remarks>
    private AssistantRun Conversation(IPatchAssistant with, AssistantConfig config)
    {
        var because = Restarting(config);

        if (because is null && run is { } going) return going;

        run?.Dispose();
        run = new AssistantRun(
            with, config, plugins.Modules, current(), samples: samples, pictures: pictures);
        runConfig = config;
        runAssistant = with;

        // Said rather than silently done, and only where there was something to
        // lose: the transcript emptying is otherwise the only sign that the
        // thing being talked to has just been replaced.
        if (saidPanel.Children.Count > 0)
        {
            saidPanel.Children.Clear();
            if (because is { Length: > 0 }) Add(because, Dim, 11);
        }

        lastFrame.Source = null;
        lastFrame.IsVisible = false;

        return run;
    }

    private async Task AskAsync()
    {
        if (asking || assistant is null) return;
        if (Configured() is not { } config || Excuse(assistant, config) is not null) return;

        var wanted = instruction.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(wanted)) return;

        var conversation = Conversation(assistant, config);

        Asked(wanted);

        instruction.Text = string.Empty;

        StartWorking();
        Refresh();

        try
        {
            await foreach (var happened in conversation.Ask(wanted))
            {
                Show(happened);

                // The tallies move as the workbench is driven, and an edit that
                // lands is the strongest sign of all that this is alive.
                Beat();
            }

            Deliver();
        }
        catch (Exception ex)
        {
            // AssistantRun already turns a provider's failure into an event, so
            // anything arriving here is the shell's own fault rather than a
            // plugin's — but the window still survives it.
            Add($"Something went wrong: {ex.Message}", Amber, 11);
            report($"The assistant stopped: {ex.Message}", null);
        }
        finally
        {
            StopWorking();
            Refresh();
        }
    }

    private void Show(PatchEvent happened)
    {
        switch (happened)
        {
            case PatchEvent.Said said:
                Append(said.Text);
                break;

            case PatchEvent.Did did:
                Add(did.Summary, Dim, 11);
                break;

            case PatchEvent.Saw saw:
                Add(saw.Caption, Dim, 11);
                Picture(saw.Png);
                break;

            // The caption and nothing else. The WAV went to the model rather
            // than to the speakers, and a panel that started playing sound while
            // the patch under the cursor is already playing would be two things
            // at once — the transcript says a sound was rendered and heard,
            // which is what somebody watching this needs to know.
            case PatchEvent.Heard heard:
                Add(heard.Caption, Dim, 11);
                break;

            case PatchEvent.Cost cost:
                Add(
                    $"{cost.Input} in ({cost.CacheRead} cached), {cost.Output} out.",
                    Dim,
                    11);
                break;

            case PatchEvent.Proposed proposed:
                Add($"Proposed: {proposed.Summary}", Brushes.White, 12);
                break;

            case PatchEvent.Failed failed:
                Add(failed.Message, Amber, 11);
                break;
        }

        transcript.ScrollToEnd();
    }

    private void Picture(byte[] png)
    {
        try
        {
            lastFrame.Source = new Bitmap(new MemoryStream(png));
            lastFrame.IsVisible = true;
        }
        catch
        {
            // A frame that will not decode is not worth the window, and it does
            // not get to claim the width either. The caption that came with it
            // is already in the transcript.
        }
    }

    private void Add(string text, IBrush color, double size)
    {
        // Anything else in the transcript ends the paragraph the assistant was
        // in the middle of. Without this, prose lands on the end of whatever
        // block happens to be last and of about the right size — which was the
        // person's own message, run together with the reply to it.
        saying = null;

        saidPanel.Children.Add(new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = color,
            FontSize = size,
        });
    }

    /// <summary>
    /// What the person just asked for, set apart from what comes back.
    /// </summary>
    /// <remarks>
    /// A conversation is kept now rather than cleared per message, so the two
    /// sides have to be told apart by looking: a gap above, and the accent the
    /// rest of the panel uses for its own voice.
    /// </remarks>
    private void Asked(string text)
    {
        saying = null;

        saidPanel.Children.Add(new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, saidPanel.Children.Count > 0 ? 14 : 0, 0, 2),
        });
    }

    /// <summary>Streamed prose arrives in pieces, so it lands on the end of the last one.</summary>
    private void Append(string text)
    {
        if (saying is not null)
        {
            saying.Text += text;
            return;
        }

        saying = new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            FontSize = 12,
        };

        saidPanel.Children.Add(saying);
    }

    // --- accepting ----------------------------------------------------------

    /// <summary>
    /// Puts what the assistant made on the canvas, which is how every turn that
    /// made anything ends.
    /// </summary>
    /// <remarks>
    /// There is no button for it, and there is no button to take it back
    /// either. A proposal nobody applied is a proposal nobody can see, and
    /// because this arrives as an edit rather than as a new document, undo is
    /// the way back.
    /// <para>
    /// Editing the patch while a turn ran takes nothing to overrule: the
    /// edits are replaced, and saying so is enough, because one press of
    /// undo has them back.
    /// </para>
    /// </remarks>
    private void Deliver()
    {
        if (run?.Proposal is not { } proposed) return;

        // A turn somebody stopped is not one to act on. What it reached is in
        // the transcript, and asking again is a keystroke.
        if (stopping) return;

        var overwrote = run.EditedUnderneath(current());

        apply(proposed);

        // What this run just put on the canvas is not somebody editing behind
        // it, so the next message carries on the same conversation rather than
        // starting one about a patch it does not remember building.
        run.Rebase(proposed);

        Add(overwrote
            ? "Applied — this replaced the edits you made while it ran. Ctrl+Z puts them back."
            : "Applied. Ctrl+Z puts the patch back as it was.", Dim, 11);

        report(string.Empty, null);
    }
}
