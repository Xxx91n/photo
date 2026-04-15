using System.Text.Json;

namespace PhotoPrivacy.Core.Configuration;

public static class AppConfigLoader
{
    public static AppConfig Load(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("config.json not found", configPath);
        }

        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return config ?? AppConfig.Default;
    }
}
