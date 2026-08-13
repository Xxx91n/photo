namespace PhotoPrivacy.Worker;

public static class WorkerInstanceMutexNames
{
    // Background and Service are distinct runtime modes with separate IPC endpoints
    // (PhotoPrivacyCleaner.Background vs PhotoPrivacyCleaner.Service). They must use
    // separate Mutex names so a UI-launched background worker does not block the
    // Windows/Linux/macOS service from starting.
    public const string Background = @"Global\PhotoPrivacyWorker_Background";
    public const string Service = @"Global\PhotoPrivacyWorker_Service";
}
