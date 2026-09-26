using Avalonia.Controls;

namespace Flyback.App;

/// <summary>Questions put over the editor's window, to be answered before anything else happens.</summary>
internal interface IDialogs
{
    /// <inheritdoc cref="Flyback.App.Controls.Dialog.extension(Avalonia.Controls.Window).ShowDialog{TResult}"/>
    Task<TResult> Show<TResult>(string title, Control content, Control? header = null, bool fill = false);
}