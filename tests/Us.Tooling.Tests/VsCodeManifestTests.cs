using System.Text.Json;
using System.Text.RegularExpressions;
using Us.Tooling;

namespace Us.Tooling.Tests;

public class VsCodeManifestTests
{
    [Fact]
    public void PackageDeclaresUsLanguageAndUsExtension()
    {
        var root = FindRepositoryRoot();
        var packagePath = Path.Combine(root, "editors", "vscode", "package.json");
        Assert.True(File.Exists(packagePath), packagePath);
        using var document = JsonDocument.Parse(File.ReadAllText(packagePath));
        var rootElement = document.RootElement;
        Assert.Equal("us-language", rootElement.GetProperty("name").GetString());
        Assert.Equal("US Programming Language", rootElement.GetProperty("displayName").GetString());
        Assert.Equal("0.2.0", rootElement.GetProperty("version").GetString());
        Assert.Equal("us-lang", rootElement.GetProperty("publisher").GetString());
        Assert.Equal("./dist/extension.js", rootElement.GetProperty("main").GetString());

        var languages = rootElement.GetProperty("contributes").GetProperty("languages");
        Assert.Contains(languages.EnumerateArray(), language =>
            language.GetProperty("id").GetString() == "us" &&
            language.GetProperty("extensions").EnumerateArray().Any(ext => ext.GetString() == ".us"));

        var configuration = rootElement.GetProperty("contributes").GetProperty("configuration");
        Assert.True(configuration.GetProperty("properties").TryGetProperty("us.executablePath", out _), "us.executablePath setting");
    }

    [Fact]
    public void ExtensionResolvesInstalledUsCliAndReusesDilSunucusu()
    {
        var root = FindRepositoryRoot();
        var extensionPath = Path.Combine(root, "editors", "vscode", "src", "extension.js");
        Assert.True(File.Exists(extensionPath), extensionPath);
        var text = File.ReadAllText(extensionPath);
        Assert.Contains("vscode-languageclient/node", text, StringComparison.Ordinal);
        Assert.Contains("dil-sunucusu", text, StringComparison.Ordinal);
        Assert.Contains("LOCALAPPDATA", text, StringComparison.Ordinal);
        Assert.Contains("executablePath", text, StringComparison.Ordinal);
        Assert.Contains("US", text, StringComparison.Ordinal);
        Assert.Contains("bin", text, StringComparison.Ordinal);
        Assert.DoesNotContain("BLOCKED_EXTERNAL_INTEGRATION", text, StringComparison.Ordinal);
        Assert.DoesNotContain("provideDocumentFormattingEdits", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PackagingScriptsAndInstallCmdExist()
    {
        var root = FindRepositoryRoot();
        Assert.True(File.Exists(Path.Combine(root, "scripts", "package-vscode.ps1")));
        Assert.True(File.Exists(Path.Combine(root, "scripts", "package-win-x64.ps1")));
        Assert.True(File.Exists(Path.Combine(root, "packaging", "windows", "install.cmd")));
        Assert.True(File.Exists(Path.Combine(root, "editors", "vscode", "esbuild.config.js")));
        Assert.True(File.Exists(Path.Combine(root, "editors", "vscode", ".vscodeignore")));

        var helpers = File.ReadAllText(Path.Combine(root, "packaging", "windows", "UsInstall.Helpers.ps1"));
        Assert.Contains("Find-UsCodeCli", helpers, StringComparison.Ordinal);
        Assert.Contains("Install-UsVsCodeExtension", helpers, StringComparison.Ordinal);
        Assert.Contains("Uninstall-UsVsCodeExtension", helpers, StringComparison.Ordinal);
        Assert.Contains("us-lang.us-language", helpers, StringComparison.Ordinal);
        Assert.Contains("us-language-0.2.0.vsix", helpers, StringComparison.Ordinal);

        var install = File.ReadAllText(Path.Combine(root, "packaging", "windows", "install.ps1"));
        Assert.Contains("Install-UsVsCodeExtension", install, StringComparison.Ordinal);
        Assert.Contains("VS Code bulunamadigi icin extension otomatik kurulmadi", helpers, StringComparison.Ordinal);

        var uninstall = File.ReadAllText(Path.Combine(root, "packaging", "windows", "uninstall.ps1"));
        Assert.Contains("Uninstall-UsVsCodeExtension", uninstall, StringComparison.Ordinal);
    }

    [Fact]
    public void BuiltVsixDeclaresVersionAndBundledExtensionWhenPresent()
    {
        var root = FindRepositoryRoot();
        var vsixPath = Path.Combine(root, "artifacts", "vscode", "us-language-0.2.0.vsix");
        if (!File.Exists(vsixPath))
        {
            // Packaging artifacts are produced by scripts/package-vscode.ps1; skip when absent.
            return;
        }

        using var archive = System.IO.Compression.ZipFile.OpenRead(vsixPath);
        var packageEntry = archive.GetEntry("extension/package.json");
        Assert.NotNull(packageEntry);
        using var reader = new StreamReader(packageEntry!.Open());
        using var document = JsonDocument.Parse(reader.ReadToEnd());
        Assert.Equal("0.2.0", document.RootElement.GetProperty("version").GetString());
        Assert.Equal("US Programming Language", document.RootElement.GetProperty("displayName").GetString());
        Assert.NotNull(archive.GetEntry("extension/dist/extension.js"));
        Assert.NotNull(archive.GetEntry("extension/syntaxes/us.tmLanguage.json"));
        Assert.Null(archive.GetEntry("extension/node_modules/vscode-languageclient/package.json"));
    }

    [Fact]
    public void TextMateGrammarDoesNotInventKeywords()
    {
        var root = FindRepositoryRoot();
        var grammarPath = Path.Combine(root, "editors", "vscode", "syntaxes", "us.tmLanguage.json");
        Assert.True(File.Exists(grammarPath), grammarPath);
        using var document = JsonDocument.Parse(File.ReadAllText(grammarPath));
        var match = document.RootElement
            .GetProperty("repository")
            .GetProperty("keywords")
            .GetProperty("match")
            .GetString();
        Assert.False(string.IsNullOrWhiteSpace(match));

        var allowed = UsKeywordCatalog.All.ToHashSet(StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);
        var inner = Regex.Match(match!, @"\\b\((?<alt>.+)\)\\b");
        Assert.True(inner.Success, "keyword match group missing");
        foreach (var part in inner.Groups["alt"].Value.Split('|'))
        {
            found.Add(Regex.Unescape(part));
        }

        var sidecar = Path.Combine(root, "editors", "vscode", "syntaxes", "keywords.from-lexer.txt");
        Assert.True(File.Exists(sidecar), sidecar);
        var sidecarKeywords = File.ReadAllLines(sidecar)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(allowed.OrderBy(item => item, StringComparer.Ordinal), sidecarKeywords.OrderBy(item => item, StringComparer.Ordinal));

        Assert.NotEmpty(found);
        var invented = found.Where(item => !allowed.Contains(item)).ToArray();
        Assert.True(invented.Length == 0, "Invented keywords: " + string.Join(", ", invented));

        foreach (var required in new[] { "eğer", "fonksiyon", "afonksiyon", "bekle", "eşişlem", "olay", "test", "servis" })
        {
            Assert.Contains(required, allowed);
            Assert.Contains(required, found);
        }
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Us.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
