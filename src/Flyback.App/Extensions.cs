using Flyback.App.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Flyback.App;

internal static class Extensions
{
    extension(IServiceProvider services)
    {
        public MainWindow GetMainWindow()
        {
            var mainWindow = services.GetRequiredService<MainWindow>();
            services.GetRequiredService<MainWindowLocator>().Attach(mainWindow);
            return mainWindow;
        }
    }
}