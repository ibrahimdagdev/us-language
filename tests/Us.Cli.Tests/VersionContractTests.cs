using System.Reflection;
using Us.Cli;

namespace Us.Cli.Tests;

public class VersionContractTests
{
    [Fact]
    public async Task CliVersionMatchesInformationalVersion()
    {
        var informational = typeof(UsCli).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(UsCli).Assembly.GetName().Version?.ToString()
            ?? string.Empty;
        Assert.Equal("0.2.0", informational);

        var host = new CliHost(Path.GetTempPath());
        var result = await UsCli.RunAsync(["--version"], host);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("0.2.0", Assert.Single(result.Output));
    }
}
