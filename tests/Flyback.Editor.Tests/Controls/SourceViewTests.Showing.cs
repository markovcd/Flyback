using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using Flyback.Editor.Assist;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;
using Flyback.Editor.Knobs;
using Flyback.Editor.Windows;
using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Editor.Tests.Controls;

public partial class SourceViewTests
{
    // --- showing it ---------------------------------------------------------

    [AvaloniaFact]
    public void The_text_and_the_canvas_are_never_both_showing()
    {
        var window = Open();

        Editor(window).IsVisible.ShouldBeTrue();

        ShowCode(window);

        Editor(window).IsVisible.ShouldBeFalse();
        Text(window).IsVisible.ShouldBeTrue();

        CodeButton(window).IsChecked = false;
        Settle(window);

        Editor(window).IsVisible.ShouldBeTrue();
    }

    /// <summary>
    /// A patch that arrived as a graph stays one. The text is a printing of it,
    /// and says so above itself rather than letting somebody find out by losing
    /// their groups.
    /// </summary>
    [AvaloniaFact]
    public void A_preset_is_read_as_text_without_becoming_text()
    {
        var window = Open();
        var text = ShowCode(window);

        text.Text.ShouldNotBeNullOrWhiteSpace();

        // Still the graph's patch: nothing is locked, and the inspector still
        // turns knobs.
        Editor(window).History.Locked.ShouldBeFalse();
        Inspector(window).IsEnabled.ShouldBeTrue();

        Notice(window).ShouldNotBeNull().ShouldContain("still the document");
    }

    /// <summary>What the printing says for itself, or null when there is nothing shown.</summary>
    private static string? Notice(MainWindow window) => All<TextBlock>(window)
        .Where(block => block.IsVisible && block.Text is { } said && said.Contains("Printed from"))
        .Select(block => block.Text)
        .FirstOrDefault();

    /// <summary>
    /// Typing is not thrown away by a second look at the canvas. Somebody who
    /// switched over to check a wire and came back would otherwise find their
    /// work replaced by a printing of a patch they had not changed.
    /// </summary>
    [AvaloniaFact]
    public void Switching_away_and_back_keeps_what_was_typed()
    {
        var window = Open();

        ShowCode(window).Text = Hum;

        CodeButton(window).IsChecked = false;
        Settle(window);

        ShowCode(window).Text.ShouldBe(Hum);
    }

    /// <summary>
    /// The editor is a code editor rather than a box with text in it: a gutter
    /// to say which line a complaint is about, and the language colored so a
    /// module reads differently from the socket it is being handed. Neither can
    /// be had from a TextBox, which is why this costs a package.
    /// </summary>
    [AvaloniaFact]
    public void The_text_is_shown_as_code()
    {
        var window = Open();
        var text = ShowCode(window);

        text.ShowLineNumbers.ShouldBeTrue();

        text.SyntaxHighlighting.ShouldNotBeNull("the language's own definition should have loaded")
            .Name.ShouldBe("Flyback");
    }

    /// <summary>
    /// And the definition covers what the language actually has. Written by hand
    /// against docs/language.md, so this is what stops it drifting from the
    /// eight statement forms it is coloring.
    /// </summary>
    [AvaloniaFact]
    public void The_language_definition_names_the_words_the_language_has()
    {
        var window = Open();
        var colors = ShowCode(window).SyntaxHighlighting.ShouldNotBeNull();

        var named = colors.NamedHighlightingColors.Select(color => color.Name).ToList();

        named.ShouldContain("Comment");
        named.ShouldContain("Keyword");
        named.ShouldContain("Pipe");
        named.ShouldContain("Sink");
        named.ShouldContain("Socket");
    }
}
