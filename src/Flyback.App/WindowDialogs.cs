using Avalonia.Controls;
using Flyback.App.Controls;

namespace Flyback.App;

internal sealed class WindowDialogs(EditorWindow window) : IDialogs
{
    public Task<TResult> Show<TResult>(string title, Control content, Control? header = null, bool fill = false) =>
        window.Value.ShowDialog<TResult>(title, content, header, fill);
}