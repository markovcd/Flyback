namespace Flyback.Editor;

/// <summary>Whether the editor's window is the one in front.</summary>
internal interface IFocus
{
    bool IsActive { get; }
}