using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SuperMod.App.Services;
using SuperMod.App.ViewModels;
using SuperMod.App.Views;

namespace SuperMod.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var controller = new BotController();
            var viewModel = new MainWindowViewModel(
                controller,
                new JsonConfigStore(),
                action => Dispatcher.UIThread.Post(action));

            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.ShutdownRequested += (_, _) => _ = controller.DisposeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
