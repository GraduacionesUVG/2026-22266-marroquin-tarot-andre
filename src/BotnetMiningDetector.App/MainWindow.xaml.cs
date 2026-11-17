using System.Windows;
using System.Globalization;
using System.Text.Json;
using BotnetMiningDetector.App.Common;
using BotnetMiningDetector.App.UI.ViewModels;
using Microsoft.AspNetCore.Components.WebView;
using BotnetMiningDetector.App.UI.Blazor;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;

namespace BotnetMiningDetector.App;

// ventana principal del agente
public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions MessageJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IServiceProvider _services;

    // prepara la ventana y el navegador local
    public MainWindow(IServiceProvider services)
    {
        _services = services;
        InitializeComponent();
        BlazorHost.BlazorWebViewInitialized += OnBlazorWebViewInitialized;
        BlazorHost.Services = services;
        BlazorHost.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(AppRoot)
        });
    }

    // configura el navegador embebido
    private void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
    {
        e.WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
    }

    // recibe mensajes desde blazor
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = JsonSerializer.Deserialize<AgentWebMessage>(e.WebMessageAsJson, MessageJsonOptions);
            if (message is null)
            {
                return;
            }

            _ = Dispatcher.InvokeAsync(async () => await HandleAgentMessageAsync(message));
        }
        catch
        {
            // el puente de ui es auxiliar y los errores no deben cerrar la app
        }
    }

    // procesa mensajes del agente
    private async Task HandleAgentMessageAsync(AgentWebMessage message)
    {
        var viewModel = _services.GetRequiredService<MainViewModel>();
        var interaction = _services.GetRequiredService<UiInteractionState>();

        switch (message.Kind)
        {
            case "nav":
                interaction.SelectTab(message.Value);
                break;
            case "scenario":
                await viewModel.RunScenarioAsync(message.Value);
                break;
            case "alert":
                if (long.TryParse(message.Value, out var selectedAlertId))
                {
                    interaction.SelectAlert(selectedAlertId);
                }
                break;
            case "setting":
                ApplySetting(viewModel.Settings, message.Name, message.Value);
                break;
            case "filter":
                interaction.SetFilter(message.Name, message.Value);
                break;
            case "column":
                interaction.SetColumnVisible(message.Name, message.Value.Equals("true", StringComparison.OrdinalIgnoreCase));
                break;
            case "action":
                await HandleActionAsync(viewModel, message);
                break;
        }
    }

    // ejecuta acciones solicitadas por la interfaz
    private static async Task HandleActionAsync(MainViewModel viewModel, AgentWebMessage message)
    {
        switch (message.Value)
        {
            case "start":
                await viewModel.StartMonitoringAsync();
                break;
            case "pause":
                await viewModel.PauseMonitoringAsync();
                break;
            case "baseline":
                await viewModel.BuildBaselineAsync();
                break;
            case "clear":
                await viewModel.ClearExperimentDataAsync();
                break;
            case "export":
                await viewModel.ExportAsync();
                break;
            case "refresh":
                await viewModel.RefreshFromDatabaseAsync();
                break;
            case "stop-simulation":
                await viewModel.StopSimulationAsync();
                break;
            case "save-settings":
                await viewModel.SaveSettingsAsync();
                break;
            case "ack-alert":
                if (long.TryParse(message.AlertId, out var alertId))
                {
                    var alert = viewModel.Alerts.FirstOrDefault(item => item.Id == alertId);
                    if (alert is not null)
                    {
                        await viewModel.AcknowledgeAlertAsync(alert);
                    }
                }
                break;
        }
    }

    // aplica un valor de configuracion
    private static void ApplySetting(AgentSettings settings, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var property = typeof(AgentSettings).GetProperty(name);
        if (property is null || !property.CanWrite)
        {
            return;
        }

        try
        {
            if (property.PropertyType == typeof(bool) && bool.TryParse(value, out var boolValue))
            {
                property.SetValue(settings, boolValue);
            }
            else if (property.PropertyType == typeof(int) && int.TryParse(value, out var intValue))
            {
                property.SetValue(settings, intValue);
            }
            else if (property.PropertyType == typeof(double) &&
                     (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue) ||
                      double.TryParse(value, out doubleValue)))
            {
                property.SetValue(settings, doubleValue);
            }
        }
        catch
        {
        }
    }

    // mensaje recibido desde la interfaz
    private sealed class AgentWebMessage
    {
        public string Kind { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AlertId { get; set; } = string.Empty;
    }
}
