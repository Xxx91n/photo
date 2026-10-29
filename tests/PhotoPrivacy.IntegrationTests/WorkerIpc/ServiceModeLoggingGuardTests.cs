namespace PhotoPrivacy.IntegrationTests.WorkerIpc;

/// <summary>
/// 回潮守卫（#85）：服务进程不得保留 <c>AddWindowsService</c> 隐式登记的 EventLog provider。
/// 服务会话下该 provider 写失败（<c>Cannot open log for source</c>，Win32 1722）会让 MEL
/// <c>Logger.Log</c> 直接抛 <c>AggregateException</c>；ReloadConfig 的 catch 链每层都在打日志，
/// 于是异常炸穿 IPC 响应路径 → 服务管道 ReloadConfig 挂起。
///
/// 摘除手段是「<c>AddWindowsService</c> 之后紧跟 <c>builder.Logging.ClearProviders()</c>」，
/// 因此断言必须校验**顺序**：MEL 的 <c>AddFilter(string, …)</c> 首参是类别前缀而非 provider 名
/// （net10 参考程序集无 provider-name 重载），靠类别过滤摘不掉这个 provider。
///
/// 同一失败面还有第二段：EventLog 摘掉后，Serilog 文件是服务模式的唯一诊断落点，落点必须是
/// 可写绝对路径（服务进程 CWD 是 <c>%WINDIR%\System32</c>），由 <c>ClassifyServiceLogPath</c> 守。
/// </summary>
public sealed class ServiceModeLoggingGuardTests
{
    private const string ClearProvidersToken = "builder.Logging.ClearProviders()";
    private const string AddWindowsServiceToken = "AddWindowsService";

    internal static List<string> ClassifyServiceLogging(string strippedProgramWithLineBreaks)
    {
        var violations = new List<string>();
        var windowsServiceIndex = strippedProgramWithLineBreaks.IndexOf(AddWindowsServiceToken, StringComparison.Ordinal);
        var clearProvidersIndex = strippedProgramWithLineBreaks.IndexOf(ClearProvidersToken, StringComparison.Ordinal);

        if (windowsServiceIndex < 0)
        {
            violations.Add("Program.cs 不再注册 AddWindowsService：守卫前提失效，需复审本守卫");
        }

        if (clearProvidersIndex < 0)
        {
            violations.Add(
                "Program.cs 缺少 builder.Logging.ClearProviders()：AddWindowsService 隐式挂载的 EventLog provider 会留在服务进程里（#85 挂起形态可回潮）");
        }
        else if (windowsServiceIndex >= 0 && clearProvidersIndex < windowsServiceIndex)
        {
            violations.Add(
                "ClearProviders 排在 AddWindowsService 之前：EventLog provider 会在其后重新登记，等于未摘除（#85）");
        }

        if (strippedProgramWithLineBreaks.Contains("AddEventLog", StringComparison.Ordinal))
        {
            violations.Add("Program.cs 直接登记 EventLog provider：ClearProviders 之后重新挂载同样会让服务会话抛异常（#85）");
        }

        return violations;
    }

    internal static List<string> ClassifyServiceLogPath(string strippedProgramWithLineBreaks)
    {
        var violations = new List<string>();

        if (!strippedProgramWithLineBreaks.Contains("CommonApplicationData", StringComparison.Ordinal))
        {
            violations.Add(
                "服务日志落点不再是 ProgramData 绝对路径：服务进程 CWD 是 %WINDIR%\\System32，受限服务账户写不进去（#85）");
        }

        // 变更探测器（非契约）：解析出绝对路径但 Serilog 仍写相对字面量，等于修复被旁路。
        // 计数口径 = 声明一处 + 文件 sink 消费一处（ResolveLogFilePath 是大小写不同的标识符，不计入）。
        var logFilePathOccurrences = strippedProgramWithLineBreaks
            .Split("logFilePath", StringSplitOptions.None).Length - 1;
        if (logFilePathOccurrences < 2)
        {
            violations.Add("Serilog 文件 sink 未消费 logFilePath（仍写相对 logs/worker-.log）：绝对路径解析形同虚设（#85）");
        }

        return violations;
    }

