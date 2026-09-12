using PhotoPrivacy.Core.Runtime;

namespace PhotoPrivacy.Core.Tests.Runtime;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void MutexSingleInstanceGuard_FirstConstructor_IsOwner()
    {
        var name = "phprvtest-mut-" + Guid.NewGuid().ToString("N");
        using var guard = new MutexSingleInstanceGuard(name);
        Assert.True(guard.IsOwner);
    }

    [Fact]
    public void MutexSingleInstanceGuard_Second_Instance_IsNotOwner()
    {
        var name = "phprvtest-mut-" + Guid.NewGuid().ToString("N");
        using var first = new MutexSingleInstanceGuard(name);
        Assert.True(first.IsOwner);

        using var second = new MutexSingleInstanceGuard(name);
        Assert.False(second.IsOwner);
    }

    [Fact]
    public void MutexSingleInstanceGuard_Dispose_Releases_For_Next_Instance()
    {
        var name = "phprvtest-mut-" + Guid.NewGuid().ToString("N");
        var first = new MutexSingleInstanceGuard(name);
        Assert.True(first.IsOwner);
        first.Dispose();

        using var second = new MutexSingleInstanceGuard(name);
        Assert.True(second.IsOwner);
    }

    [Fact]
    public void FlockSingleInstanceGuard_FirstConstructor_IsOwner()
    {
        var path = Path.Combine(Path.GetTempPath(), "phprvtest-flock-" + Guid.NewGuid().ToString("N") + ".lock");
        using var guard = new FlockSingleInstanceGuard(path);
        try
        {
            Assert.True(guard.IsOwner);
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) { try { File.Delete(path); } catch { } }
        }
    }

    [Fact]
    public void FlockSingleInstanceGuard_Second_Instance_IsNotOwner()
    {
        var path = Path.Combine(Path.GetTempPath(), "phprvtest-flock-" + Guid.NewGuid().ToString("N") + ".lock");
        using var first = new FlockSingleInstanceGuard(path);
        Assert.True(first.IsOwner);

        using var second = new FlockSingleInstanceGuard(path);
        Assert.False(second.IsOwner);

        first.Dispose();
        using var third = new FlockSingleInstanceGuard(path);
        Assert.True(third.IsOwner);
        third.Dispose();
    }

    [Fact]
    public void SingleInstanceGuardFactory_Returns_Concrete_Guard_For_This_Platform()
    {
        var guard = SingleInstanceGuardFactory.Create("phprvtest-factory-" + Guid.NewGuid().ToString("N"));
        Assert.NotNull(guard);
        Assert.True(guard is ISingleInstanceGuard);
        guard.Dispose();
    }
}
