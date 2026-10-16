namespace Us.Runtime;

public interface IUsClock
{
    DateTime WallNow { get; }
}

public sealed class SystemClock : IUsClock
{
    public static readonly SystemClock Instance = new();

    public DateTime WallNow => DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
}

public sealed class FixedClock : IUsClock
{
    public FixedClock(DateTime wall) => WallNow = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);

    public DateTime WallNow { get; }
}

public interface IUsInput
{
    string? ReadLine();
}

public sealed class EmptyInput : IUsInput
{
    public static readonly EmptyInput Instance = new();

    public string? ReadLine() => null;
}

public sealed class QueueInput : IUsInput
{
    private readonly Queue<string> _lines;

    public QueueInput(IEnumerable<string> lines) => _lines = new Queue<string>(lines);

    public string? ReadLine() => _lines.Count == 0 ? null : _lines.Dequeue();
}

public enum UsLogLevel
{
    Trace,
    Information,
    Warning,
    Error,
    Critical,
}

public sealed record UsLogRecord(UsLogLevel Level, string Message);

public interface IUsLog
{
    void Write(UsLogLevel level, string message);

    IReadOnlyList<UsLogRecord> Records { get; }
}

public sealed class ListLog : IUsLog
{
    private readonly List<UsLogRecord> _records = [];

    public IReadOnlyList<UsLogRecord> Records => _records;

    public void Write(UsLogLevel level, string message) => _records.Add(new UsLogRecord(level, message));
}

public interface IUsConfiguration
{
    int Reads { get; }

    string? ReadText();
}

public sealed class MemoryConfiguration : IUsConfiguration
{
    private readonly string? _text;
    private string? _cached;
    private bool _loaded;

    public MemoryConfiguration(string? text) => _text = text;

    public bool HasText => _text is not null;

    public int Reads { get; private set; }

    public string? ReadText()
    {
        if (_loaded)
        {
            return _cached;
        }

        Reads++;
        _loaded = true;
        _cached = _text;
        return _cached;
    }
}

public interface IUsFileSystem
{
    string Read(string path);

    void Write(string path, string text);

    void Append(string path, string text);

    void Delete(string path);

    bool Exists(string path);

    void CreateDirectory(string path);

    void DeleteDirectory(string path);

    bool DirectoryExists(string path);

    IReadOnlyList<string> ListFiles(string path);

    string Combine(string left, string right);
}

public sealed class UsHost
{
    public IUsInput Input { get; init; } = EmptyInput.Instance;

    public IUsClock Clock { get; init; } = SystemClock.Instance;

    public IUsFileSystem Files { get; init; } = new MemoryFileSystem();

    public IUsConfiguration Configuration { get; init; } = new MemoryConfiguration(null);

    public IUsLog Log { get; init; } = new ListLog();

    public IUsScheduler Scheduler { get; init; } = TaskDelayScheduler.Instance;

    public IHttpTransport Transport { get; init; } = new FakeHttpTransport();

    public List<object> Services { get; } = [];
}
