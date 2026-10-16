using System.Data;
using Microsoft.Data.SqlClient;
using Us.Runtime;

namespace Us.Data.SqlServer;

/// <summary>US runtime type name → SQL Server type / parameter / reader mapping.</summary>
public static class SqlTypeMap
{
    public static string SqlType(string usType, bool nullable)
    {
        var bare = StripNullable(usType);
        var sql = bare switch
        {
            "metin" => "nvarchar(max)",
            "karakter" => "nchar(1)",
            "tamsayı" => "int",
            "utamsayı" => "bigint",
            "ondalık" => "float(53)",
            "gondalık" => "real",
            "hondalık" => "decimal(28,10)",
            "mantıksal" => "bit",
            "tarih" => "date",
            "saat" => "time",
            "tarihsaat" => "datetime2",
            _ when bare.StartsWith("model:", StringComparison.Ordinal) => "int",
            _ => throw new UsRuntimeException(UsRuntimeCodes.NotExecutable, $"SQL tip eşlemesi yok: '{usType}'.", 0, 0),
        };

        return nullable ? sql + " NULL" : sql + " NOT NULL";
    }

    public static SqlDbType DbType(string usType) => StripNullable(usType) switch
    {
        "metin" => SqlDbType.NVarChar,
        "karakter" => SqlDbType.NChar,
        "tamsayı" => SqlDbType.Int,
        "utamsayı" => SqlDbType.BigInt,
        "ondalık" => SqlDbType.Float,
        "gondalık" => SqlDbType.Real,
        "hondalık" => SqlDbType.Decimal,
        "mantıksal" => SqlDbType.Bit,
        "tarih" => SqlDbType.Date,
        "saat" => SqlDbType.Time,
        "tarihsaat" => SqlDbType.DateTime2,
        _ when StripNullable(usType).StartsWith("model:", StringComparison.Ordinal) => SqlDbType.Int,
        _ => SqlDbType.NVarChar,
    };

    public static object ToParameterValue(UsValue value, string usType)
    {
        if (value.IsNull)
        {
            return DBNull.Value;
        }

        return StripNullable(usType) switch
        {
            "metin" => value.Text ?? string.Empty,
            "karakter" => value.Char.ToString(),
            "tamsayı" => value.Int,
            "utamsayı" => (long)value.UInt,
            "ondalık" => value.Float64,
            "gondalık" => value.Float32,
            "hondalık" => value.Decimal,
            "mantıksal" => value.Bool,
            "tarih" or "saat" or "tarihsaat" or "süre" =>
                throw new UsRuntimeException(UsRuntimeCodes.NotExecutable, $"'{usType}' SQL yazımı bu sürümde desteklenmiyor.", 0, 0),
            _ when StripNullable(usType).StartsWith("model:", StringComparison.Ordinal) => value.Int,
            _ => value.ToDisplay(),
        };
    }

    public static UsValue FromReader(SqlDataReader reader, int ordinal, string usType, bool nullable)
    {
        if (reader.IsDBNull(ordinal))
        {
            if (!nullable)
            {
                throw new UsRuntimeException(UsRuntimeCodes.NullStore, "Veritabanı NULL, zorunlu alana yazılamaz.", 0, 0);
            }

            return UsValue.Null;
        }

        return StripNullable(usType) switch
        {
            "metin" => UsValue.FromText(reader.GetString(ordinal)),
            "karakter" => UsValue.FromChar(reader.GetString(ordinal)[0]),
            "tamsayı" => UsValue.FromInt(reader.GetInt32(ordinal)),
            "utamsayı" => UsValue.FromUInt(checked((uint)reader.GetInt64(ordinal))),
            "ondalık" => UsValue.FromFloat64(reader.GetDouble(ordinal)),
            "gondalık" => UsValue.FromFloat32(reader.GetFloat(ordinal)),
            "hondalık" => UsValue.FromDecimal(reader.GetDecimal(ordinal)),
            "mantıksal" => UsValue.FromBool(reader.GetBoolean(ordinal)),
            _ when StripNullable(usType).StartsWith("model:", StringComparison.Ordinal) => UsValue.FromInt(reader.GetInt32(ordinal)),
            _ => throw new UsRuntimeException(UsRuntimeCodes.NotExecutable, $"SQL okuma eşlemesi yok: '{usType}'.", 0, 0),
        };
    }

    public static bool IsRelationType(string usType) =>
        StripNullable(usType).StartsWith("model:", StringComparison.Ordinal);

    public static string StripNullable(string usType)
    {
        ArgumentException.ThrowIfNullOrEmpty(usType);
        return usType.EndsWith('?') ? usType[..^1] : usType;
    }
}
