namespace PhotoPrivacy.Core.Configuration;

public sealed class AppConfigValidationException : Exception
{
    public AppConfigValidationException(string message)
        : base(message)
    {
    }
}
