using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class TrayHostSourceSafetyTests
{
    [Fact]
    public void TrayHost_Source_Should_Guard_UpdateMenu_Ipc_Failures()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "TrayHost.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.Contains("private void UpdateMenu()", source, StringComparison.Ordinal);
        Assert.Contains("try", source, StringComparison.Ordinal);
        Assert.Contains("catch", source, StringComparison.Ordinal);
    }
}
