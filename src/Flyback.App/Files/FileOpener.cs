namespace Flyback.App.Files;

/// <summary>Which program opens Flyback's files when one is opened from outside it.</summary>
public enum FileOpener
{
    /// <summary>Flyback claims none of them.</summary>
    None,

    Editor,

    Viewer,
}