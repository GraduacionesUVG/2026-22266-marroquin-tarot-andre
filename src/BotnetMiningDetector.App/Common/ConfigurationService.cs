using System.IO;
using System.Text.Json;

namespace BotnetMiningDetector.App.Common;

// servicio de lectura y escritura de configuracion
public sealed class ConfigurationService
{
    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    // crea el servicio de configuracion
    public ConfigurationService(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    // carga la configuracion desde json
    public AgentSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                var defaults = new AgentSettings();
                defaults.Normalize();
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AgentSettings>(json) ?? new AgentSettings();
            settings.Normalize();
            return settings;
        }
        catch
        {
            var defaults = new AgentSettings();
            defaults.Normalize();
            return defaults;
        }
    }

    // guarda la configuracion en json
    public void Save(AgentSettings settings)
    {
        settings.Normalize();
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(_settingsPath, json);
    }
}
