using System.Diagnostics;
using System.Text;
using Us.Cli;

namespace Us.Cli.Tests;

public class PackagingLayoutTests
{
    [Fact]
    public void WindowsPackagingFilesExist()
    {
        var root = FindRepo();
        string[] required =
        [
            Path.Combine(root, "packaging", "windows", "install.ps1"),
            Path.Combine(root, "packaging", "windows", "uninstall.ps1"),
            Path.Combine(root, "packaging", "windows", "install.cmd"),
            Path.Combine(root, "packaging", "windows", "UsInstall.Helpers.ps1"),
            Path.Combine(root, "packaging", "windows", "README.txt"),
            Path.Combine(root, "packaging", "windows", "Test-UsInstall.Helpers.ps1"),
            Path.Combine(root, "scripts", "package-win-x64.ps1"),
            Path.Combine(root, "scripts", "package-vscode.ps1"),
        ];

        foreach (var path in required)
        {
            Assert.True(File.Exists(path), path);
        }
    }

    [Fact]
    public void CliProjectKeepsUsCliAssemblyAndRenamesAppHost()
    {
        var csproj = File.ReadAllText(Path.Combine(FindRepo(), "src", "Us.Cli", "Us.Cli.csproj"));
        Assert.Contains("<AssemblyName>Us.Cli</AssemblyName>", csproj, StringComparison.Ordinal);
        Assert.Contains("RenameUsCliAppHost", csproj, StringComparison.Ordinal);
        Assert.Contains("us$(_NativeExecutableExtension)", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildOutputExposesUsExeAndUsCliDll()
    {
        var repo = FindRepo();
        var dir = Path.Combine(repo, "src", "Us.Cli", "bin", "Release", "net10.0");
        if (!Directory.Exists(dir))
        {
            dir = Path.Combine(repo, "src", "Us.Cli", "bin", "Debug", "net10.0");
        }

        Assert.True(Directory.Exists(dir), dir);
        Assert.True(File.Exists(Path.Combine(dir, "Us.Cli.dll")), "Us.Cli.dll");
        Assert.True(File.Exists(Path.Combine(dir, "us.exe")), "us.exe");
        Assert.False(File.Exists(Path.Combine(dir, "Us.Cli.exe")), "Us.Cli.exe should not remain in CLI output");
    }

    internal static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Us.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }

    internal static string FindUsExe()
    {
        var besideTests = Path.Combine(AppContext.BaseDirectory, "us.exe");
        if (File.Exists(besideTests))
        {
            return besideTests;
        }

        var repo = FindRepo();
        foreach (var candidate in Directory.EnumerateFiles(
                     Path.Combine(repo, "src", "Us.Cli", "bin"),
                     "us.exe",
                     SearchOption.AllDirectories))
        {
            return candidate;
        }

        throw new FileNotFoundException("us.exe not found in test output or Us.Cli build output.");
    }
}

public class PublicCommandContractTests
{
    [Fact]
    public async Task VersionIsExactly020()
    {
        var host = new CliHost(Path.GetTempPath());
        var result = await UsCli.RunAsync(["--version"], host);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("0.2.0", Assert.Single(result.Output));
    }

    [Fact]
    public async Task UsExeProcessReportsVersionCreateRunAndCompile()
    {
        var usExe = PackagingLayoutTests.FindUsExe();

        var version = RunCaptured(usExe, "--version", Path.GetDirectoryName(usExe)!);
        Assert.Equal(0, version.ExitCode);
        Assert.Equal("0.2.0", version.Stdout.Trim());

        var workspace = Path.Combine(Path.GetTempPath(), "us-cmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var created = RunCaptured(usExe, "yeni konsol Merhaba", workspace);
            Assert.Equal(0, created.ExitCode);

            var project = Path.Combine(workspace, "Merhaba");
            var run = RunCaptured(usExe, "çalıştır", project);
            Assert.Equal(0, run.ExitCode);
            Assert.Contains("Merhaba US!", run.Stdout, StringComparison.Ordinal);

            var compile = RunCaptured(usExe, "derle", project);
            Assert.Equal(0, compile.ExitCode);

            var dll = Path.Combine(project, "cikti", "UsProgram.dll");
            Assert.True(File.Exists(dll), dll);
            var compiled = RunCaptured("dotnet", Quote(dll), project);
            Assert.Equal(0, compiled.ExitCode);
            Assert.Contains("Merhaba US!", compiled.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(workspace, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    private static string Quote(string path) => path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;

    private static (int ExitCode, string Stdout, string Stderr) RunCaptured(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {fileName}");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(120_000);
        return (process.ExitCode, stdout, stderr);
    }
}

public class InstallHelperScriptTests
{
    [Fact]
    public void PathHelpersAreIdempotentAndPreserveOtherEntries()
    {
        var script = Path.Combine(
            PackagingLayoutTests.FindRepo(),
            "packaging",
            "windows",
            "Test-UsInstall.Helpers.ps1");

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("powershell failed to start");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(60_000);

        Assert.True(process.ExitCode == 0, stderr + Environment.NewLine + stdout);
        Assert.Contains("OK", stdout, StringComparison.Ordinal);
    }
}
