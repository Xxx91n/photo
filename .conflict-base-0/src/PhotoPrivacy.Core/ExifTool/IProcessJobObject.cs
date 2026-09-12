using System.Diagnostics;

namespace PhotoPrivacy.Core.ExifTool;

public interface IProcessJobObject : IDisposable
{
    void Assign(Process process);
}

public sealed class NoopProcessJobObject : IProcessJobObject
{
    public void Assign(Process process)
    {
    }

    public void Dispose()
    {
    }
}
