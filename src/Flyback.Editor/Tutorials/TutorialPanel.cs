using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Editor.Canvas;
using Flyback.Editor.Gallery;
using Flyback.Editor.Notices;
using Flyback.Engine.Tutorials;
using Flyback.Plugins.Hosting;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Tutorials;

/// <summary>
/// The column that walks somebody through a tutorial: the steps, what to do now, and what is still
/// missing, checked against the patch after every edit (ADR-0196).
/// </summary>
internal sealed class TutorialPanel : UserControl, IReactTo<PatchChanged>
{
    private static readonly IBrush Chrome = new ImmutableSolidColorBrush(Colors.Panel);
    private static readonly IBrush Rule = new ImmutableSolidColorBrush(Colors.Edge);
    private static readonly IBrush Ink = new ImmutableSolidColorBrush(Colors.Label);
    private static readonly IBrush Amber = new ImmutableSolidColorBrush(Colors.Attention);
    private static readonly IBrush Met = new ImmutableSolidColorBrush(Colors.Oscillator);
    private static readonly IBrush Current = new ImmutableSolidColorBrush(Colors.NodeSelected);

    private readonly NodeEditor editor;
    private readonly PresetSlot presets;
    private readonly PluginCatalog plugins;

    private readonly StackPanel list = new() { Name = "tutorial-list", Spacing = 8, Margin = new Thickness(12) };
    private readonly StackPanel lesson = new() { Name = "tutorial-lesson", Spacing = 10, Margin = new Thickness(12) };
    private readonly ContentControl body = new();
    private readonly TextBlock heading = new() { FontSize = Text.Emphasis, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button all = new() { Name = "tutorial-all", Content = "All tutorials", FontSize = Text.Small, IsVisible = false };

    private bool[] met = [];

    /// <param name="presets">Opens the preset a tutorial starts on, asking first about unsaved work.</param>
    public TutorialPanel(NodeEditor editor, PresetSlot presets, PluginCatalog plugins)
    {
        this.editor = editor;
        this.presets = presets;
        this.plugins = plugins;

        all.Click += (_, _) => Leave();
        Content = Build();
        ShowList();
    }

    /// <summary>The tutorial being taken, or null while the list is showing.</summary>
    public Tutorial? Taking { get; private set; }

    /// <summary>Which step is being worked on, counting from 0; the step count once the last is done.</summary>
    public int Step { get; private set; }

    /// <summary>What the patch still lacks for the step being worked on, or null where it lacks nothing or nothing is checked.</summary>
    public string? Hint { get; private set; }

    /// <summary>Whether each step of <see cref="Taking"/> has been done.</summary>
    public IReadOnlyList<bool> Done => met;

    public Task On(PatchChanged notice)
    {
        if (Taking is not null) Check();
        return Task.CompletedTask;
    }

    /// <summary>Opens the tutorial's starting preset and takes it from its first step; false where the preset was not opened.</summary>
    public async Task<bool> StartAsync(Tutorial tutorial)
    {
        if (!await presets.OpenAsync(tutorial.From)) return false;

        Taking = tutorial;
        Step = 0;
        met = new bool[tutorial.Steps.Count];
        Check();

        return true;
    }

    /// <summary>Moves on past the step being worked on, done or not.</summary>
    public void Next()
    {
        if (Taking is null || Step >= Taking.Steps.Count) return;

        if (Taking.Steps[Step].Goal is null) met[Step] = true;
        Step++;
        Check();
    }

    /// <summary>Goes back a step, to read it again or do it again.</summary>
    public void Back()
    {
        if (Taking is null || Step == 0) return;

        Step--;
        met[Step] = false;
        Check(advance: false);
    }

    /// <summary>Puts the tutorial down and shows the list again; the patch stays as it is.</summary>
    public void Leave()
    {
        Taking = null;
        Hint = null;
        ShowList();
    }

    /// <summary>Ticks off the step being worked on, and any after it, while the patch already does what each asks.</summary>
    /// <param name="advance">False to stay on a step gone back to, until the patch next changes.</param>
    private void Check(bool advance = true)
    {
        var tutorial = Taking!;
        Hint = null;

        while (Step < tutorial.Steps.Count && tutorial.Steps[Step].Goal is { } goal)
        {
            var verdict = StepCheck.Against(editor.History.Patch, goal, plugins.Modules);

            if (!verdict.Done || !advance)
            {
                Hint = verdict.Hint;
                break;
            }

            met[Step] = true;
            Step++;
        }

        ShowLesson(tutorial);
    }

    private Control Build()
    {
        var mark = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Colors.Faded(Colors.Attention, 0.14)),
            Child = Glyphs.Lesson(),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10, Margin = new Thickness(12, 9, 6, 9) };
        header.Children.Add(mark);
        header.Children.Add(heading);
        header.Children.Add(all);
        Grid.SetColumn(heading, 1);
        Grid.SetColumn(all, 2);

