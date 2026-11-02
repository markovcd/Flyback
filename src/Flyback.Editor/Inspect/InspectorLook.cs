using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Inspect;

/// <summary>
/// How the inspector's rows are set: one height for every row, thin bar thumbs on hairline
/// tracks, and each value a reading in amber mono rather than a box (ADR-0219).
/// </summary>
internal static class InspectorLook
{
    /// <summary>How tall a value's field stands, which sets how tight the rows are.</summary>
    public const double FieldHeight = 24;

    public static void Apply(StyledElement panel)
    {
        panel.Resources["SliderHorizontalThumbWidth"] = 4d;
        panel.Resources["SliderHorizontalThumbHeight"] = 14d;
        panel.Resources["SliderThumbCornerRadius"] = new CornerRadius(1);
        panel.Resources["SliderTrackThemeHeight"] = 2d;
        panel.Resources["SliderHorizontalHeight"] = FieldHeight;
        panel.Resources["SliderPreContentMargin"] = new GridLength(5);
        panel.Resources["SliderPostContentMargin"] = new GridLength(5);

        var reading = new SolidColorBrush(Colors.Attention);

        var slider = new Style(x => x.OfType<Slider>());
        slider.Setters.Add(new Setter(Layoutable.MinHeightProperty, 0d));

        var number = new Style(x => x.OfType<NumericUpDown>());
        number.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent));
        number.Setters.Add(new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)));
        number.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, reading));
        number.Setters.Add(new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Right));
        number.Setters.Add(new Setter(Layoutable.MinHeightProperty, FieldHeight));
        number.Setters.Add(new Setter(Layoutable.HeightProperty, FieldHeight));
        number.Setters.Add(new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center));

        // The theme's box inside it sets its own height, fill and padding.
        var field = new Style(x => x.OfType<NumericUpDown>().Template().OfType<TextBox>());
        field.Setters.Add(new Setter(Layoutable.MinHeightProperty, 0d));
        field.Setters.Add(new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)));
        field.Setters.Add(new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        field.Setters.Add(new Setter(TemplatedControl.PaddingProperty, new Thickness(4, 0, 2, 0)));
        field.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent));
        field.Setters.Add(new Setter(TextBox.TextAlignmentProperty, TextAlignment.Right));

        // Every row on the panel stands as tall as a knob's, so a wired row keeps the rhythm.
        var row = new Style(x => x.OfType<StackPanel>().Name("inspector").Child().OfType<Grid>());
        row.Setters.Add(new Setter(Layoutable.MinHeightProperty, FieldHeight));

        panel.Styles.Add(row);
        panel.Styles.Add(slider);
        panel.Styles.Add(number);
        panel.Styles.Add(field);
    }
}
