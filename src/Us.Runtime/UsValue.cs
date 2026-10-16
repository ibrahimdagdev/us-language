using System.Globalization;

namespace Us.Runtime;

public enum UsValueKind
{
    Unit,
    Null,
    Bool,
    Char,
    Int,
    UInt,
    Float64,
    Float32,
    Decimal,
    Text,
    Object,
}

public readonly record struct UsValue
{
    private UsValue(UsValueKind kind, bool boolean = false, char character = '\0', int signed = 0, uint unsigned = 0, double float64 = 0, float float32 = 0, decimal high = 0, string? text = null, object? reference = null)
    {
        Kind = kind;
        Bool = boolean;
        Char = character;
        Int = signed;
        UInt = unsigned;
        Float64 = float64;
        Float32 = float32;
        Decimal = high;
        Text = text;
        Reference = reference;
    }

    public UsValueKind Kind { get; }

    public bool Bool { get; }

    public char Char { get; }

    public int Int { get; }

    public uint UInt { get; }

    public double Float64 { get; }

    public float Float32 { get; }

    public decimal Decimal { get; }

    public string? Text { get; }

    public object? Reference { get; }

    public static UsValue Unit { get; } = new(UsValueKind.Unit);

    public static UsValue Null { get; } = new(UsValueKind.Null);

    public static UsValue FromBool(bool value) => new(UsValueKind.Bool, boolean: value);

    public static UsValue FromChar(char value) => new(UsValueKind.Char, character: value);

    public static UsValue FromInt(int value) => new(UsValueKind.Int, signed: value);

    public static UsValue FromUInt(uint value) => new(UsValueKind.UInt, unsigned: value);

    public static UsValue FromFloat64(double value) => new(UsValueKind.Float64, float64: value);

    public static UsValue FromFloat32(float value) => new(UsValueKind.Float32, float32: value);

    public static UsValue FromDecimal(decimal value) => new(UsValueKind.Decimal, high: value);

    public static UsValue FromText(string value) => new(UsValueKind.Text, text: value);

    public static UsValue FromReference(object value) => new(UsValueKind.Object, reference: value);

    public bool IsNull => Kind == UsValueKind.Null;

    public void EnsureCanStore(bool nullable, int start, int length)
    {
        if (IsNull && !nullable)
        {
            throw new UsRuntimeException(UsRuntimeCodes.NullStore, "~ nullable olmayan hücreye yazılamaz.", start, length);
        }
    }

    public string ToDisplay() => Kind switch
    {
        UsValueKind.Bool => Bool ? "doğru" : "yanlış",
        UsValueKind.Char => Char.ToString(),
        UsValueKind.Int => Int.ToString(CultureInfo.InvariantCulture),
        UsValueKind.UInt => UInt.ToString(CultureInfo.InvariantCulture),
        UsValueKind.Float64 => Float64.ToString("G17", CultureInfo.InvariantCulture),
        UsValueKind.Float32 => Float32.ToString("G9", CultureInfo.InvariantCulture),
        UsValueKind.Decimal => Decimal.ToString(CultureInfo.InvariantCulture),
        UsValueKind.Text => Text ?? string.Empty,
        UsValueKind.Null => string.Empty,
        UsValueKind.Object => Reference switch
        {
            UsDate date => date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            UsTime time => time.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            UsDateTime instant => instant.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            UsDuration duration => duration.Milliseconds.ToString(CultureInfo.InvariantCulture) + " ms",
            UsEmitObject => string.Empty,
            UsEmitList list => string.Join(", ", list.Items.Select(item => item.ToDisplay())),
            _ => string.Empty,
        },
        _ => string.Empty,
    };
}

