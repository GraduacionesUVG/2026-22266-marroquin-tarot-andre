using System.IO;
using System.Windows;

using BotnetMiningDetector.App.Agent;
using BotnetMiningDetector.App.Baseline;
using BotnetMiningDetector.App.Common;
using BotnetMiningDetector.App.Data;
using BotnetMiningDetector.App.Detection;
using BotnetMiningDetector.App.Evaluation;
using BotnetMiningDetector.App.Export;
using BotnetMiningDetector.App.Simulation;
using BotnetMiningDetector.App.UI.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace BotnetMiningDetector.App;

// ciclo de vida de la aplicacion
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    // inicia la aplicacion y sus servicios
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {

            var services = new ServiceCollection();
            services.AddWpfBlazorWebView();

            var configuration = new ConfigurationService(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
            var settings = configuration.Load();

            services.AddSingleton(configuration);
            services.AddSingleton(settings);
            services.AddSingleton<UiInteractionState>();
            services.AddSingleton(new SqliteRepository(settings.DatabasePath));
            services.AddSingleton<SystemResourceReader>();
            services.AddSingleton<NetworkConnectionReader>();
            services.AddSingleton<ProcessSampler>();
            services.AddSingleton<MonitoringService>();
            services.AddSingleton<BaselineService>();
            services.AddSingleton<DetectionEngine>();
            services.AddSingleton<SimulationService>();
            services.AddSingleton<EvaluationMetricsCalculator>();
            services.AddSingleton<CsvExportService>();
            services.AddSingleton<MainViewModel>();

            _serviceProvider = services.BuildServiceProvider();

            var window = new MainWindow(_serviceProvider);
            MainWindow = window;
            window.Show();
            ShutdownMode = ShutdownMode.OnLastWindowClose;

            _ = InitializeViewModelAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"No se pudo iniciar la aplicación Blazor Hybrid.\n\n{ex.Message}",
                "Error de inicio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    // prepara el modelo principal de la interfaz
    private async Task InitializeViewModelAsync()
    {
        if (_serviceProvider is null)
        {
            return;
        }

        try
        {
            var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();
            await viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"La ventana se inició, pero no se pudo inicializar el agente.\n\n{ex.Message}",
                "Error de inicialización",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    // detiene servicios y libera recursos
    protected override async void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider is not null)
        {
            var viewModel = _serviceProvider.GetService<MainViewModel>();
            if (viewModel is not null)
            {
                await viewModel.ShutdownAsync();
            }

            await _serviceProvider.DisposeAsync();
        }

        base.OnExit(e);
    }
}
