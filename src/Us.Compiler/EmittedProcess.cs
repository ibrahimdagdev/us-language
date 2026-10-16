using System.Diagnostics;
using System.Text;

namespace Us.Compiler;

public sealed class EmittedProcessResult
{
    public EmittedProcessResult(int exitCode, string standardOutput, string standardError)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    public int ExitCode { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }
}

public static class EmittedProcess
{
    public static EmittedProcessResult Run(
        string assemblyPath,
        int timeoutMilliseconds = 15000,
        string? standardInput = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("Derleme çıktısı bulunamadı.", assemblyPath);
        }

        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = Quote(assemblyPath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(assemblyPath) ?? Environment.CurrentDirectory,
        };
        start.Environment["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "1";
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                start.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet süreci başlatılamadı.");
        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            throw new TimeoutException("Derlenmiş program zaman aşımına uğradı.");
        }

        return new EmittedProcessResult(process.ExitCode, stdout.Replace("\r\n", "\n", StringComparison.Ordinal), stderr.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static string Quote(string path) =>
        path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;
}
