using Microsoft.Extensions.Configuration;

namespace PhotoPrivacy.Cli;

public static class RuntimeModeResolver
{
    public static RuntimeMode Resolve(IConfiguration configuration)
    {
        var raw = configuration["mode"];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return RuntimeMode.Background;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "background" => RuntimeMode.Background,
            "service" => RuntimeMode.Service,
            "cli" => RuntimeMode.Cli,
            _ => RuntimeMode.Background
        };
    }
}
