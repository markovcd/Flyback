using Avalonia.Headless.XUnit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using Flyback.Editor.Hubs;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Controls;

/// <summary>Flyback.xshd colors each part of the language the text view shows.</summary>
public class SourceHighlightingTests
{
    [AvaloniaFact]
    public void The_language_colors_load() => SourceView.Language.ShouldNotBeNull();

    [AvaloniaTheory]
    [InlineData("let lead = saw()", "let", "Keyword")]
    [InlineData("def voice(note) = sine(freq: note)", "def", "Keyword")]
    [InlineData("group \"Bass\" {", "group", "Keyword")]
    [InlineData("off lead", "off", "Keyword")]
    [InlineData("let v = for k in 1..4 { sine() }", "for", "Keyword")]
    [InlineData("let v = for k in 1..4 { sine() }", "in", "Keyword")]
    [InlineData("let v = for k in [1, 2] { sine() }", "in", "Keyword")]
    [InlineData("let v = for (k, g) in [(A2, 1)] { sine() }", "in", "Keyword")]
    [InlineData("let v = for (k, g) in [1] { sine() }", "in", "Keyword")]
    [InlineData("for (k, g) in [1] { sine() }", "in", "Keyword")]
    [InlineData("let chord = for (pitch, gain) in [(A2, 0.5), (C#3, 0.3), (Bb3, 0.2)] {", "in", "Keyword")]
    [InlineData("panel cutoff = 0.4, label: \"Cutoff\"", "panel", "Keyword")]
    [InlineData("panel hit = 0, button, note: 36", "button", "Keyword")]
    [InlineData("panel drop = 0, switch, latching", "latching", "Keyword")]
    [InlineData("panel level = 0.8, held // kept", "held", "Keyword")]
    [InlineData("requires flyback.picture", "requires", "Keyword")]
    [InlineData("requires \"flyback.picture\"", "requires", "Keyword")]
    [InlineData("keyboard scale [ D F# A ]", "keyboard scale", "Keyword")]
    [InlineData("keyboard piano", "keyboard piano", "Keyword")]
    [InlineData("length 2:30", "length", "Keyword")]
    [InlineData("description \"A drone\"", "description", "Keyword")]
    [InlineData("tags \"slow\"", "tags", "Keyword")]
    [InlineData("sum.b <- sum * 0.94", "<-", "Pipe")]
    [InlineData("t |> sine()", "|>", "Pipe")]
    [InlineData("mixer(...voices)", "...", "Pipe")]
    [InlineData("pitch |> tone(0.5, _)", "_", "Pipe")]
    [InlineData("noise(-2..2)", "..", "Pipe")]
    [InlineData("adsr(attack: 4ms)", "4ms", "Number")]
    [InlineData("snap(time: 1.5s)", "1.5s", "Number")]
    [InlineData("sine(freq: note(A3))", "A3", "Number")]
    [InlineData("sine(freq: note(C#4)) |> out.left", "C#4", "Number")]
    [InlineData("sine(freq: note(Bb2))", "Bb2", "Number")]
    [InlineData("sine(freq: note(C#4)) |> out.left", "out.left", "Sink")]
    [InlineData("tune(in: _) [ C D# E ]", "D#", "Number")]
    [InlineData("tune(in: _) [ C D# E ]", "E", "Number")]
    [InlineData("notes() [ A3 ~ C4%0.6 ]", "~", "Comment")]
    [InlineData("notes() [ A3 ~ C4%0.6 ]", "%", "Pipe")]
    [InlineData("notes() [ A3(3,8) ]", "A3", "Number")]
    [InlineData("notes() [ <A3 C4> ]", "<", "Pipe")]
    [InlineData("arrangement(in: beats) [ 1 >1 | 0 ]", "|", "Pipe")]
    [InlineData("tune(in: _)", "in", "Socket")]
    [InlineData("let v = [(A2, 0.5), (E3, 0.3)]", "E3", "Number")]
    [InlineData("x |> sine(t: 0.5)", "x", "Domain")]
    [InlineData("x |> sine(t: 0.5)", "t", "Socket")]
    [InlineData("saw() // a C#4 here is a comment", "// a C#4 here is a comment", "Comment")]
    public void Each_part_of_a_line_takes_its_color(string line, string part, string color) =>
        ColorOf(line, part).ShouldBe(color);

    [AvaloniaTheory]
    [InlineData("def tone(in, level) = in", "in")]
    [InlineData("let tags = 3", "tags")]
    [InlineData("let author = 3 // \"x\"", "author")]
    [InlineData("t.length |> out.left", "length")]
    [InlineData("let A = 1", "A")]
    public void A_name_spelled_like_a_word_of_the_language_is_left_plain(string line, string part) =>
        ColorOf(line, part).ShouldBeNull();

    /// <summary>The color covering the first <paramref name="part"/> of <paramref name="line"/> whole, not inside a longer word, or null for none.</summary>
    private static string? ColorOf(string line, string part)
    {
        var document = new TextDocument(line);
        var highlighter = new DocumentHighlighter(document, SourceView.Language.ShouldNotBeNull());
        var found = Regex.Match(line, (char.IsLetterOrDigit(part[0]) ? @"(?<!\w)" : "") + Regex.Escape(part) + (char.IsLetterOrDigit(part[^1]) ? @"(?!\w)" : ""));

        found.Success.ShouldBeTrue();

        var start = found.Index;

        var section = highlighter.HighlightLine(1).Sections
            .LastOrDefault(s => s.Offset <= start && start + part.Length <= s.Offset + s.Length);

        return section?.Color.Name;
    }
}
