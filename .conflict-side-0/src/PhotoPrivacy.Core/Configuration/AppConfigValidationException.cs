namespace PhotoPrivacy.Core.Configuration;

public sealed class AppConfigValidationException : Exception
{
    public AppConfigValidationException()
    {
    }

    public AppConfigValidationException(string message)
        : base(message)
    {
    }

    public AppConfigValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
