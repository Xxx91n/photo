using PhotoPrivacy.Core.Audit;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace PhotoPrivacy.Core.Tests.Audit;

public class PathMaskingTests
{
    [Theory]
    [InlineData("C:\\Users\\john\\Pictures\\test.jpg", "C:\\Users\\[redacted]\\Pictures\\test.jpg")]
    [InlineData("/home/john/photos/test.jpg", "/home/[redacted]/photos/test.jpg")]
    public void Mask_Should_Redact_Username_After_Users(string input, string expected)
    {
        var result = PathMasker.Mask(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Mask_Should_Return_Empty_Or_Whitespace_Unchanged(string input)
    {
        Assert.Equal(input, PathMasker.Mask(input));
    }

    [Fact]
    public void Mask_Should_Not_Redact_When_No_Users_Segment()
    {
        var input = "D:\\Photos\\test.jpg";
        Assert.Equal(input, PathMasker.Mask(input));
    }

    [Fact]
    public void StableHash_Should_Return_Consistent_Hex()
    {
        var path = "C:\\Users\\john\\test.jpg";
        var h1 = PathMasker.StableHash(path);
        var h2 = PathMasker.StableHash(path);
        Assert.Equal(h1, h2);
        Assert.Equal(16, h1.Length);
    }

    [Fact]
    public void PathMaskingEnricher_Should_Mask_SourcePath_In_LogEvent()
    {
        var enricher = new PathMaskingEnricher();
        var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var properties = new List<LogEventProperty>
        {
            new("SourcePath", new ScalarValue("C:\\Users\\alice\\photo.jpg"))
        };
        var logEvent = new LogEvent(timestamp, LogEventLevel.Information, null,
            new Serilog.Parsing.MessageTemplateParser().Parse("{SourcePath}"), properties);
        var propFactory = new ConcretePropertyFactory();

        enricher.Enrich(logEvent, propFactory);

        var masked = (ScalarValue)logEvent.Properties["SourcePath"];
        Assert.Equal("C:\\Users\\[redacted]\\photo.jpg", masked.Value);
    }

    [Fact]
    public void PathMaskingEnricher_Should_Not_Mask_NonPath_Properties()
    {
        var enricher = new PathMaskingEnricher();
        var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var properties = new List<LogEventProperty>
        {
            new("Count", new ScalarValue(42L)),
            new("FilePath", new ScalarValue("/home/bob/file.txt"))
        };
        var logEvent = new LogEvent(timestamp, LogEventLevel.Information, null,
            new Serilog.Parsing.MessageTemplateParser().Parse("{Count} {FilePath}"), properties);
        var propFactory = new ConcretePropertyFactory();

        enricher.Enrich(logEvent, propFactory);

        var countVal = (ScalarValue)logEvent.Properties["Count"];
        Assert.Equal(42L, countVal.Value);
        var fileVal = (ScalarValue)logEvent.Properties["FilePath"];
        Assert.Equal("/home/[redacted]/file.txt", fileVal.Value);
    }

    private sealed class ConcretePropertyFactory : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
            => new(name, new ScalarValue(value));
    }
}
