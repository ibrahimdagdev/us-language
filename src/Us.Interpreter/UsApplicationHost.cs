using Us.Core;
using Us.Hosting;
using Us.Runtime;

namespace Us.Interpreter;

public static class UsApplicationHost
{
    public static ExecutionResult Start(string root, SourceFile file, UsHost? host = null) =>
        Task.Run(async () => await StartAsync(root, file, host).ConfigureAwait(false)).GetAwaiter().GetResult();

    public static async ValueTask<ExecutionResult> StartAsync(string root, SourceFile file, UsHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        return await StartAsync(root, [file], file, host).ConfigureAwait(false);
    }

    public static async ValueTask<ExecutionResult> StartAsync(string root, IReadOnlyList<SourceFile> files, SourceFile entry, UsHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(entry);
        host ??= new UsHost();
        if (host.Configuration is MemoryConfiguration memory && !memory.HasText)
        {
            var path = Path.Combine(root, "uygulamaAyar.json");
            if (File.Exists(path))
            {
                host = new UsHost
                {
                    Input = host.Input,
                    Clock = host.Clock,
                    Files = host.Files,
                    Configuration = new MemoryConfiguration(File.ReadAllText(path)),
                    Log = host.Log,
                    Scheduler = host.Scheduler,
                    Transport = host.Transport,
                };
            }
        }

        host.Configuration.ReadText();
        var result = await ProgramRunner.RunAsync(files, entry, null, host).ConfigureAwait(false);
        if (!result.Evaluated || result.Diagnostics.Count > 0)
        {
            return result;
        }

        await MaybeListenWebAsync(files, entry, host).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// When the bound program has routes and Kestrel is not disabled, keep the process alive
    /// on a real listener (interpreter <c>us çalıştır</c> / <c>hedef web</c> path).
    /// </summary>
    private static async Task MaybeListenWebAsync(IReadOnlyList<SourceFile> files, SourceFile entry, UsHost host)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("US_TEST_ROUTE")))
        {
            return;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("US_KESTREL"), "0", StringComparison.Ordinal))
        {
            return;
        }

        var compiled = UsCompile.Front(files);
        if (compiled.Errors.Length > 0 || compiled.Compilation.Routes.IsDefaultOrEmpty)
        {
            return;
        }

        var web = new UsWebHost(compiled.Compilation, entry, host);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, args) =>
        {
            args.Cancel = true;
            cts.Cancel();
        };

        await using var kestrel = await UsKestrelHost.StartAsync(
            request => web.DispatchAsync(request).AsTask(),
            cancellationToken: cts.Token).ConfigureAwait(false);
        Console.Out.WriteLine("US dinliyor: " + kestrel.BaseAddress.AbsoluteUri);
        await kestrel.RunUntilCancelledAsync(cts.Token).ConfigureAwait(false);
    }
}