public static class UsRuntimeCodes
{
    public const string NullStore = "US1500";
    public const string UnassignedRead = "US1501";
    public const string DivisionByZero = "US1502";
    public const string RuntimeCondition = "US1503";
    public const string ZeroStep = "US1504";
    public const string NotExecutable = "US1505";
    public const string BadConstructor = "US1506";
    public const string RuntimeVisibility = "US1507";
    public const string GenericStore = "US1508";
    public const string SelectMiss = "US1509";
    public const string AbstractRuntime = "US1510";
    public const string OutputRuntime = "US1511";
    public const string MissingField = "US1512";
    public const string NullReceiver = "US1513";
    public const string FixedCollection = "US1514";
    public const string EmptyFirst = "US1515";
    public const string EmptyExtreme = "US1516";
    public const string IndexRange = "US1517";
    public const string DuplicateKey = "US1518";
    public const string CollectionModified = "US1519";
    public const string MissingSubscription = "US1520";
    public const string Aggregate = "US1521";
    public const string AwaitTarget = "US1522";
    public const string DurationOverflow = "US1523";
    public const string AsyncHandler = "US1524";
    public const string UserThrown = "US1525";
    public const string InputEnded = "US1526";
    public const string UnknownJsonField = "US1527";
    public const string MissingJsonField = "US1528";
    public const string JsonCycle = "US1529";
    public const string PathEscape = "US1530";
    public const string SymbolicLink = "US1531";
    public const string MissingConfig = "US1532";
    public const string MissingSection = "US1533";
    public const string UnknownTransportPath = "US1534";
    public const string JsonMalformed = "US1535";
    public const string JsonType = "US1536";
    public const string FileNotFound = "US1537";
    public const string FileAccess = "US1538";
    public const string InvalidPath = "US1539";
    public const string IoError = "US1540";
    public const string JsonNull = "US1541";
    public const string NegativeWindow = "US0849";
    public const string QueryTarget = "US0852";
    public const string QuerySelector = "US0853";
    public const string AmbiguousService = "US0859";
    public const string ServiceMissing = "US1544";
    public const string ScopeClosed = "US1545";
    public const string ServiceCycle = "US1546";
    public const string UnknownTable = "US1547";
    public const string MissingRecord = "US1548";
    public const string ClosedTransaction = "US1549";
    public const string DestructiveSchema = "US1550";
    public const string WriteRejected = "US1551";
    public const string BadRouteParameter = "US1552";
    public const string UnsupportedMediaType = "US1553";
    public const string PackagePathEscape = "US1554";
    public const string UnknownHttpVerb = "US0866";
    public const string EmptyHeaderName = "US0867";
    public const string InterpreterInvariant = "US1599";
}

public class UsRuntimeException : Exception
{
    public UsRuntimeException(string code, string message, int start = 0, int length = 0)
        : base(message)
    {
        Code = code;
        Start = start;
        Length = length;
    }

    public string Code { get; }

    public int Start { get; }

    public int Length { get; }

    public List<string> Frames { get; } = [];
}

public sealed class UsThrown : Exception
{
    public UsThrown(UsValue error, string message, int start, int length)
        : base(message)
    {
        Error = error;
        Start = start;
        Length = length;
    }

    public UsValue Error { get; }

    public int Start { get; }

    public int Length { get; }

    public List<string> Frames { get; } = [];
}

public sealed class UsAggregateException : UsRuntimeException
{
    public UsAggregateException(IReadOnlyList<Exception> errors, int start, int length)
        : base(UsRuntimeCodes.Aggregate, string.Join(" | ", errors.Select(item => item.Message)), start, length)
    {
        Errors = errors;
    }

    public IReadOnlyList<Exception> Errors { get; }
}

public interface IUsScheduler
{
    ValueTask DelayAsync(long milliseconds);
}

public sealed class TaskDelayScheduler : IUsScheduler
{
    public static readonly TaskDelayScheduler Instance = new();

    public ValueTask DelayAsync(long milliseconds)
    {
        if (milliseconds < 0 || milliseconds > int.MaxValue)
        {
            throw new UsRuntimeException(UsRuntimeCodes.DurationOverflow, "Süre bekleyemeyecek kadar büyük.", 0, 0);
        }

        return new ValueTask(Task.Delay((int)milliseconds));
    }
}

public interface IOutput
{
    void Write(string text);

    void WriteLine(string text);

    void Clear();
}

public sealed class ListOutput : IOutput
{
    private readonly Lock _write = new();
    private readonly System.Text.StringBuilder _pending = new();

    public List<string> Lines { get; } = [];

    public string Pending
    {
        get
        {
            lock (_write)
            {
                return _pending.ToString();
            }
        }
    }

    public void Write(string text)
    {
        lock (_write)
        {
            _pending.Append(text);
        }
    }

    public void WriteLine(string text)
    {
        lock (_write)
        {
            _pending.Append(text);
            Lines.Add(_pending.ToString());
            _pending.Clear();
        }
    }

    public void Clear()
    {
        lock (_write)
        {
            _pending.Clear();
            Lines.Clear();
        }
    }
}
