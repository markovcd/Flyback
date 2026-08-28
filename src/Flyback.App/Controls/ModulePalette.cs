using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// The list of modules that can be added, with a filter and a tick per plugin.
/// </summary>
/// <remarks>
/// <para>
/// A loop over the catalogue rather than markup, which is what makes a module
/// added anywhere — the engine or a plugin — appear here with no change to the
/// shell at all. Nothing in this file names a single module.
/// </para>
/// <para>
/// A control of its own rather than a region of the window, because it is shown
/// where it is asked for: right-clicking the canvas opens it at the pointer and
/// what is picked lands there. It used to be a column down the left, standing
/// open whether or not anything was being added, and the width of the canvas is
/// worth more than that — see ADR-0046.
/// </para>
/// </remarks>
public sealed class ModulePalette : UserControl
{
    /// <summary>How tall the list is allowed to get before it scrolls.</summary>
    private const double TallestList = 420;

    private const double PopupWidth = 180;

    /// <summary>How solid the list is over the patch it is being added to.</summary>
    public const double Translucency = 0.8;

    /// <summary>
    /// The class the flyout presenter holding this is given, so that
    /// <see cref="Trim"/> can find it. The presenter's own padding and border
    /// are sized for a menu of a few words and are far too much around a list
    /// that brings its own.
    /// </summary>
    public const string PresenterClass = "palette";

    private readonly ModuleCatalog catalog;
    private readonly Action<string> chosen;
    private readonly GroupLibrary groups;
    private readonly Action<SavedGroup> adding;

    /// <summary>
    /// Which kept group has been asked about, and so is showing a confirm in
    /// place of its row.
    /// </summary>
    /// <remarks>
    /// A row that turns into its own question rather than a dialog over the
    /// window: the list is a popup already, and a popup that puts a second thing
    /// over the window to ask about one line of itself is two layers deep for a
    /// yes. Held here rather than on the row, because the list is rebuilt on
    /// every keystroke and a row does not outlive one.
    /// </remarks>
    private SavedGroup? removing;

    private readonly StackPanel modules = new() { Margin = new Thickness(6, 0, 6, 6), Spacing = 2 };

    private readonly TextBox filter = new()
    {
        PlaceholderText = "Filter modules",
        Margin = new Thickness(0, 0, 0, 2),
        FontSize = 12,
    };

    /// <summary>
    /// Opens the list of plugins to show modules from. The engine's own are one
    /// of them, so a patch can be built out of nothing but plugins as readily as
    /// out of nothing but the engine.
    /// </summary>
    private readonly Button sources = new()
    {
        FontSize = 11.5,
        Padding = new Thickness(6, 2),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Left,
    };

    /// <summary>
    /// Providers whose modules are hidden. Stored as what is <em>off</em> rather
    /// than what is on, so a plugin installed later shows up ticked instead of
    /// having to be found and turned on.
    /// </summary>
    private readonly HashSet<string> hidden = [];

    /// <summary>
    /// The module buttons currently listed, in the order they are shown. The
    /// arrow keys walk this rather than the visual tree, which also holds the
    /// category headings and would step through them as though they were
    /// choices.
    /// </summary>
    private readonly List<Button> listed = [];

    /// <summary>
    /// Which of <see cref="listed"/> the arrows have reached, and what Enter
    /// would add. Kept on the palette rather than as the focused control,
    /// because the focus belongs to the filter box — typing has to keep
    /// narrowing the list while the arrows move through it.
    /// </summary>
    private int highlighted = -1;

