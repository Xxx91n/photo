using System.Threading;

namespace PhotoPrivacy.Core.Runtime;

public sealed class RuntimeControl : IRuntimeControl
{
    private int _paused;

    public bool IsPaused => Volatile.Read(ref _paused) == 1;

    public void Pause()
    {
        Interlocked.Exchange(ref _paused, 1);
    }

    public void Resume()
    {
        Interlocked.Exchange(ref _paused, 0);
    }
}
