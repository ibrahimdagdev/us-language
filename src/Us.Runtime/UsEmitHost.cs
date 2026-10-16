using System.Globalization;

namespace Us.Runtime;

/// <summary>
/// Process-wide host defaults for emitted IL. Mirrors <see cref="UsHost"/> defaults;
/// not an interpreter wrapper.
/// </summary>
public static class UsEmitHost
{
    public static IUsClock Clock { get; set; } = SystemClock.Instance;

    public static IUsFileSystem Files { get; set; } = new MemoryFileSystem();

    public static IUsConfiguration Configuration { get; set; } = new MemoryConfiguration(null);

    public static IUsLog Log { get; set; } = new ListLog();

    public static IHttpTransport Transport { get; set; } = new FakeHttpTransport();

    public static List<object> Services { get; } = [];

    public static void BootstrapFromEnvironment()
    {
        var clock = Environment.GetEnvironmentVariable("US_TEST_CLOCK");
        if (!string.IsNullOrWhiteSpace(clock))
        {
            Clock = new FixedClock(DateTime.Parse(clock, CultureInfo.InvariantCulture));
        }

        var config = Environment.GetEnvironmentVariable("US_TEST_CONFIG");
        if (config is not null)
        {
            Configuration = new MemoryConfiguration(config);
        }

        UsEmitHttp.BootstrapTestHttpFromEnvironment();

        // Real outbound transport when explicitly requested; Fake remains default for tests/CI.
        if (string.Equals(Environment.GetEnvironmentVariable("US_HTTP_CLIENT"), "1", StringComparison.Ordinal))
        {
            Transport = new HttpClientTransport();
        }
    }

    /// <summary>Install production <see cref="HttpClientTransport"/> (idempotent for hosts).</summary>
    public static void UseHttpClientTransport() => Transport = new HttpClientTransport();
}
