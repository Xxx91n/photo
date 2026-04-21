namespace PhotoPrivacy.Worker;

public static class WorkerInstanceMutexNames
{
    public const string Unified = @"Global\PhotoPrivacyWorker_Instance";
    public const string Background = Unified;
    public const string Service = Unified;
}
