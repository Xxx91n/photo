using System.Diagnostics;
using PhotoPrivacy.Ui;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class ServiceManagerTests
{
    private sealed class FakeExecutor : IScCommandExecutor
    {
        private readonly ServiceCommandResult _result;
        private readonly Exception? _exception;

        public FakeExecutor(ServiceCommandResult result)
        {
            _result = result;
        }

        public FakeExecutor(Exception exception)
        {
            _result = ServiceCommandResult.Failed("exception");
            _exception = exception;
        }

        public ProcessStartInfo? LastStartInfo { get; private set; }

        public ServiceCommandResult Execute(ProcessStartInfo startInfo)
        {
            LastStartInfo = startInfo;

            if (_exception is not null)
            {
                throw _exception;
            }

            return _result;
        }
    }

    [Fact]
    public void BuildRunScVerb_Should_Return_Empty_When_Admin_Not_Required()
    {
        var verb = ServiceManager.BuildRunScVerb(requireAdmin: false, isAdministrator: false);

        Assert.Equal(string.Empty, verb);
    }

    [Fact]
    public void BuildRunScVerb_Should_Return_Empty_When_Already_Admin()
    {
        var verb = ServiceManager.BuildRunScVerb(requireAdmin: true, isAdministrator: true);

        Assert.Equal(string.Empty, verb);
    }

    [Fact]
    public void BuildRunScVerb_Should_Return_Runas_When_Elevation_Needed()
    {
        var verb = ServiceManager.BuildRunScVerb(requireAdmin: true, isAdministrator: false);

        Assert.Equal("runas", verb);
    }

    [Fact]
    public void BuildInstallArguments_Should_Quote_Executable_And_Config_Path()
    {
        var exePath = @"C:\Program Files\Photo Privacy\PhotoPrivacy.exe";
        var configPath = @"D:\cfg path\config.json";

        var args = ServiceManager.BuildInstallArguments(exePath, configPath);

        Assert.Equal(
            "create PhotoPrivacyCleaner binPath= \"\"C:\\Program Files\\Photo Privacy\\PhotoPrivacy.exe\" --mode service --config \"D:\\cfg path\\config.json\"\" start= auto",
            args);
    }

    [Fact]
    public void BuildRunScStartInfo_Should_Request_Runas_When_Forced()
    {
        var startInfo = ServiceManager.BuildRunScStartInfo(
            args: "start PhotoPrivacyCleaner",
            requireAdmin: true,
            isAdministrator: true,
            forceElevation: true);

        Assert.Equal("sc.exe", startInfo.FileName);
        Assert.Equal("start PhotoPrivacyCleaner", startInfo.Arguments);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal("runas", startInfo.Verb);
    }

    [Fact]
    public void BuildRunScStartInfo_Should_Skip_Runas_When_ForceElevation_Is_False()
    {
        var startInfo = ServiceManager.BuildRunScStartInfo(
            args: "stop PhotoPrivacyCleaner",
            requireAdmin: true,
            isAdministrator: false,
            forceElevation: false);

        Assert.Equal(string.Empty, startInfo.Verb);
    }

    [Fact]
    public void Install_Should_Return_Executor_Result_And_Build_Create_Command()
    {
        var expected = ServiceCommandResult.Success();
        var fakeExecutor = new FakeExecutor(expected);
        var manager = new ServiceManager(fakeExecutor);

        var result = manager.Install(@"C:\Program Files\PhotoPrivacy\PhotoPrivacy.exe", @"D:\cfg\config.json", forceElevation: false);

        Assert.Equal(ServiceCommandStatus.Success, result.Status);
        Assert.NotNull(fakeExecutor.LastStartInfo);
        Assert.Equal("sc.exe", fakeExecutor.LastStartInfo!.FileName);
        Assert.Equal(string.Empty, fakeExecutor.LastStartInfo.Verb);
        Assert.Contains("create PhotoPrivacyCleaner", fakeExecutor.LastStartInfo.Arguments);
    }

    [Fact]
    public void Start_Should_Request_Runas_When_Forced_And_Not_Admin()
    {
        var fakeExecutor = new FakeExecutor(ServiceCommandResult.Success());
        var manager = new ServiceManager(fakeExecutor);

        _ = manager.Start(forceElevation: true);

        Assert.NotNull(fakeExecutor.LastStartInfo);
        Assert.Equal("runas", fakeExecutor.LastStartInfo!.Verb);
        Assert.Equal("start PhotoPrivacyCleaner", fakeExecutor.LastStartInfo.Arguments);
    }

    [Fact]
    public void IsElevationCancelled_Should_Return_True_For_Win32_1223()
    {
        var cancelled = ServiceManager.IsElevationCancelled(new System.ComponentModel.Win32Exception(1223));

        Assert.True(cancelled);
    }

    [Fact]
    public void Install_Should_Return_Failed_When_Executor_Returns_NonZero_Code()
    {
        var fakeExecutor = new FakeExecutor(ServiceCommandResult.Failed("sc failure", exitCode: 5));
        var manager = new ServiceManager(fakeExecutor);

        var result = manager.Install(@"C:\app\PhotoPrivacy.exe", @"D:\cfg\config.json", forceElevation: false);

        Assert.Equal(ServiceCommandStatus.Failed, result.Status);
        Assert.Equal(5, result.ExitCode);
    }

    [Fact]
    public void Stop_Should_Return_ElevationCancelled_When_User_Rejects_Uac()
    {
        var fakeExecutor = new FakeExecutor(new System.ComponentModel.Win32Exception(1223));
        var manager = new ServiceManager(fakeExecutor);

        var result = manager.Stop(forceElevation: true);

        Assert.Equal(ServiceCommandStatus.ElevationCancelled, result.Status);
    }

    [Fact]
    public void GetStatusText_Should_Return_NonEmpty_Text_On_Current_Platform()
    {
        var manager = new ServiceManager(new FakeExecutor(ServiceCommandResult.Success()));

        var status = manager.GetStatusText();

        Assert.False(string.IsNullOrWhiteSpace(status));
    }
}
