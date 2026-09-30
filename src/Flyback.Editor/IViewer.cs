namespace Flyback.App;

/// <summary>Plays a patch in the viewer, beside the editor.</summary>
internal interface IViewer
{
    /// <summary>Plays the bundle <paramref name="pack"/> makes, under <paramref name="name"/>.</summary>
    /// <remarks>Packed only once the viewer is ready for it, since a page may open a tab only straight from the press.</remarks>
    void Show(string name, Func<byte[]> pack);
}
