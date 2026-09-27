using Microsoft.Extensions.DependencyInjection;

namespace Flyback.App;

internal static class Extensions
{
    extension(IServiceProvider services)
    {
        public MainWindow GetMainWindow()
        {
            var mainWindow = services.GetRequiredService<MainWindow>();
            services.GetRequiredService<WindowHolder>().Attach(mainWindow);
            return mainWindow;
        }
    }
}