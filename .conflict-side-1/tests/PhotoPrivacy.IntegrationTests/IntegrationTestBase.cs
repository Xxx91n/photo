namespace PhotoPrivacy.IntegrationTests;

public abstract class IntegrationTestBase
{
    protected static string RequireExifTool()
    {
        return ExifToolLocator.TryResolve();
    }
}
