namespace Flyback.Editor.Inspect;

/// <summary>
/// Whether the picture steps aside so the inspector has the whole side column: asked for from the
/// inspector, and kept only while it shows a block, so the picture comes back with nothing selected.
/// </summary>
internal sealed class PictureAside
{
    private bool wanted;
    private bool holding;

    /// <summary>Whether the picture is out of the side column now.</summary>
    public bool Hidden => wanted && holding;

    /// <summary>The picture went or came back.</summary>
    public event Action? Changed;

    /// <summary>Sends the picture aside, or brings it back.</summary>
    public void Toggle() => Set(() => wanted = !wanted);

    /// <summary>Whether the inspector is showing a block, which is all the picture may step aside for.</summary>
    public void Holding(bool showing) => Set(() => holding = showing);

    private void Set(Action change)
    {
        var was = Hidden;
        change();
        if (Hidden != was) Changed?.Invoke();
    }
}
