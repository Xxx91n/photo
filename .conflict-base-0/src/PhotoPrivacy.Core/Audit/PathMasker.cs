using System.Security.Cryptography;
using System.Text;

namespace PhotoPrivacy.Core.Audit;

public static class PathMasker
{
    public static string Mask(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return sourcePath;
        }

        var separators = sourcePath.Contains('/') ? new[] { '/' } : new[] { '\\' };
        var parts = sourcePath.Split(separators, StringSplitOptions.None).ToArray();

        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (string.Equals(parts[i], "Users", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parts[i], "home", StringComparison.OrdinalIgnoreCase))
            {
                parts[i + 1] = "[redacted]";
                return string.Join(separators[0], parts);
            }
        }

        return sourcePath;
    }

    public static string StableHash(string sourcePath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath));
        return Convert.ToHexString(bytes[..8]);
    }
}
