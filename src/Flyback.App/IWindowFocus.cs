namespace Flyback.App;

/// <summary>Whether the editor's window is the one in front.</summary>
internal interface IWindowFocus
{
    bool IsActive { get; }
}