using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Flyback.Editor.Windows;

/// <summary>
/// A picture drawn by the host above the editor's own surface, a page's canvas, put behind
/// it instead while the picture has the screen, so the patch and the controls over the
/// picture show: every ground between the picture and the screen is cleared until it is put back.
/// </summary>
/// <param name="move">The host's own move of its picture behind the editor's surface, or back above it.</param>
internal sealed class PictureBehind(Action<bool> move)
{
    private readonly List<(Visual Visual, IBrush? Ground)> cleared = [];

    /// <summary>Whether the picture is behind the editor's surface.</summary>
    public bool IsBehind { get; private set; }

    /// <summary>Puts the picture in <paramref name="box"/> behind the editor's surface, or back above it.</summary>
    public void Put(Control box, bool behind)
    {
        if (behind == IsBehind) return;

        IsBehind = behind;

        if (behind)
        {
            for (Visual? at = box; at is not null; at = at.GetVisualParent()) Clear(at);

            // The ground a surface with no transparency of its own paints under everything, white.
            if (TopLevel.GetTopLevel(box) is { } top)
                foreach (var part in top.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "PART_TransparencyFallback"))
                    Clear(part);
        }
        else
        {
            foreach (var (visual, ground) in cleared) SetGround(visual, ground);
            cleared.Clear();
        }

        move(behind);
    }

    private void Clear(Visual visual)
    {
        if (Ground(visual) is not { } ground || ground == Brushes.Transparent) return;

        cleared.Add((visual, ground));
        SetGround(visual, Brushes.Transparent);
    }

    private static IBrush? Ground(Visual visual) => visual switch
    {
        Panel panel => panel.Background,
        Border border => border.Background,
        ContentPresenter presenter => presenter.Background,
        TemplatedControl templated => templated.Background,
        _ => null,
    };

    private static void SetGround(Visual visual, IBrush? ground)
    {
        switch (visual)
        {
            case Panel panel: panel.Background = ground; break;
            case Border border: border.Background = ground; break;
            case ContentPresenter presenter: presenter.Background = ground; break;
            case TemplatedControl templated: templated.Background = ground; break;
        }
    }
}