        var head = new Border { Child = header, Background = Chrome, BorderBrush = Rule, BorderThickness = new Thickness(0, 0, 0, 1) };
        DockPanel.SetDock(head, Dock.Top);

        var dock = new DockPanel { Background = new SolidColorBrush(Colors.Window) };
        dock.Children.Add(head);
        dock.Children.Add(new ScrollViewer { Content = body });

        return new Border { BorderBrush = Rule, BorderThickness = new Thickness(0, 1, 0, 0), MinHeight = 140, Child = dock };
    }

    private void ShowList()
    {
        heading.Text = "Tutorials";
        all.IsVisible = false;
        list.Children.Clear();
        list.Children.Add(Words("Each one opens on a preset and checks your patch as you build it, ticking off a step once it is done."));

        foreach (var tutorial in ShippedTutorials.All)
        {
            var card = new StackPanel { Spacing = 3 };
            card.Children.Add(new TextBlock { Text = tutorial.Title, FontSize = Text.Body, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White });
            card.Children.Add(Words(tutorial.Summary));
            card.Children.Add(Text.Quiet($"{tutorial.Steps.Count} steps, from {tutorial.From}", Text.Caption));

            var start = new Button
            {
                Name = $"tutorial-{tutorial.Id}",
                Content = card,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 8),
            };
            start.Click += async (_, _) => await StartAsync(tutorial);
            list.Children.Add(start);
        }

        body.Content = list;
    }

    private void ShowLesson(Tutorial tutorial)
    {
        heading.Text = tutorial.Title;
        all.IsVisible = true;
        lesson.Children.Clear();

        var steps = new StackPanel { Name = "tutorial-steps", Spacing = 2 };

        for (var i = 0; i < tutorial.Steps.Count; i++)
        {
            var row = new Border
            {
                Background = i == Step ? Current : Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3),
                Child = new TextBlock
                {
                    Text = $"{(met[i] ? "✓" : $"{i + 1}.")}  {tutorial.Steps[i].Title}",
                    FontSize = Text.Small,
                    Foreground = met[i] ? Met : i == Step ? Brushes.White : Text.Muted,
                },
            };
            steps.Children.Add(row);
        }

        lesson.Children.Add(steps);

        if (Step < tutorial.Steps.Count)
        {
            var step = tutorial.Steps[Step];
            lesson.Children.Add(new TextBlock { Name = "tutorial-step", Text = step.Title, FontSize = Text.Heading, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
            lesson.Children.Add(Words(step.Text));

            if (Hint is { } hint)
                lesson.Children.Add(new TextBlock { Name = "tutorial-hint", Text = $"Still to do: {hint}", FontSize = Text.Small, Foreground = Amber, TextWrapping = TextWrapping.Wrap });
        }
        else
        {
            lesson.Children.Add(new TextBlock { Name = "tutorial-step", Text = "Done", FontSize = Text.Heading, FontWeight = FontWeight.SemiBold, Foreground = Met });
            lesson.Children.Add(Words("Every step is done. The patch is yours: keep it, change it, or pick another tutorial."));
        }

        var back = new Button { Name = "tutorial-back", Content = "Back", IsEnabled = Step > 0 };
        back.Click += (_, _) => Back();

        var finished = Step >= tutorial.Steps.Count;
        var next = new Button
        {
            Name = "tutorial-next",
            Content = finished ? "All tutorials" : tutorial.Steps[Step].Goal is null ? "Next" : "Skip",
        };
        next.Click += (_, _) =>
        {
            if (finished) Leave();
            else Next();
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(back);
        buttons.Children.Add(next);
        lesson.Children.Add(buttons);

        body.Content = lesson;
    }

    private static TextBlock Words(string text) => new()
    {
        Text = text,
        FontSize = Text.Body,
        Foreground = Ink,
        TextWrapping = TextWrapping.Wrap,
    };
}
