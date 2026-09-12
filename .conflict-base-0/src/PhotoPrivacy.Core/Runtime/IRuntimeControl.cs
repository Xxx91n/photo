namespace PhotoPrivacy.Core.Runtime;

public interface IRuntimeControl
{
    bool IsPaused { get; }

    void Pause();

    void Resume();
}
