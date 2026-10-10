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
    // --- a number no box can hold -------------------------------------------

    /// <summary>
    /// A knob is a float and its number box holds a decimal, which stops near
    /// 7.9e28. Applying points the panel at the caret, so text holding a larger
    /// number has to survive the panel being built for it.
    /// </summary>
    [AvaloniaFact]
    public void A_number_past_what_a_number_box_holds_can_be_applied()
    {
        var window = Open();
        var text = ShowCode(window);

        text.Text = "t |> sine(freq: 100000000000000000000000000000000) |> out.left";
        text.CaretOffset = text.Text.IndexOf("sine", StringComparison.Ordinal) + 1;
        Settle(window);

        Should.NotThrow(() =>
        {
            Press(Apply(window));
            Settle(window);
        });

        Editor(window).Selection.Focused.ShouldNotBeNull().TypeId.ShouldBe("osc.sine");
    }
}
