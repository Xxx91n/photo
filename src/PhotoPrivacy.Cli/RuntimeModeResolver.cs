using Microsoft.Extensions.Configuration;

namespace PhotoPrivacy.Cli;

public static class RuntimeModeResolver
{
    public static RuntimeMode Resolve(IConfiguration configuration)
    {
        return ResolveRaw(configuration["mode"]);
    }

    public static RuntimeMode ResolveFromArgs(string[] args)
    {
        if (args is null || args.Length == 0)
        {
            return RuntimeMode.Background;
        }

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--mode=", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveRaw(arg["--mode=".Length..]);
            }

            if (!string.Equals(arg, "--mode", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 < args.Length)
            {
                return ResolveRaw(args[i + 1]);
            }
        }

        return RuntimeMode.Background;
    }

    public static bool HasModeOption(string[] args)
    {
        if (args is null || args.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--mode=", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(arg, "--mode", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return true;
            }
        }

        return false;
    }

    public static RuntimeMode ResolveRaw(string? raw)
    {
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
