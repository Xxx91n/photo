namespace PhotoPrivacy.IntegrationTests.WorkerIpc;

/// <summary>
/// 回潮守卫（#85）：服务模式必须停用 <c>AddWindowsService</c> 隐式挂载的 EventLog provider。
/// 服务会话下该 provider 写失败（<c>Cannot open log for source</c>）会让 MEL <c>Logger.Log</c>
/// 直接抛 <c>AggregateException</c>；ReloadConfig 的 catch 链每层都在打日志，
/// 于是异常炸穿 IPC 响应路径 → 服务管道 ReloadConfig 挂起。
/// </summary>
public sealed class ServiceModeLoggingGuardTests
{
    internal static List<string> ClassifyServiceLogging(string strippedProgram)
    {
        var violations = new List<string>();

        if (!strippedProgram.Contains("AddWindowsService", StringComparison.Ordinal))
        {
            violations.Add("Program.cs 不再注册 AddWindowsService：守卫前提失效，需复审本守卫");
        }

        if (!strippedProgram.Contains("AddFilter(\"EventLog\"", StringComparison.Ordinal))
        {
            violations.Add("Program.cs 缺少 EventLog provider 停用过滤：服务会话写失败会重新抛（#85 挂起形态可回潮）");
        }

        return violations;
    }

    [Fact]
    public void Service_Mode_Should_Disable_EventLog_Logging_Provider()
    {
        // 防的具体回潮形态：把这行过滤删掉、改成 `(_ => true)` / `(l => l >= LogLevel.Warning)`，
        // 或把它挪到 AddWindowsService 之前（规则顺序失效）—— 任一形态都会让服务会话下
        // EventLog 写失败重新抛异常，沿 ReloadConfig 的 catch 链炸穿 IPC 响应路径
        // （#85 实测：服务管道 ReloadConfig 10 s/15 s 无响应，其余方法正常）。关联 issue #85。
        var violations = ClassifyServiceLogging(SourceLint.ReadStripped("src", "PhotoPrivacy.Worker", "Program.cs"));
        Assert.True(violations.Count == 0, string.Join("；", violations));
    }

    [Fact]
    public void Service_Logging_Guard_Negative_Self_Proof()
    {
        // 失效即红自证：从绿态样本里移除停用过滤，分类器必须报红
        // —— 防「过滤被删而守卫仍绿」的恒绿事故。
        var green = SourceLint.ReadStripped("src", "PhotoPrivacy.Worker", "Program.cs");
        Assert.Empty(ClassifyServiceLogging(green));

        var withoutFilter = string.Join(
            "\n",
            green.Split('\n').Where(line => !line.Contains("AddFilter(\"EventLog\"", StringComparison.Ordinal)));

        Assert.NotEmpty(ClassifyServiceLogging(withoutFilter));
    }
}
