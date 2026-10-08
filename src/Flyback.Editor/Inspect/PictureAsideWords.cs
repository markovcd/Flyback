namespace Flyback.Editor.Inspect;

/// <summary>What the button that sends the picture aside says, by what pressing it would do.</summary>
internal static class PictureAsideWords
{
    public static string Label(bool hidden) => hidden ? "Picture" : "Expand";

    public static string Tip(bool hidden) => hidden
        ? "Bring the picture back above the panel"
        : "Give the panel the whole side, the picture stepping aside";
}