    [Fact]
    public void Service_Mode_Should_Drop_EventLog_Provider_After_Registering_WindowsService()
    {
        // 防的具体回潮形态：① 删掉 ClearProviders（EventLog provider 随 AddWindowsService 重新挂上）；
        // ② 把 ClearProviders 挪到 AddWindowsService 之前（同样留活口）；③ 之后又显式 AddEventLog；
        // ④ 换成 AddFilter("EventLog", …) —— 该重载首参是类别前缀，摘不掉 provider，且此时
        // ClearProviders 已缺失。①–④ 都会被本断言判红，自证只对 ①②③ 造样本（④ 与 ① 同因：缺 ClearProviders）。关联 issue #85。
        var violations = ClassifyServiceLogging(ReadStrippedProgramWithLineBreaks());
        Assert.True(violations.Count == 0, string.Join("；", violations));
    }

    [Fact]
    public void Service_Mode_Should_Log_To_A_Writable_Absolute_Path()
    {
        // 防的具体回潮形态：把服务日志退回相对 "logs/worker-.log" —— 服务进程 CWD 是
        // %WINDIR%\System32，受限服务账户写不进去；而 EventLog 已被本票摘除，文件日志是
        // 服务模式的唯一诊断落点，写不进去等于服务静默失明。关联 issue #85。
        var violations = ClassifyServiceLogPath(ReadStrippedProgramWithLineBreaks());
        Assert.True(violations.Count == 0, string.Join("；", violations));
    }

    [Fact]
    public void Service_Log_Path_Guard_Negative_Self_Proof()
    {
        // 失效即红自证：绿态零违规；① 删掉 ProgramData 解析、② 保留解析但把 sink 换回相对字面量
        // （死代码绕道），两种回潮形态都必须报红。关联 issue #85。
        var green = ReadStrippedProgramWithLineBreaks();
        Assert.Empty(ClassifyServiceLogPath(green));

        var withoutProgramData = string.Join(
            "\n",
            green.Split('\n').Where(line => !line.Contains("CommonApplicationData", StringComparison.Ordinal)));
        Assert.NotEmpty(ClassifyServiceLogPath(withoutProgramData));

        var sinkRevertedToLiteral = green.Replace(
            "    logFilePath,",
            "    \"logs/worker-.log\",",
            StringComparison.Ordinal);
        Assert.NotEmpty(ClassifyServiceLogPath(sinkRevertedToLiteral));
    }

    [Fact]
    public void Service_Logging_Guard_Negative_Self_Proof()
    {
        // 失效即红自证：绿态必须零违规；删掉 ClearProviders、把它挪到 AddWindowsService 之前、
        // 或在其后重新 AddEventLog，分类器都必须报红——防「摘除被删而守卫仍绿」的恒绿事故。
        // 关联 issue #85。
        var green = ReadStrippedProgramWithLineBreaks();
        Assert.Empty(ClassifyServiceLogging(green));

        var lines = green.Split('\n');
        var withoutClear = string.Join(
            "\n",
            lines.Where(line => !line.Contains(ClearProvidersToken, StringComparison.Ordinal)));
        Assert.NotEmpty(ClassifyServiceLogging(withoutClear));

        var clearLine = lines.First(line => line.Contains(ClearProvidersToken, StringComparison.Ordinal));
        Assert.NotEmpty(ClassifyServiceLogging(clearLine + "\n" + withoutClear));

        Assert.NotEmpty(ClassifyServiceLogging(green + "\nbuilder.Logging.AddEventLog();"));
    }

    /// <summary>
    /// <c>SourceLint.ReadStripped</c> 逐行剥注释后 <c>string.Concat</c>，产物不含换行——用于
    /// 「存在性」断言足够，但本守卫要判**顺序**并对样本做增删，必须保留行结构，故在此按行拼接。
    /// </summary>
    private static string ReadStrippedProgramWithLineBreaks()
        => string.Join(
            "\n",
            SourceLint.Read("src", "PhotoPrivacy.Worker", "Program.cs")
                .Split('\n')
                .Select(line => SourceLint.StripLineComment(line.TrimEnd('\r'))));
}
