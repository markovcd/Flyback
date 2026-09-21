namespace Flyback.App;

/// <summary>A monitor as the platform described it: its name and where it sits in the desktop.</summary>
public sealed class MonitorSpot
{
    public string? Name { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}
