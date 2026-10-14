using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Flyback.Ui.Controls;
using Shouldly;

namespace Flyback.Ui.Tests.Controls;

/// <summary>A glyph with no ink of its own draws in the foreground it sits in.</summary>
public class GlyphsTests
{
    private static Window Shown(Control content)
    {
        var window = new Window { Content = content };
        window.Show();

        return window;
    }

    /// <summary>Outside any button: a panel row or a header, with no presenter above it.</summary>
    [AvaloniaFact]
    public void A_glyph_outside_a_presenter_takes_the_foreground_around_it()
    {
        var glyph = (Path)Glyphs.Down(10);
        var row = new Border { Child = glyph };
        TextElement.SetForeground(row, Brushes.Orange);
        var window = Shown(row);

        glyph.Fill.ShouldBe(Brushes.Orange);
        window.Close();
    }

    /// <summary>A disabled button dims through its presenter's Foreground, so the glyph follows that.</summary>
    [AvaloniaFact]
    public void A_glyph_in_a_button_follows_the_presenter_it_is_drawn_through()
    {
        var glyph = (Path)Glyphs.Open();
        var button = new Button { Content = glyph };
        var window = Shown(button);
        var presenter = button.Presenter.ShouldNotBeNull();

        presenter.Foreground = Brushes.Teal;

        glyph.Stroke.ShouldBe(Brushes.Teal);
        window.Close();
    }
}
