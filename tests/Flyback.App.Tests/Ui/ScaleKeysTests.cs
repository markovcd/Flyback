using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The keyboard a quantiser's scale is edited on: twelve switches laid out as an
/// octave, not a list and not twelve sockets.
/// </summary>
/// <remarks>
/// The layout is the part worth testing rather than the part worth taking on
/// trust. A scale of twelve toggles is only readable if the keys are where an
/// eye expects them — sharps above and between the naturals, and a gap where
/// E meets F and B meets C — and every structural assertion about "twelve
/// buttons that toggle" would pass just as well on a row of twelve.
/// </remarks>
public class ScaleKeysTests : UiTest
{
    private static readonly int[] Major = [0, 2, 4, 5, 7, 9, 11];

    private static Window Open(out NodeInstance node, out Func<int> edits)
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.QuantiserTypeId);
        var built = NodeInstance.Create(def, 0, 0);
        var count = 0;

        node = built;
        edits = () => count;

        return Show(new ScaleKeys(built, def, _ => count++).View);
    }

    /// <summary>Every key, in the order the canvas holds them: seven naturals then five sharps.</summary>
    private static Button[] Keys(Window window) =>
        [.. All<Button>(window).Where(b => b.Classes.Contains(ScaleKeys.KeyTag))];

    /// <summary>The key for one pitch class, found by the name written on it.</summary>
    private static Button Key(Window window, int pitchClass) =>
        Keys(window).Single(b =>
            b.Content is TextBlock text && text.Text == Pitch.ClassName(pitchClass));

    private static void Press(Button button)
    {
        button.Focus();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [AvaloniaFact]
    public void There_is_one_key_for_each_note_of_the_octave()
    {
        var window = Open(out _, out _);

        Keys(window).Length.ShouldBe(Pitch.Classes);

        // Named, and each name used once — which is what says the twelve are
        // twelve different notes rather than twelve buttons.
        Keys(window)
            .Select(b => ((TextBlock)b.Content!).Text)
            .Distinct()
            .Count()
            .ShouldBe(Pitch.Classes);
    }

    [AvaloniaFact]
    public void A_new_quantiser_opens_on_its_major_scale()
    {
        Open(out var node, out _);

        node.Scale.ShouldBe(Major);
    }

    [AvaloniaFact]
    public void Pressing_a_key_that_is_off_turns_it_on()
    {
        var window = Open(out var node, out var edits);

        node.Scale.ShouldNotBeNull().ShouldNotContain(1);

        Press(Key(window, 1));

        node.Scale.ShouldNotBeNull().ShouldContain(1);
        edits().ShouldBe(1);
    }

    [AvaloniaFact]
    public void Pressing_a_key_that_is_on_turns_it_off()
    {
        var window = Open(out var node, out _);

        node.Scale.ShouldNotBeNull().ShouldContain(4);

        Press(Key(window, 4));

        node.Scale.ShouldNotBeNull().ShouldNotContain(4);
    }

    /// <summary>
    /// A set, so what the keys add up to is in ascending order however they were
    /// pressed — two scales with the same notes on have to be the same scale.
    /// </summary>
    [AvaloniaFact]
    public void The_scale_stays_a_tidy_set_whatever_order_the_keys_are_pressed()
    {
        var window = Open(out var node, out _);

        node.Scale!.Clear();

        Press(Key(window, 7));
        Press(Key(window, 0));
        Press(Key(window, 4));

        node.Scale.ShouldBe([0, 4, 7]);
    }

    /// <summary>
    /// Turning every key off is a state somebody may want — the module becomes a
    /// wire — so nothing here stops the last one going.
    /// </summary>
    [AvaloniaFact]
    public void Every_key_may_be_turned_off()
    {
        var window = Open(out var node, out _);

        foreach (var pitchClass in Major) Press(Key(window, pitchClass));

        node.Scale.ShouldBeEmpty();
    }

    /// <summary>
    /// The layout is the whole of what makes twelve switches readable. A sharp
    /// sits above the join between two naturals and is shorter than they are;
    /// the pairs with no sharp between them, E–F and B–C, are what give the
    /// keyboard the gaps an eye navigates by.
    /// </summary>
    [AvaloniaFact]
    public void The_keys_are_laid_out_as_an_octave()
    {
        var window = Open(out _, out _);

        var naturals = Major.Select(p => Key(window, p)).ToArray();
        var sharps = Enumerable.Range(0, Pitch.Classes)
            .Where(p => !Major.Contains(p))
            .Select(p => Key(window, p))
            .ToArray();

        sharps.Length.ShouldBe(5);

        foreach (var sharp in sharps)
            sharp.Height.ShouldBeLessThan(naturals[0].Height);

        // The naturals run left to right in pitch order and none of them
        // overlaps the next.
        var lefts = naturals.Select(Canvas.GetLeft).ToArray();

        for (var i = 1; i < lefts.Length; i++)
            lefts[i].ShouldBeGreaterThan(lefts[i - 1]);

        // And each sharp sits between the natural below it and the one above,
        // which is the arrangement rather than a row of twelve.
        foreach (var pitchClass in Enumerable.Range(0, Pitch.Classes).Where(p => !Major.Contains(p)))
        {
            var below = Canvas.GetLeft(Key(window, pitchClass - 1));
            var above = Canvas.GetLeft(Key(window, pitchClass + 1));
            var sharp = Canvas.GetLeft(Key(window, pitchClass));

            sharp.ShouldBeGreaterThan(below);
            sharp.ShouldBeLessThan(above);
        }
    }

    /// <summary>
    /// A key that is on has to look different from one that is off, or the
    /// control says nothing at all. The colours are what the panel is for.
    /// </summary>
    [AvaloniaFact]
    public void A_key_that_is_on_is_painted_differently_from_one_that_is_off()
    {
        var window = Open(out _, out _);

        var on = Key(window, 0);
        var off = Key(window, 1);

        on.Background.ShouldNotBe(off.Background);

        Press(on);

        // And the paint follows the state rather than the press: the key that
        // was on now matches the one that was always off.
        on.Background.ShouldBe(off.Background);
    }

    /// <summary>
    /// The two ends of the range are where the module stops being a quantiser,
    /// and both look from the keys alone like an ordinary scale that happens to
    /// be full or empty — so the panel says so in words.
    /// </summary>
    [AvaloniaFact]
    public void The_line_underneath_says_what_the_scale_adds_up_to()
    {
        var window = Open(out var node, out _);

        Summary(window).ShouldContain("7 notes");

        foreach (var pitchClass in Enumerable.Range(0, Pitch.Classes))
            if (!node.Scale!.Contains(pitchClass))
                Press(Key(window, pitchClass));

        Summary(window).ShouldContain("nearest semitone");

        foreach (var pitchClass in Enumerable.Range(0, Pitch.Classes))
            Press(Key(window, pitchClass));

        Summary(window).ShouldContain("passes straight through");

        static string Summary(Window window) =>
            All<TextBlock>(window)
                .Select(t => t.Text ?? string.Empty)
                .First(t => t.Length > 40);
    }

    /// <summary>
    /// All and None are the two scales worth a button: one is the nearest
    /// semitone and the other is a wire, and reaching either by hand is twelve
    /// presses.
    /// </summary>
    [AvaloniaFact]
    public void All_and_None_switch_every_key_at_once()
    {
        var window = Open(out var node, out _);

        Press(Shortcut(window, "All"));
        node.Scale!.Count.ShouldBe(Pitch.Classes);

        Press(Shortcut(window, "None"));
        node.Scale.ShouldBeEmpty();

        static Button Shortcut(Window window, string label) =>
            All<Button>(window).Single(b =>
                !b.Classes.Contains(ScaleKeys.KeyTag) && (b.Content as string) == label);
    }
}
