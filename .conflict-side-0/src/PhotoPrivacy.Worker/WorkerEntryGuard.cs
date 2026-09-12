namespace PhotoPrivacy.Worker;

public static class WorkerEntryGuard
{
    public static bool ShouldRejectDirectLaunch(string[] args, bool userInteractive, bool hasModeOption)
    {
        if (!userInteractive)
        {
            return false;
        }

        if (hasModeOption)
        {
            return false;
        }

        return true;
    }
}
