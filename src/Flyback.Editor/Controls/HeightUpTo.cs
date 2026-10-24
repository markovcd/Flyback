using Avalonia;
using Avalonia.Controls;

namespace Flyback.Editor.Controls;

/// <summary>Holds its child at a set height, or at the room there is where that is less.</summary>
internal sealed class HeightUpTo(double height) : Decorator
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var room = availableSize.WithHeight(Math.Min(height, availableSize.Height));

        Child?.Measure(room);

        return new Size(Child?.DesiredSize.Width ?? 0, room.Height);
    }
}