    /// <param name="catalog">Every module that may be added, and which plugin each came from.</param>
    /// <param name="chosen">Called with the type id of whatever is picked.</param>
    /// <param name="groups">The groups somebody kept, which are listed above the catalogue.</param>
    /// <param name="adding">Called with the kept group that was picked.</param>
    public ModulePalette(
        ModuleCatalog catalog,
        Action<string> chosen,
        GroupLibrary groups,
        Action<SavedGroup> adding)
    {
        this.catalog = catalog;
        this.chosen = chosen;
        this.groups = groups;
        this.adding = adding;

        Width = PopupWidth;
        Name = "palette";

        // Slightly see-through, so the patch under it is still readable while
        // you are choosing what to add to it. On the whole control rather than
        // on its background, because a solid list of text over a translucent
        // panel reads as a mistake rather than as a decision.
        Opacity = Translucency;

        filter.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) Fill();
        };

        // Everything the keyboard does here is handled on the filter box,
        // because the filter box is what has the focus the whole time it is
        // open — the list is walked by the arrows without ever taking it, so
        // that typing keeps narrowing while the arrows move.
        //
        // Space is deliberately not one of these: it is a character, and the
        // box it would be typed into is right there.
        filter.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down:
                    Highlight(highlighted + 1);
                    break;

                case Key.Up:
                    Highlight(highlighted - 1);
                    break;

                case Key.Enter:
                    if (highlighted >= 0 && highlighted < listed.Count)
                        listed[highlighted].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    break;

                // Empties the box, which is the quickest way back to the whole
                // list once you have found the module you were after. An empty
                // box lets it through instead, so a second press closes the
                // popup — one key, and it always takes a step back.
                case Key.Escape when filter.Text is { Length: > 0 }:
                    filter.Text = string.Empty;
                    break;

                default:
                    return;
            }

            e.Handled = true;
        };

        // Escape from anywhere in the list, and not only from the filter box:
        // the ✕ that asks whether to remove a kept group takes the focus when it
        // is clicked, so the key that means "never mind" arrives at a button
        // rather than at the box above it.
        //
        // Every row that turned into a question goes back to being a row. A
        // question nobody answered must not still be on the list the next time
        // it is opened, and Escape is how a question is not answered.
        //
        // Deliberately not handled here, and deliberately not a step of its own.
        // Escape goes on to mean exactly what it always meant — empty the box,
        // or, with the box already empty, let the popup close on it. A key that
        // needed pressing twice to leave, because a row somewhere was
        // mid-question, would have stopped being the way out.
        //
        // handledEventsToo, because the filter box marks Escape handled when it
        // empties itself, and a question left standing behind a cleared filter
        // is the very thing this is here to prevent.
        AddHandler(
            KeyDownEvent,
            (_, e) =>
            {
                if (e.Key != Key.Escape || removing is null) return;

                Asking(null);
            },
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        var ticks = new StackPanel { Spacing = 2, Margin = new Thickness(4) };

        foreach (var provider in catalog.Providers)
        {
            var id = provider.Id;
            var tick = new CheckBox { Content = provider.Name, IsChecked = true, FontSize = 12 };

            tick.IsCheckedChanged += (_, _) =>
            {
                if (tick.IsChecked == true) hidden.Remove(id); else hidden.Add(id);

                DescribeSources();
                Fill();
            };

            ticks.Children.Add(tick);
        }

        sources.Flyout = new Flyout { Content = ticks, Placement = PlacementMode.BottomEdgeAlignedLeft };

        DescribeSources();
        Fill();

        var header = new StackPanel { Spacing = 4, Margin = new Thickness(6, 6, 6, 0) };
        header.Children.Add(filter);

        // With nothing installed there is only the engine's own entry, and a
        // dropdown that can only say "all" or "the only one" is noise.
        if (catalog.Providers.Count > 1) header.Children.Add(sources);

        // The header stays put while the list beneath it scrolls; a filter that
        // scrolled away with the results would be the wrong way round.
        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);

        panel.Children.Add(header);
        panel.Children.Add(new ScrollViewer
        {
            Content = modules,
            MaxHeight = TallestList,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        // The list brings its own edge, because the presenter's is given away by
        // Trim below — one border rather than two boxes a few pixels apart.
        Content = new Border
        {
            Background = new SolidColorBrush(Colors.Panel),
            BorderBrush = new SolidColorBrush(Colors.Edge),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = panel,
        };
    }

    /// <summary>
    /// Takes the padding and the border off the flyout presenter that holds one
    /// of these, and stops it painting a background of its own.
    /// </summary>
    /// <remarks>
    /// A presenter is dressed for a menu of a few words: sixteen pixels of
    /// padding all round, a border and a corner radius. Around a list that is
    /// already a panel with its own margins that reads as a wide empty frame,
    /// and its opaque background would sit behind the translucency rather than
    /// under it. So the presenter gives up all three and the list keeps them.
    /// <para>
    /// A style rather than properties on the flyout, because a presenter is made
    /// by the flyout when it opens and there is nothing to set them on until
    /// then. Added to the window's own styles, so it reaches the popup wherever
    /// that ends up in the tree.
    /// </para>
    /// </remarks>
    public static Style Trim()
    {
        var style = new Style(x => x.OfType<FlyoutPresenter>().Class(PresenterClass));

        style.Setters.Add(new Setter(TemplatedControl.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4)));

        return style;
    }

    /// <summary>
    /// Puts the list back to the whole of it and takes the keyboard, so that
    /// opening it and typing narrows it — which is the fast way to reach a
    /// module and the only reason the filter is the first thing in it.
    /// </summary>
    public void Reset()
    {
        // Read off the disk again on the way open, which is the only moment
        // anybody could notice it happening. So a group kept a minute ago is on
        // the list — the panel that keeps one is behind this popup and cannot
        // reach in to say so — and so is a patch file dropped into the folder by
        // hand while the program was running.
        groups.Reload();
        removing = null;

        filter.Text = string.Empty;

        // Explicitly, because emptying a box that was already empty raises
        // nothing, and the list would then be the one the last visit left —
        // filtered, or with a row still asking whether to remove itself.
        Fill();

        filter.Focus();
        filter.SelectAll();

        Highlight(0);
    }

    /// <summary>
    /// Puts the list into — or out of — asking whether to remove a kept group,
    /// and hands the keyboard back to the filter box.
    /// </summary>
    /// <remarks>
    /// The focus is the part that is easy to miss. Everything the keyboard does
    /// here is handled on the filter box because the filter box is what holds
    /// the focus the whole time the list is open, and a row's ✕ takes it away by
    /// being clicked — then the rebuild below deletes the very button holding
    /// it, leaving the focus nowhere at all. A list with the focus nowhere
    /// answers no keys: not the arrows, not Enter, and not Escape, which is the
    /// way out.
    /// </remarks>
    private void Asking(SavedGroup? entry)
    {
        removing = entry;

        Fill();
        filter.Focus();
    }

    /// <summary>
    /// Rebuilds the module list for whatever is in the filter box. Rebuilding
    /// rather than hiding buttons keeps the category headings honest: a heading
    /// with nothing under it is worse than no heading.
    /// </summary>
    private void Fill()
    {
        var text = filter.Text?.Trim() ?? string.Empty;
        var matches = catalog.All.Where(d => Matches(d, text)).ToList();
        var kept = groups.All.Where(entry => Matches(entry, text)).ToList();

        modules.Children.Clear();
        listed.Clear();
        highlighted = -1;

        if (matches.Count == 0 && kept.Count == 0)
        {
            modules.Children.Add(Hint(
                hidden.Count == catalog.Providers.Count ? "No plugins are ticked."
                : text.Length == 0 ? "Nothing to show."
                : $"Nothing matches “{text}”."));
            return;
        }

        // Above the catalogue rather than below it. There are a handful of these
        // and hundreds of modules, they are the only things in the list somebody
        // made themselves, and a section of one's own things under a screenful
        // of everything else is a section nobody scrolls to. The heading takes
        // no accent color because a group has no category — the same reason a
        // box's header is the one color on the canvas that means nothing.
        if (kept.Count > 0)
        {
            modules.Children.Add(Heading("GROUPS", Colors.Muted));

            foreach (var entry in kept) modules.Children.Add(Kept(entry));
        }

        foreach (var category in matches.Select(d => d.Category).Distinct())
        {
            modules.Children.Add(Heading(category.ToUpperInvariant(), Colors.Accent(category)));

            foreach (var def in matches.Where(d => d.Category == category))
            {
                var button = new Button
                {
                    Content = def.Name,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 4),
                    FontSize = 12,
                };

                // Naming the plugin only where it is not the engine keeps the
                // built-in modules reading as they always did, and tells you
                // which patches will need something installed to open.
                var from = catalog.ProviderOf(def.TypeId);
                var origin = from is null || from.Id == NodeCatalog.BuiltInProvider.Id
                    ? string.Empty
                    : $"{Environment.NewLine}{Environment.NewLine}From {from.Name} ({from.Id})";

                var tip = def.Description + origin;
                if (tip.Length > 0) ToolTip.SetTip(button, tip);

                var typeId = def.TypeId;
                button.Click += (_, _) => chosen(typeId);

                modules.Children.Add(button);
                listed.Add(button);
            }
        }

        // The first match, so that typing a few letters and pressing Enter adds
        // what you were after without a single arrow key. That is the fast path,
        // and there is no sense in which the list has no answer yet.
        Highlight(0);
    }

    /// <summary>
    /// Moves the arrow-key highlight, clamped to the ends of the list rather
    /// than wrapping — a list that jumps from bottom to top loses the reader's
    /// place, and holding Down to reach the end is a thing people do.
    /// </summary>
    private void Highlight(int index)
    {
        if (listed.Count == 0)
        {
            highlighted = -1;
            return;
        }

        var wanted = Math.Clamp(index, 0, listed.Count - 1);

        if (highlighted >= 0 && highlighted < listed.Count)
            listed[highlighted].ClearValue(BackgroundProperty);

        highlighted = wanted;
        listed[highlighted].Background = new SolidColorBrush(Colors.Attention, 0.28);
        listed[highlighted].BringIntoView();
    }

    /// <summary>Says what the ticks add up to, so the state is readable without opening them.</summary>
    private void DescribeSources()
    {
        var providers = catalog.Providers;
        var showing = providers.Count - hidden.Count;

        sources.Content = showing switch
        {
            _ when hidden.Count == 0 => "All modules  ▾",
            0 => "No plugins  ▾",
            1 => $"{providers.First(p => !hidden.Contains(p.Id)).Name}  ▾",
            _ => $"{showing} of {providers.Count} plugins  ▾",
        };
    }

    /// <summary>
    /// One kept group: the button that adds it, and the ✕ that asks whether to
    /// forget it.
    /// </summary>
    /// <remarks>
    /// The ✕ is the one the group inspector puts on a socket that can come off
    /// the edge, in the same place at the same weight, because it is the same
    /// offer — the row it is on can go. Asked rather than done, since a kept
    /// group is a file and there is no undo out here to take one back with.
    /// <para>
    /// An entry this build cannot make is still listed, and dimmed. It is a real
    /// thing somebody kept; what happens when it is picked is a sentence naming
    /// the plugin it wants, which is what opening such a patch would say.
    /// </para>
    /// </remarks>
    private Control Kept(SavedGroup entry)
    {
        if (removing is { } asked && asked.Path == entry.Path) return Confirm(entry);

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 1) };

        var forget = new Button
        {
            Content = "✕",
            FontSize = 10,
            Padding = new Thickness(5, 0, 5, 0),
            Background = Brushes.Transparent,
            Opacity = 0.55,
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(forget, $"Take “{entry.Name}” off this list.");

        forget.Click += (_, _) => Asking(entry);

        DockPanel.SetDock(forget, Dock.Right);
        row.Children.Add(forget);

        var add = new Button
        {
            Content = entry.Name,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 4),
            FontSize = 12,
            Opacity = entry.IsComplete ? 1 : 0.55,
        };

        ToolTip.SetTip(add, entry.IsComplete
            ? entry.Modules == 1 ? "1 module." : $"{entry.Modules} modules, drawn as one box."
            : entry.Load.Summary);

        add.Click += (_, _) => adding(entry);

        row.Children.Add(add);
        listed.Add(add);

        return row;
    }

    /// <summary>
    /// The row a kept group's ✕ turns into: the question and its two answers, on
    /// the one line the row was taking anyway.
    /// </summary>
    /// <remarks>
    /// In the list rather than over the window. This is a popup already, and a
    /// dialog put over the whole shell to ask about one line of it would be two
    /// layers deep for a yes — as well as taking the popup down on the way,
    /// since a flyout closes when something else takes the focus.
    /// <para>
    /// One line and not two, so the list does not jump under the hand that has
    /// just reached for a row: the question stands exactly where the row was, at
    /// the height it was, and the answers are where the ✕ that asked it is. Which
    /// is also why they are a ✔ and a ✕ rather than two words — the ✕ is the one
    /// already under the pointer, and it now means what it always meant on this
    /// row, which is "no, put it back".
    /// </para>
    /// </remarks>
    private Control Confirm(SavedGroup entry)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 1) };

        var answers = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };

        answers.Children.Add(Answer("✔", $"Remove “{entry.Name}”.", 1, () =>
        {
            // A file that will not go stays on the list, which is the truth
            // about it and better than a row that vanishes and comes back the
            // next time the folder is read.
            try
            {
                groups.Remove(entry);
            }
            catch (Exception)
            {
                // Nothing to say it with out here — see the remarks above. The
                // row still being there is what says it.
            }

            Asking(null);
        }));

        answers.Children.Add(Answer("✕", "Keep it.", 0.55, () => Asking(null)));

        DockPanel.SetDock(answers, Dock.Right);
        row.Children.Add(answers);

        row.Children.Add(new TextBlock
        {
            Text = $"Remove “{entry.Name}”?",
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0),
        });

        return row;

        Button Answer(string glyph, string tip, double strength, Action taken)
        {
            var button = new Button
            {
                Content = glyph,
                FontSize = 10,
                Padding = new Thickness(5, 0, 5, 0),
                Background = Brushes.Transparent,
                Opacity = strength,
                VerticalAlignment = VerticalAlignment.Center,
            };

            ToolTip.SetTip(button, tip);
            button.Click += (_, _) => taken();

            return button;
        }
    }

    private static TextBlock Heading(string text, Color color) => new()
    {
        Text = text,
        FontSize = 10.5,
        FontWeight = FontWeight.SemiBold,
        Foreground = new SolidColorBrush(color),
        Margin = new Thickness(2, 12, 2, 4),
    };

    /// <summary>
    /// A kept group matches on its name, which is all it has: the modules inside
    /// are its business rather than the list's, and matching them would put
    /// "Voice" under a search for "sine" without saying why.
    /// </summary>
    private static bool Matches(SavedGroup entry, string text) =>
        text.Length == 0 || entry.Name.Contains(text, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Text matches name, category and type id, but deliberately not the
    /// description: every module has a sentence of prose, and matching it turns a
    /// search for a common word into most of the catalogue. The ticks narrow
    /// separately, so the two combine rather than compete.
    /// </summary>
    private bool Matches(NodeDef def, string text)
    {
        // The Output is never listed. Every patch has one already and cannot
        // have a second, so a button for it could only ever select the one that
        // is there — and a palette entry that never adds anything is a puzzle.
        if (NodeCatalog.IsSink(def.TypeId)) return false;

        if (catalog.ProviderOf(def.TypeId) is { } from && hidden.Contains(from.Id)) return false;

        return text.Length == 0
            || def.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || def.Category.Contains(text, StringComparison.OrdinalIgnoreCase)
            || def.TypeId.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 11,
        Opacity = 0.55,
        Margin = new Thickness(2, 8, 2, 0),
    };
}
