using System.Diagnostics;
using PhotoPrivacy.Ui;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class ServiceManagerTests
{
    private sealed class FakeStateProbe : IServiceStateProbe
    {
        public bool Exists { get; set; }

        public ServiceRuntimeState State { get; set; }

        public ServiceRuntimeState GetState(string serviceName)
        {
            return State;
        }

        public bool ServiceExists(string serviceName)
        {
            return Exists;
        }
    }

    private sealed class MutableStateProbe : IServiceStateProbe
    {
        private readonly Queue<ServiceRuntimeState> _states;
        private ServiceRuntimeState _last;

        public MutableStateProbe(params ServiceRuntimeState[] states)
        {
            _states = new Queue<ServiceRuntimeState>(states);
            _last = states.Length > 0 ? states[^1] : ServiceRuntimeState.NotInstalled;
        }

        public ServiceRuntimeState GetState(string serviceName)
        {
            if (_states.Count > 0)
            {
                _last = _states.Dequeue();
            }

            return _last;
        }

        public bool ServiceExists(string serviceName)
        {
            return GetState(serviceName) != ServiceRuntimeState.NotInstalled;
        }
    }

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
        var exePath = @"C:\Program Files\Photo Privacy\PhotoPrivacyWorker.exe";
        var configPath = @"D:\cfg path\config.json";

        var args = ServiceManager.BuildInstallArguments(exePath, configPath);

        Assert.Equal(
            "create PhotoPrivacyCleaner binPath= \"\"C:\\Program Files\\Photo Privacy\\PhotoPrivacyWorker.exe\" --mode service --config \"D:\\cfg path\\config.json\"\" start= auto",
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
        var manager = new ServiceManager(fakeExecutor, new FakeStateProbe());

        var result = manager.Install(@"C:\Program Files\PhotoPrivacy\PhotoPrivacyWorker.exe", @"D:\cfg\config.json", forceElevation: false);

        Assert.Equal(ServiceCommandStatus.Success, result.Status);
        Assert.NotNull(fakeExecutor.LastStartInfo);
        Assert.Equal("sc.exe", fakeExecutor.LastStartInfo!.FileName);
        Assert.Equal(string.Empty, fakeExecutor.LastStartInfo.Verb);
        Assert.Contains("create PhotoPrivacyCleaner", fakeExecutor.LastStartInfo.Arguments);
    }

    [Fact]
    public void Install_Should_Fail_When_Path_Is_Not_Worker_Executable()
    {
        var fakeExecutor = new FakeExecutor(ServiceCommandResult.Success());
        var manager = new ServiceManager(fakeExecutor, new FakeStateProbe());

        var result = manager.Install(@"C:\Program Files\PhotoPrivacy\PhotoPrivacy.exe", @"D:\cfg\config.json", forceElevation: false);

        Assert.Equal(ServiceCommandStatus.Failed, result.Status);
        Assert.Contains("PhotoPrivacyWorker.exe", result.Message, StringComparison.Ordinal);
        Assert.Null(fakeExecutor.LastStartInfo);
    }

    [Fact]
    public void Start_Should_Request_Runas_When_Forced_And_Not_Admin()
    {
        var fakeExecutor = new FakeExecutorWithQueue(new Queue<ServiceCommandResult>(new[]
        {
            ServiceCommandResult.Success(),
            ServiceCommandResult.Success()
        }));
        var manager = new ServiceManager(fakeExecutor, new FakeStateProbe
        {
            Exists = true,
            State = ServiceRuntimeState.Stopped
        });

        _ = manager.Start(@"C:\app\PhotoPrivacyWorker.exe", @"D:\cfg\config.json", forceElevation: true);

        Assert.Equal(2, fakeExecutor.Calls.Count);
        Assert.Contains(fakeExecutor.Calls, x => x.StartsWith("config PhotoPrivacyCleaner", StringComparison.Ordinal));
        Assert.Contains(fakeExecutor.Calls, x => x.Equals("start PhotoPrivacyCleaner", StringComparison.Ordinal));
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
        var manager = new ServiceManager(fakeExecutor, new FakeStateProbe());

        var result = manager.Install(@"C:\app\PhotoPrivacyWorker.exe", @"D:\cfg\config.json", forceElevation: false);

        Assert.Equal(ServiceCommandStatus.Failed, result.Status);
        Assert.Equal(5, result.ExitCode);
    }

    [Fact]
    public void Stop_Should_Return_ElevationCancelled_When_User_Rejects_Uac()
    {
        var fakeExecutor = new FakeExecutor(new System.ComponentModel.Win32Exception(1223));
        var manager = new ServiceManager(fakeExecutor, new FakeStateProbe
        {
            Exists = true,
            State = ServiceRuntimeState.Stopped
        });

        var result = manager.Stop(forceElevation: true);

        Assert.Equal(ServiceCommandStatus.ElevationCancelled, result.Status);
    }

    [Fact]
    public void Start_Should_Fail_When_Service_Not_Installed()
    {
        var fakeExecutor = new FakeExecutor(ServiceCommandResult.Success());
        var manager = new ServiceManager(fakeExecutor, new FakeStateProbe
        {
            Exists = false,
            State = ServiceRuntimeState.NotInstalled
        });

        var result = manager.Start(@"C:\app\PhotoPrivacyWorker.exe", @"D:\cfg\config.json", forceElevation: false);

        Assert.Equal(ServiceCommandStatus.Failed, result.Status);
        Assert.Equal(1060, result.ExitCode);
    }

    [Fact]
    public void Start_Should_Fail_When_Path_Is_Not_Worker_Executable()
    {
        var fakeExecutor = new FakeExecutor(ServiceCommandResult.Success());
        var manager = new ServiceManager(fakeExecutor, new FakeStateProbe
        {
            Exists = true,
            State = ServiceRuntimeState.Stopped
        });

        var result = manager.Start(@"C:\app\PhotoPrivacy.exe", @"D:\cfg\config.json", forceElevation: false);

        Assert.Equal(ServiceCommandStatus.Failed, result.Status);
        Assert.Contains("PhotoPrivacyWorker.exe", result.Message, StringComparison.Ordinal);
        Assert.Null(fakeExecutor.LastStartInfo);
    }

    [Fact]
    public void Uninstall_Should_Stop_Running_Service_Before_Delete()
    {
        var fakeExecutor = new FakeExecutorWithQueue(new Queue<ServiceCommandResult>(new[]
        {
            ServiceCommandResult.Success(),
            ServiceCommandResult.Success()
        }));

        var manager = new ServiceManager(fakeExecutor, new MutableStateProbe(ServiceRuntimeState.Running));

        var result = manager.Uninstall(forceElevation: false);

        Assert.Equal(ServiceCommandStatus.Success, result.Status);
        Assert.Equal(2, fakeExecutor.Calls.Count);
        Assert.Equal("stop PhotoPrivacyCleaner", fakeExecutor.Calls[0]);
        Assert.Equal("delete PhotoPrivacyCleaner", fakeExecutor.Calls[1]);
    }

    [Fact]
    public void GetRuntimeState_Should_Return_Current_State()
    {
        var manager = new ServiceManager(new FakeExecutor(ServiceCommandResult.Success()), new FakeStateProbe
        {
            Exists = true,
            State = ServiceRuntimeState.Running
        });

        var state = manager.GetRuntimeState();

        Assert.Equal(ServiceRuntimeState.Running, state);
    }

    [Fact]
    public void GetStatusText_Should_Return_NonEmpty_Text_On_Current_Platform()
    {
        var manager = new ServiceManager(new FakeExecutor(ServiceCommandResult.Success()));

        var status = manager.GetStatusText();

        Assert.False(string.IsNullOrWhiteSpace(status));
    }

    [Fact]
    public void GetStatusText_Should_Return_Localized_Text_For_Common_States()
    {
        var manager = new ServiceManager(new FakeExecutor(ServiceCommandResult.Success()), new FakeStateProbe
        {
            Exists = true,
            State = ServiceRuntimeState.Running
        });

        var status = manager.GetStatusText();

        Assert.Equal("运行中", status);
    }

    [Theory]
    [InlineData(0, "操作成功")]
    [InlineData(5, "权限不足")]
    [InlineData(1053, "服务启动超时")]
    [InlineData(1060, "服务不存在")]
    [InlineData(1073, "服务已存在")]
    [InlineData(9999, "未知错误")]
    public void TranslateExitCode_Should_Return_Friendly_Message(int code, string expected)
    {
        var message = ServiceManager.TranslateExitCode(code);

        Assert.Contains(expected, message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRunScStartInfo_Should_Hide_Window_When_No_Elevation_Needed()
    {
        var startInfo = ServiceManager.BuildRunScStartInfo(
            args: "query PhotoPrivacyCleaner",
            requireAdmin: true,
            isAdministrator: true,
            forceElevation: null);

        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(string.Empty, startInfo.Verb);
        Assert.True(startInfo.CreateNoWindow);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
    }

    [Fact]
    public void BuildReconfigArguments_Should_Quote_Executable_And_Config_Path()
    {
        var args = ServiceManager.BuildReconfigArguments(
            @"C:\Program Files\PhotoPrivacy\PhotoPrivacyWorker.exe",
            @"D:\cfg path\config.json");

        Assert.Equal(
            "config PhotoPrivacyCleaner binPath= \"\"C:\\Program Files\\PhotoPrivacy\\PhotoPrivacyWorker.exe\" --mode service --config \"D:\\cfg path\\config.json\"\" start= auto",
            args);
    }

    [Fact]
    public void Install_Should_Auto_Retry_After_Service_Already_Exists()
    {
        var results = new Queue<ServiceCommandResult>(new[]
        {
            ServiceCommandResult.Failed("not running", 1062),
            ServiceCommandResult.Success(),
            ServiceCommandResult.Failed("exists", 1073),
            ServiceCommandResult.Failed("not running", 1062),
            ServiceCommandResult.Success(),
            ServiceCommandResult.Success()
        });

        var fakeExecutor = new FakeExecutorWithQueue(results);
        var fakeProbe = new FakeStateProbe
        {
            Exists = true,
            State = ServiceRuntimeState.Running
        };
        var manager = new ServiceManager(fakeExecutor, fakeProbe);

        var result = manager.Install(@"C:\app\PhotoPrivacyWorker.exe", @"D:\cfg\config.json", forceElevation: false);

        Assert.Equal(ServiceCommandStatus.Success, result.Status);
        Assert.Equal(6, fakeExecutor.Calls.Count);
        Assert.Contains(fakeExecutor.Calls, x => x.Contains("stop PhotoPrivacyCleaner", StringComparison.Ordinal));
        Assert.Contains(fakeExecutor.Calls, x => x.Contains("delete PhotoPrivacyCleaner", StringComparison.Ordinal));
    }

    private sealed class FakeExecutorWithQueue : IScCommandExecutor
    {
        private readonly Queue<ServiceCommandResult> _results;

        public FakeExecutorWithQueue(Queue<ServiceCommandResult> results)
        {
            _results = results;
        }

        public List<string> Calls { get; } = [];

        public ServiceCommandResult Execute(ProcessStartInfo startInfo)
        {
            Calls.Add(startInfo.Arguments);
            return _results.Dequeue();
        }
    }
}
