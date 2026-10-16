using System.Globalization;
using System.Text.Json;

namespace Us.Runtime;

/// <summary>
/// Stdlib helpers for DotNetEmitBackend. Semantics match Evaluator.Library (tr-TR text, temporal, files).
/// </summary>
public static class UsEmitStdlib
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static UsValue Today() =>
        UsValue.FromReference(new UsDate(DateOnly.FromDateTime(UsEmitHost.Clock.WallNow)));

    public static UsValue Now() =>
        UsValue.FromReference(new UsDateTime(UsEmitHost.Clock.WallNow));

    public static UsValue Trim(UsValue receiver) =>
        UsValue.FromText((receiver.Text ?? string.Empty).Trim());

    public static UsValue Upper(UsValue receiver) =>
        UsValue.FromText((receiver.Text ?? string.Empty).ToUpper(Turkish));

    public static UsValue Lower(UsValue receiver) =>
        UsValue.FromText((receiver.Text ?? string.Empty).ToLower(Turkish));

    public static UsValue Contains(UsValue receiver, UsValue part) =>
        UsValue.FromBool((receiver.Text ?? string.Empty).Contains(part.Text ?? string.Empty, StringComparison.Ordinal));

    public static UsValue Starts(UsValue receiver, UsValue part) =>
        UsValue.FromBool((receiver.Text ?? string.Empty).StartsWith(part.Text ?? string.Empty, StringComparison.Ordinal));

    public static UsValue Ends(UsValue receiver, UsValue part) =>
        UsValue.FromBool((receiver.Text ?? string.Empty).EndsWith(part.Text ?? string.Empty, StringComparison.Ordinal));

    public static UsValue Replace(UsValue receiver, UsValue oldValue, UsValue newValue) =>
        UsValue.FromText((receiver.Text ?? string.Empty).Replace(oldValue.Text ?? string.Empty, newValue.Text ?? string.Empty, StringComparison.Ordinal));

    public static UsValue Split(UsValue receiver, UsValue separator, int start, int length)
    {
        var sep = separator.Text ?? string.Empty;
        if (sep.Length == 0)
        {
            throw new UsRuntimeException(UsRuntimeCodes.JsonType, "Ayraç boş olamaz.", start, length);
        }

        var list = new UsEmitList("liste");
        foreach (var part in (receiver.Text ?? string.Empty).Split(sep))
        {
            list.Items.Add(UsValue.FromText(part));
        }

        return UsValue.FromReference(list);
    }

    public static UsValue Join(UsValue receiver, UsValue values, int start, int length)
    {
        var list = values.Reference as UsEmitList
            ?? throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "birleştir liste ister.", start, length);
        return UsValue.FromText(string.Join(receiver.Text ?? string.Empty, list.Items.Select(item => item.ToDisplay())));
    }

    public static UsValue FileRead(UsValue path, int start, int length) =>
        UsValue.FromText(File(start, length, () => UsEmitHost.Files.Read(path.Text ?? string.Empty)));

    public static void FileWrite(UsValue path, UsValue content, int start, int length) =>
        File(start, length, () => UsEmitHost.Files.Write(path.Text ?? string.Empty, content.Text ?? string.Empty));

    public static void FileAppend(UsValue path, UsValue content, int start, int length) =>
        File(start, length, () => UsEmitHost.Files.Append(path.Text ?? string.Empty, content.Text ?? string.Empty));

    public static void FileDelete(UsValue path, int start, int length) =>
        File(start, length, () => UsEmitHost.Files.Delete(path.Text ?? string.Empty));

    public static UsValue FileExists(UsValue path, int start, int length) =>
        UsValue.FromBool(File(start, length, () => UsEmitHost.Files.Exists(path.Text ?? string.Empty)));

    public static void DirectoryCreate(UsValue path, int start, int length) =>
        File(start, length, () => UsEmitHost.Files.CreateDirectory(path.Text ?? string.Empty));

    public static void DirectoryDelete(UsValue path, int start, int length) =>
        File(start, length, () => UsEmitHost.Files.DeleteDirectory(path.Text ?? string.Empty));

    public static UsValue DirectoryExists(UsValue path, int start, int length) =>
        UsValue.FromBool(File(start, length, () => UsEmitHost.Files.DirectoryExists(path.Text ?? string.Empty)));

    public static UsValue DirectoryFiles(UsValue path, int start, int length)
    {
        var names = File(start, length, () => UsEmitHost.Files.ListFiles(path.Text ?? string.Empty));
        var list = new UsEmitList("liste");
        foreach (var name in names)
        {
            list.Items.Add(UsValue.FromText(name));
        }

        return UsValue.FromReference(list);
    }

    public static UsValue PathCombine(UsValue left, UsValue right, int start, int length) =>
        UsValue.FromText(File(start, length, () => UsEmitHost.Files.Combine(left.Text ?? string.Empty, right.Text ?? string.Empty)));

    public static void Log(int level, UsValue message)
    {
        var mapped = level switch
        {
            0 => UsLogLevel.Trace,
            1 => UsLogLevel.Information,
            2 => UsLogLevel.Warning,
            3 => UsLogLevel.Error,
            _ => UsLogLevel.Critical,
        };
        UsEmitHost.Log.Write(mapped, message.ToDisplay());
    }

    public static void LogNamed(int level, string name, UsValue message)
    {
        Log(level, UsValue.FromText(SecretRedactor.RedactNamed(name, message.ToDisplay())));
    }

    public static UsValue Add(UsValue left, UsValue right, int start, int length)
    {
        if (TryTemporal(left, right, '+', start, length, out var result))
        {
            return result;
        }

        throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Zamansal toplama uymuyor.", start, length);
    }

    public static UsValue Subtract(UsValue left, UsValue right, int start, int length)
    {
        if (TryTemporal(left, right, '-', start, length, out var result))
        {
            return result;
        }

        throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Zamansal çıkarma uymuyor.", start, length);
    }

    public static bool Compare(UsValue left, UsValue right, string op, int start, int length)
    {
        if (!Order(left, right, out var order))
        {
            throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Zamansal karşılaştırma uymuyor.", start, length);
        }

        return op switch
        {
            "==" => order == 0,
            "!=" => order != 0,
            "<" => order < 0,
            "<=" => order <= 0,
            ">" => order > 0,
            ">=" => order >= 0,
            _ => throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Karşılaştırma işleci yok.", start, length),
        };
    }

    public static void BindSetting(
        string section,
        string typeName,
        string[] fieldNames,
        string[] typeNames,
        bool[] nullables,
        bool[] required,
        string[] defaultJsonOrEmpty,
        int start,
        int length)
    {
        if (string.IsNullOrEmpty(section))
        {
            throw new UsRuntimeException(UsRuntimeCodes.MissingSection, "Ayar bölümü yok.", start, length);
        }

        var text = UsEmitHost.Configuration.ReadText();
        if (text is null)
        {
            throw new UsRuntimeException(UsRuntimeCodes.MissingConfig, "uygulamaAyar.json bulunamadı.", start, length);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            throw new UsRuntimeException(UsRuntimeCodes.JsonMalformed, "JSON okunamadı.", start, length);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(section, out var sectionElement))
            {
                throw new UsRuntimeException(UsRuntimeCodes.MissingSection, $"'{section}' bölümü yok.", start, length);
            }

            var value = UsEmitJson.ReadElement(
                sectionElement,
                typeName,
                fieldNames,
                typeNames,
                nullables,
                required,
                defaultJsonOrEmpty,
                start,
                length);
            if (value.Reference is not null)
            {
                UsEmitHost.Services.Add(value.Reference);
            }
        }
    }

    private static bool TryTemporal(UsValue left, UsValue right, char op, int start, int length, out UsValue result)
    {
        result = UsValue.Unit;
        if (left.Reference is UsDateTime instant && right.Reference is UsDuration duration && op is '+' or '-')
        {
            try
            {
                var ticks = Ticks(duration.Milliseconds, start, length);
                if (op == '-')
                {
                    ticks = checked(-ticks);
                }

                result = UsValue.FromReference(new UsDateTime(instant.Value.AddTicks(ticks)));
            }
            catch (Exception exception) when (exception is ArgumentOutOfRangeException or OverflowException)
            {
                throw new UsRuntimeException(UsRuntimeCodes.DurationOverflow, "Süre taştı.", start, length);
            }

            return true;
        }

        if (left.Reference is UsDateTime startInstant && right.Reference is UsDateTime endInstant && op == '-')
        {
            result = UsValue.FromReference(new UsDuration((startInstant.Value.Ticks - endInstant.Value.Ticks) / TimeSpan.TicksPerMillisecond));
            return true;
        }

        if (left.Reference is UsDuration first && right.Reference is UsDuration second && op is '+' or '-')
        {
            try
            {
                var milliseconds = op == '+'
                    ? checked(first.Milliseconds + second.Milliseconds)
                    : checked(first.Milliseconds - second.Milliseconds);
                result = UsValue.FromReference(new UsDuration(milliseconds));
            }
            catch (OverflowException)
            {
                throw new UsRuntimeException(UsRuntimeCodes.DurationOverflow, "Süre taştı.", start, length);
            }

            return true;
        }

        return false;
    }

    private static long Ticks(long milliseconds, int start, int length)
    {
        try
        {
            return checked(milliseconds * TimeSpan.TicksPerMillisecond);
        }
        catch (OverflowException)
        {
            throw new UsRuntimeException(UsRuntimeCodes.DurationOverflow, "Süre taştı.", start, length);
        }
    }

    private static bool Order(UsValue left, UsValue right, out int order)
    {
        order = (left.Reference, right.Reference) switch
        {
            (UsDate leftDate, UsDate rightDate) => leftDate.Value.CompareTo(rightDate.Value),
            (UsTime leftTime, UsTime rightTime) => leftTime.Value.CompareTo(rightTime.Value),
            (UsDateTime leftInstant, UsDateTime rightInstant) => leftInstant.Value.CompareTo(rightInstant.Value),
            (UsDuration leftDuration, UsDuration rightDuration) => leftDuration.Milliseconds.CompareTo(rightDuration.Milliseconds),
            _ => int.MinValue,
        };
        return order != int.MinValue
            || left.Reference is UsDate or UsTime or UsDateTime or UsDuration
                && right.Reference?.GetType() == left.Reference?.GetType();
    }

    private static T File<T>(int start, int length, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (UsRuntimeException exception) when (exception.Start == 0 && exception.Length == 0)
        {
            throw new UsRuntimeException(exception.Code, exception.Message, start, length);
        }
    }

    private static void File(int start, int length, Action action) => File(start, length, () =>
    {
        action();
        return 0;
    });
}
