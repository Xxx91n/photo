namespace PhotoPrivacy.Cli;

public static class AppInstanceMutexNames
{
    public const string Unified = @"Global\PhotoPrivacyCleaner_Instance";
    public const string ServiceHost = @"Global\PhotoPrivacyCleaner_ServiceHost";
    public const string ForServiceMode = Unified;
    public const string ForUiMode = Unified;
    public const string ForBackgroundMode = Unified;
}
