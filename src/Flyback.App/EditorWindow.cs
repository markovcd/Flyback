using Microsoft.Extensions.DependencyInjection;

namespace Flyback.App;

/// <summary>
/// The editor's window, for the services that act on it. The window is built from
/// what takes these, so it is asked for only once one of them is used.
/// </summary>
internal sealed class EditorWindow(IServiceProvider services)
{
    private bool building;

    /// <summary>The window. Asked for while it is being built, it throws rather than have the container build a second one.</summary>
    public MainWindow Value => building
        ? throw new InvalidOperationException("The window was asked for while it was being built. Ask for it once something acts, not from a constructor.")
        : services.GetRequiredService<MainWindow>();

    /// <summary>Builds the window, with <see cref="Value"/> refused until it is done. The container's one way to a <see cref="MainWindow"/>.</summary>
    public MainWindow Build()
    {
        building = true;

        try
        {
            return ActivatorUtilities.CreateInstance<MainWindow>(services);
        }
        finally
        {
            building = false;
        }
    }
}