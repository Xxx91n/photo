namespace PhotoPrivacy.Worker;

public static class WorkerInstanceMutexNames
{
    public const string Background = @"Global\PhotoPrivacyWorker_Background_Instance";
    public const string Service = @"Global\PhotoPrivacyWorker_Service_Instance";
}
