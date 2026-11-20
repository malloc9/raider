namespace Raider.Desktop;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Raider.Core.Models;
using Raider.Core.Services;
using Raider.Core.Ports;
using Raider.Desktop.Services;
using Raider.Desktop.ViewModels;
using Raider.Desktop.Views;
using System.Windows;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Core services
                services.AddSingleton<IGameDetectionService, GameDetectionService>();
                services.AddSingleton<IPluginInstallerService, PluginInstallerService>();
                services.AddSingleton<IPluginHealthClient, PluginHealthClient>();
                services.AddSingleton<IAccountSwitcher, PlariumPlayAccountSwitcher>();
                services.AddSingleton<IGamePort>(sp => new PluginGamePort(sp.GetRequiredService<IAccountSwitcher>()));
                services.AddSingleton<INotificationService, WindowsNotificationService>();
                services.AddSingleton<IAccountStore>(_ => new JsonAccountStore());
                services.AddSingleton<ISnapshotService, SnapshotService>();
                services.AddSingleton<IInstanceRegistry>(sp => 
                {
                    var healthClient = sp.GetRequiredService<IPluginHealthClient>();
                    return new InstanceRegistry(healthClient);
                });
                services.AddSingleton<RunCoordinator>(sp => new RunCoordinator(
                    sp.GetRequiredService<IInstanceRegistry>(),
                    sp.GetRequiredService<IAccountStore>(),
                    sp.GetRequiredService<RunOrchestrator>(),
                    sp.GetRequiredService<ILogger<RunCoordinator>>()));
                // No target game build pinned yet (Unknown): failures count as plain failures, not "broken by game update"
                services.AddSingleton(sp => new RunOrchestrator(
                    sp.GetRequiredService<IGamePort>(),
                    sp.GetRequiredService<ISnapshotService>(),
                    sp.GetRequiredService<INotificationService>(),
                    GameBuild.Unknown,
                    sp.GetRequiredService<ILogger<RunOrchestrator>>()));
                
                // ViewModels
                services.AddSingleton<MainWindowViewModel>();
                
                // Views
                services.AddSingleton<MainWindow>();
                
                // Logging
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Debug);
                });
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host!.StartAsync();
        
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
        
        // Start the RunCoordinator
        var coordinator = _host.Services.GetRequiredService<RunCoordinator>();
        coordinator.Start();
        
        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        var coordinator = _host.Services.GetRequiredService<RunCoordinator>();
        coordinator.Stop();
        coordinator.Dispose();
        
        var registry = _host.Services.GetRequiredService<IInstanceRegistry>();
        await registry.DisposeAsync();
        
        await _host!.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }

    public static T GetService<T>() where T : notnull
    {
        return ((App)Current)._host!.Services.GetRequiredService<T>();
    }
}