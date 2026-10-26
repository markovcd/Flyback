using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Core.Graph;

namespace Flyback.Editor.Canvas;

/// <summary>
/// The buttons under a module whose well a finger tapped: a step down, Fine, which
/// latches each step to a fine one, Type, and a step up. For a hand that would rather
/// press than drag.
/// </summary>
internal sealed class StepBar(SocketFocus focus, CanvasHistory history, NodeGeometry geometry, Viewport view)
{
    public const double Height = 64, Gap = 8;

    private const double ButtonWidth = 76, Margin = 8, Below = 12;

    private static readonly string[] Labels = ["− step", "Fine ×5", "Type…", "+ step"];

    /// <summary>Where the bar stands on screen: under the module, or over it where the view ends first.</summary>
    public Rect? Box
    {
        get
        {
            if (!focus.Bar || focus.Typed is not null || focus.Focused is not { } pick
                || history.Patch.Find(pick.Node) is not { } node || NodeCatalog.Get(node.TypeId) is not { } def)
                return null;

            var module = view.OnScreen(geometry.Bounds(node, def));
            var width = Math.Max(module.Width, Labels.Length * ButtonWidth + (Labels.Length - 1) * Gap);
            var x = Math.Clamp(module.Left, Margin, Math.Max(Margin, view.Size.Width - width - Margin));
            var y = module.Bottom + Below + Height <= view.Size.Height - Margin ? module.Bottom + Below : module.Top - Below - Height;

            return new Rect(x, Math.Max(Margin, y), width, Height);
        }
    }

    /// <summary>The button under a point on screen: 0 a step down, 1 Fine, 2 Type, 3 a step up.</summary>
    public int? ButtonAt(Point screen)
    {
        if (Box is not { } box || !box.Contains(screen)) return null;

        for (var i = 0; i < Labels.Length; i++)
            if (Button(box, i).Contains(screen)) return i;

        return null;
    }

    /// <summary>The middle of the button labeled <paramref name="label"/> on screen, while the bar shows.</summary>
    public Point? Center(string label) =>
        Box is { } box && Array.IndexOf(Labels, label) is var index and >= 0 ? Button(box, index).Center : null;

    /// <summary>Does what the button pressed says.</summary>
    public void Press(int button)
    {
        switch (button)
        {
            case 0:
                focus.Step(-1);
                break;

            case 1:
                focus.FineLatched = !focus.FineLatched;
                break;

            case 2:
                focus.BeginTyping();
                break;

            case 3:
                focus.Step(1);
                break;
        }
    }

    public void Draw(DrawingContext context)
    {
        if (Box is not { } box) return;

        for (var i = 0; i < Labels.Length; i++)
        {
            var button = Button(box, i);
            var lit = i == 1 && focus.FineLatched;

            context.DrawRectangle(lit ? LitFill : Fill, lit ? LitEdge : Edge, button, 10, 10);

            var label = new FormattedText(Labels[i], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, 15, lit ? Dark : Light);
            context.DrawText(label, new Point(button.Center.X - label.Width / 2, button.Center.Y - label.Height / 2));
        }
    }

    private static Rect Button(Rect box, int index)
    {
        var width = (box.Width - (Labels.Length - 1) * Gap) / Labels.Length;

        return new Rect(box.Left + index * (width + Gap), box.Top, width, box.Height);
    }

    private static readonly Typeface Bold = new(Typeface.Default.FontFamily, weight: FontWeight.SemiBold);
    private static readonly IBrush Fill = new ImmutableSolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x34));
    private static readonly IPen Edge = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x4A, 0x50, 0x5B)));
    private static readonly IImmutableBrush LitFill = new ImmutableSolidColorBrush(Flyback.Ui.Controls.Colors.Attention);
    private static readonly IPen LitEdge = new ImmutablePen(LitFill);
    private static readonly IBrush Light = new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8));
    private static readonly IBrush Dark = new ImmutableSolidColorBrush(Color.FromRgb(0x0E, 0x10, 0x13));
}
