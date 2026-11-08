using PhotoPrivacy.Core.Constants;

namespace PhotoPrivacy.Core.Tests.Constants;

public sealed class DefaultPathsTests
{
    // 要防的老毛病（#108 审查，CodeRabbit #155）：断言把 Windows 默认值钉死成 Program Files——
    // 发布包自带 ExifTool/ 与 PATH 探测之后，任一前置候选命中都会合法地改变返回值，钉死它只会随安装环境误红。
    // 保留仍成立的部分：Windows 侧必须是绝对路径（校验器要求）；Unix 侧允许裸 "exiftool" 作为
    // 「交给 PATH/execvp 解析」的保底。
    [Fact]
    public void ExifToolPath_Should_Resolve_To_An_Absolute_Path_Or_Bare_Command()
    {
        var path = DefaultPaths.ExifToolPath;

        if (OperatingSystem.IsWindows())
        {
            Assert.True(Path.IsPathFullyQualified(path), $"unexpected ExifToolPath: {path}");
        }
        else
        {
            Assert.True(
                Path.IsPathFullyQualified(path) || Path.GetFileName(path) == path,
                $"unexpected ExifToolPath: {path}");
        }
    }

    // 要防的老毛病（#108 审查，CodeRabbit #155）：Unix 候选把包根裸 exiftool 排在 ExifTool/ 之前。
    // 包内那份自带同级 lib/，包根裸文件通常没有；顺序反了会选中缺运行时目录的副本，
    // AppConfigValidator 随即以「缺 exiftool_files/lib」拒绝启用监控，而 UI 侧选中的却是可用的那份。
    [Fact]
    public void Unix_Candidates_Should_Prefer_The_App_Local_ExifTool_Directory()
    {
        var candidates = DefaultPaths.UnixCandidates();
        var appDirectory = AppContext.BaseDirectory;

        Assert.Equal(Path.Combine(appDirectory, "ExifTool", "exiftool"), candidates[0]);
        Assert.Equal(Path.Combine(appDirectory, "exiftool"), candidates[1]);
        // 系统安装位置仍作为后续候选保留（顺序：/usr/bin → /usr/local/bin → /opt/homebrew/bin）。
        Assert.Contains("/usr/bin/exiftool", candidates);
    }

    // 要防的老毛病（#108 审查，CodeRabbit #155）：PATH 解析接受相对片段，返回一个依赖工作目录的
    // 可执行路径——校验器随即以「must be absolute」拒绝启用监控，而 UI 侧显示「已自动检测到」。
    // 相对片段必须被跳过，只认绝对路径片段。
    [Fact]
    public void FindExifToolInPathValue_Should_Skip_Relative_Entries_And_Return_The_Absolute_Hit()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pp-path-" + Guid.NewGuid().ToString("N"));
        var fileName = OperatingSystem.IsWindows() ? "exiftool.exe" : "exiftool";
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), "stub");

        try
        {
            var separator = Path.PathSeparator;
            var pathValue = $"relative{separator}{directory}{separator}/pp-nonexistent-root";

            Assert.Equal(
                Path.Combine(directory, fileName),
                DefaultPaths.FindExifToolInPathValue(pathValue, fileName));

            Assert.Null(DefaultPaths.FindExifToolInPathValue(null, fileName));
            Assert.Null(DefaultPaths.FindExifToolInPathValue(string.Empty, fileName));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // #33: exiftool.path 目录式覆盖——给定已安装的 ExifTool 目录，解析出其中的
    // exiftool/exiftool.exe（对齐 ExifToolGUI 的 ETOverrideDir）。
    [Fact]
    public void ResolveExifToolExecutable_Should_Resolve_Directory_To_Contained_Exe()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pp-etoverride-" + Guid.NewGuid().ToString("N"));
        var exeName = OperatingSystem.IsWindows() ? "exiftool.exe" : "exiftool";
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, exeName), "stub");

        try
        {
            Assert.Equal(
                Path.Combine(directory, exeName),
                DefaultPaths.ResolveExifToolExecutable(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // #33: 用户给的是 exiftool 可执行文件本身的绝对路径时，原样返回，不画蛇添足。
    [Fact]
    public void ResolveExifToolExecutable_Should_Return_Existing_File_AsIs()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pp-etoverride-" + Guid.NewGuid().ToString("N"));
        var exeName = OperatingSystem.IsWindows() ? "exiftool.exe" : "exiftool";
        Directory.CreateDirectory(directory);
        var exePath = Path.Combine(directory, exeName);
        File.WriteAllText(exePath, "stub");

        try
        {
            Assert.Equal(exePath, DefaultPaths.ResolveExifToolExecutable(exePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // #33: 既非文件也非目录的无效值原样返回，交由 AppConfigValidator 报 not found。
    [Fact]
    public void ResolveExifToolExecutable_Should_Return_Nonexistent_Path_AsIs()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "pp-etoverride-" + Guid.NewGuid().ToString("N") + "-gone");
        Assert.Equal(bogus, DefaultPaths.ResolveExifToolExecutable(bogus));
        Assert.Equal(string.Empty, DefaultPaths.ResolveExifToolExecutable(string.Empty));
    }

    // DefaultHotFolder must be app-local (BaseDirectory/hot), not user's system pictures
    [Fact]
    public void DefaultHotFolder_Should_Be_App_Local_Hot_Directory()
    {
        var expected = System.IO.Path.Combine(AppContext.BaseDirectory, "hot");
        Assert.Equal(expected, DefaultPaths.DefaultHotFolder);
    }

    // DefaultBackupDirectory subdir must be "bak" (aligned with BackupPathResolver.DefaultBackupDirName)
    [Fact]
    public void DefaultBackupDirectory_Should_Use_Bak_Subdir_Aligned_With_BackupPathResolver()
    {
        var expected = System.IO.Path.Combine(DefaultPaths.DefaultHotFolder, "bak");
        Assert.Equal(expected, DefaultPaths.DefaultBackupDirectory);
    }

}
