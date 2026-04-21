using PhotoPrivacy.Worker;

namespace PhotoPrivacy.IntegrationTests;

public sealed class WorkerEntryGuardTests
{
    [Fact]
    public void ShouldRejectDirectLaunch_Should_Return_True_For_UserInteractive_Without_Mode()
    {
        var reject = WorkerEntryGuard.ShouldRejectDirectLaunch(
            args: [],
            userInteractive: true,
            hasModeOption: false);

        Assert.True(reject);
    }

    [Fact]
    public void ShouldRejectDirectLaunch_Should_Return_False_When_Mode_Provided()
    {
        var reject = WorkerEntryGuard.ShouldRejectDirectLaunch(
            args: ["--mode", "background"],
            userInteractive: true,
            hasModeOption: true);

        Assert.False(reject);
    }

    [Fact]
    public void ShouldRejectDirectLaunch_Should_Return_False_For_NonInteractive()
    {
        var reject = WorkerEntryGuard.ShouldRejectDirectLaunch(
            args: [],
            userInteractive: false,
            hasModeOption: false);

        Assert.False(reject);
    }
}
