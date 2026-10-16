namespace Us.Runtime;

public sealed class UsDuration
{
    public UsDuration(long milliseconds) => Milliseconds = milliseconds;

    public long Milliseconds { get; }
}

public sealed class UsDate
{
    public UsDate(DateOnly value) => Value = value;

    public DateOnly Value { get; }
}

public sealed class UsTime
{
    public UsTime(TimeOnly value) => Value = value;

    public TimeOnly Value { get; }
}

public sealed class UsDateTime
{
    public UsDateTime(DateTime value) => Value = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    public DateTime Value { get; }
}
