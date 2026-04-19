using System.Reflection;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class UiAvailabilityTests
{
    [Fact]
    public void UiProgram_Type_Should_Be_Resolvable_From_Cli_References()
    {
        var type = Type.GetType("PhotoPrivacy.Ui.UiProgram, PhotoPrivacy.Ui", throwOnError: false);

        Assert.NotNull(type);
    }

    [Fact]
    public void TrayContext_Type_Should_Not_Exist_After_WinForms_Removal()
    {
        var type = typeof(PhotoPrivacy.Cli.RuntimeMode).Assembly.GetType("PhotoPrivacy.Cli.TrayApplicationContext");

        Assert.Null(type);
    }
}
