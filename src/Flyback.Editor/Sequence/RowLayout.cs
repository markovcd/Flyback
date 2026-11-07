using Avalonia;

namespace Flyback.Editor.Sequence;

/// <summary>Where everything in the Sequence pane's rows stands: the names, the ruler, each part's cells, scrolled and zoomed.</summary>
internal sealed class RowLayout
{
    public const double NameWidth = 112, RulerHeight = 22, BarHeight = 8;

    public const double MinCell = 6, MaxCell = 64, DefaultCell = 24;

    private const double MaxRow = 34, MinRow = 14;

    /// <summary>How far either side of a part's end its handle can be taken hold of.</summary>
    public const double HandleReach = 5;

    /// <summary>How wide a section is drawn.</summary>
    public double Cell { get; private set; } = DefaultCell;

    /// <summary>How far along the song the rows are scrolled, in pixels.</summary>
    public double Scroll { get; private set; }

    public Size Size { get; set; }

    /// <summary>How many parts there are, and the longest's length, which bound the scroll.</summary>
    public (int Parts, int Longest) Content { get; set; }

    /// <summary>How wide the rows' window onto the song is.</summary>
    public double Window => Math.Max(0, Size.Width - NameWidth);

    /// <summary>How tall a row is: as tall as it can be up to its most, when the pane is short.</summary>
    public double Row => Content.Parts == 0
        ? MaxRow
        : Math.Clamp((Size.Height - RulerHeight - BarHeight) / Content.Parts, MinRow, MaxRow);

    /// <summary>The whole song's width, and a section past the longest part's end for its handle.</summary>
    private double Width => (Content.Longest + 1) * Cell;

    /// <summary>Where section <paramref name="section"/> begins, on screen.</summary>
    public double X(double section) => NameWidth + section * Cell - Scroll;

    /// <summary>The section at <paramref name="x"/>, as a fraction.</summary>
    public double Section(double x) => (x - NameWidth + Scroll) / Cell;

    public double RowTop(int part) => RulerHeight + part * Row;

    /// <summary>The part whose row is at <paramref name="y"/>, or -1.</summary>
    public int PartAt(double y) =>
        y < RulerHeight || Content.Parts == 0 ? -1 : (int)((y - RulerHeight) / Row) is var part && part < Content.Parts ? part : -1;

    public void ScrollTo(double pixels) => Scroll = Math.Clamp(pixels, 0, Math.Max(0, Width - Window));

    public void ScrollBy(double pixels) => ScrollTo(Scroll + pixels);

    /// <summary>Zooms to <paramref name="cell"/>, keeping the section under <paramref name="x"/> where it is.</summary>
    public void Zoom(double cell, double x)
    {
        var held = Section(x);
        Cell = Math.Clamp(cell, MinCell, MaxCell);
        ScrollTo(held * Cell - (x - NameWidth));
    }

    /// <summary>Zooms so the whole of the longest part is in view.</summary>
    public void Fit()
    {
        Cell = Content.Longest == 0 ? DefaultCell : Math.Clamp(Window / (Content.Longest + 1), MinCell, MaxCell);
        ScrollTo(0);
    }

    /// <summary>The thumb of the scroll bar under the rows, or null where the whole song is in view.</summary>
    public Rect? Thumb
    {
        get
        {
            if (Width <= Window || Window <= 0) return null;

            var share = Window / Width;
            return new Rect(NameWidth + Scroll / Width * Window, Size.Height - BarHeight + 2, Math.Max(16, share * Window), BarHeight - 4);
        }
    }

    /// <summary>The scroll that puts the bar's thumb's left edge at <paramref name="x"/>.</summary>
    public double ScrollForThumb(double x) => (x - NameWidth) / Window * Width;
}
