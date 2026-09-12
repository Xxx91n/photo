using PhotoPrivacy.Core.Runtime;

namespace PhotoPrivacy.Core.Tests.Runtime;

public sealed class RuntimeControlTests
{
    [Fact]
    public void Pause_Then_Resume_Should_Toggle_IsPaused()
    {
        var control = new RuntimeControl();

        Assert.False(control.IsPaused);

        control.Pause();
        Assert.True(control.IsPaused);

        control.Resume();
        Assert.False(control.IsPaused);
    }
}
