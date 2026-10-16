using Us.Runtime;
using Us.Semantics;

namespace Us.Compiler;

public static class ClrTypeMap
{
    public static bool TryMapPrimitive(TypeSymbol type, out Type clrType, out UsValueKind valueKind)
    {
        clrType = typeof(UsValue);
        valueKind = UsValueKind.Unit;
        if (type is SourceTypeSymbol or ConstructedTypeSymbol or DatabaseTypeSymbol or TableQueryTypeSymbol)
        {
            valueKind = UsValueKind.Object;
            return true;
        }

        var name = type.WithNullable(false).Name;
        switch (name)
        {
            case "metin":
                valueKind = UsValueKind.Text;
                return true;
            case "karakter":
                valueKind = UsValueKind.Char;
                return true;
            case "tamsayı":
                valueKind = UsValueKind.Int;
                return true;
            case "utamsayı":
                valueKind = UsValueKind.UInt;
                return true;
            case "ondalık":
                valueKind = UsValueKind.Float64;
                return true;
            case "gondalık":
                valueKind = UsValueKind.Float32;
                return true;
            case "hondalık":
                valueKind = UsValueKind.Decimal;
                return true;
            case "mantıksal":
                valueKind = UsValueKind.Bool;
                return true;
            case "tarih":
            case "saat":
            case "tarihsaat":
            case "süre":
            case "liste":
            case "dizi":
            case "küme":
            case "sözlük":
                valueKind = UsValueKind.Object;
                return true;
            default:
                return false;
        }
    }

    public static Type StorageType => typeof(UsValue);
}
