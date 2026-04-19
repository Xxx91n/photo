using Avalonia;

namespace PhotoPrivacy.Ui;

public static class UiProgram
{
    public static BackgroundUiOptions Options { get; set; } = BackgroundUiOptions.CreateFallback();

    public static void Main(string[] args)
    {
        Start(args);
    }

    public static int Start(string[] args)
    {
        App.RuntimeOptions = Options;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
