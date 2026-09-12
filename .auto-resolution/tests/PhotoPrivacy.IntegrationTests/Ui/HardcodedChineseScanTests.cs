using System.Text.RegularExpressions;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// ADR 0047 test closure — scan src/PhotoPrivacy.Ui source for hardcoded CJK string literals
/// outside comments. With full i18n extraction, no .cs (except LocalizationService BuiltIn
/// dictionaries which are the embedded-resource fallback by design) or .axaml (except the
/// language picker whose ComboBoxItem Content natively shows each language name in itself)
/// should embed Chinese in string literals. This is a regression guard.
/// </summary>
public sealed class HardcodedChineseScanTests
{

    private static readonly HashSet<string> ExcludedCsFiles = new()
    {
        "LocalizationService.cs"   // BuiltInZhCN/BuiltInEn dictionaries are legitimate fallback
    };

    [Fact]
    public void No_Hardcoded_Chinese_In_Cs_Source_Outside_Comments()
    {
        Assert.True(Directory.Exists(SourceLint.UiSourceRoot), $"Ui source root not found: {SourceLint.UiSourceRoot}");
        var csFiles = Directory.GetFiles(SourceLint.UiSourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                        !f.EndsWith(".g.cs") && !f.EndsWith(".GlobalUsings.g.cs") &&
                        !ExcludedCsFiles.Contains(Path.GetFileName(f)))
            .ToList();
        Assert.NotEmpty(csFiles);

        var cjkPattern = new Regex("[\u4e00-\u9fff]");
        var stringLiteralPattern = new Regex(@"""([^""\\]|\\.)*""");
        var violations = new List<string>();

        foreach (var file in csFiles)
        {
            var lines = File.ReadAllLines(file);
            foreach (var raw in lines)
            {
                var line = SourceLint.StripLineComment(raw);

                foreach (Match m in stringLiteralPattern.Matches(line))
                {
                    var captured = m.Groups[1].Value;
                    if (cjkPattern.IsMatch(captured))
                    {
                        violations.Add($"{Path.GetFileName(file)}: {raw.Trim()}");
                        break;
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Hardcoded CJK string literals found in {violations.Count} locations:{Environment.NewLine}{string.Join(Environment.NewLine, violations.Take(30))}");
    }

    [Fact]
    public void No_Hardcoded_Chinese_In_Axaml_Outside_Language_Picker()
    {
        Assert.True(Directory.Exists(SourceLint.UiSourceRoot), $"Ui source root not found: {SourceLint.UiSourceRoot}");
        var axamlFiles = Directory.GetFiles(SourceLint.UiSourceRoot, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .ToList();

        var cjkPattern = new Regex("[\u4e00-\u9fff\u3040-\u30ff\uac00-\ud7af\u0400-\u04ff]");
        var attrValuePattern = new Regex(@"\b(Text|Header|Title|Content|ToolTip|Watermark|PlaceholderText|Tag|ButtonText)\s*=\s*""([^""]*)""");

        // ComboBoxItem entries inside the language picker LocaleVariantComboBox show each
        // language's endonym (zhongwen / English / Japanese / Korean / ...) - by design.
        var localePickerComboBoxItemPattern = new Regex(@"<ComboBoxItem\s+Content=""[^""]*""\s+Tag=""[a-zA-Z\-]+""");

        var violations = new List<string>();

        foreach (var file in axamlFiles)
        {
            var content = File.ReadAllText(file);
            var noComments = Regex.Replace(content, @"<!--.*?-->", "", RegexOptions.Singleline);
            noComments = localePickerComboBoxItemPattern.Replace(noComments, "");

            foreach (Match m in attrValuePattern.Matches(noComments))
            {
                var value = m.Groups[2].Value;
                if (cjkPattern.IsMatch(value))
                {
                    violations.Add($"{Path.GetFileName(file)}: {m.Value.Trim()}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Hardcoded CJK in AXAML attributes found in {violations.Count} locations:{Environment.NewLine}{string.Join(Environment.NewLine, violations.Take(20))}");
    }
}
