using Us.Core;
using Us.Parser;

namespace Us.Parser.Tests;

public class ParserRecoveryTests
{
    [Fact]
    public void BrokenDeclarationDoesNotDropTheNextOne()
    {
        var result = Parsed.File("ad: metin =\n\nyaş: tamsayı = 38");
        Assert.Equal(2, result.Root.Members.Length);
        Assert.IsType<VariableDeclarationSyntax>(result.Root.Members[0]);
        var second = Assert.IsType<VariableDeclarationSyntax>(result.Root.Members[1]);
        Assert.IsType<LiteralExpressionSyntax>(second.Initializers[0]);
        Assert.Contains(result.Diagnostics.ToImmutableArray(), item => item.Code == DiagnosticCodes.UnexpectedToken);
    }

    [Fact]
    public void MissingCloseBraceIsDiagnosed()
    {
        var result = Parsed.File("{\nad: metin = \"a\"");
        Assert.Contains(result.Diagnostics.ToImmutableArray(), item => item.Code == DiagnosticCodes.UnclosedBlock);
        Assert.IsType<BlockSyntax>(result.Root.Members[0]);
    }

    [Fact]
    public void ReportsStableParserCodes()
    {
        ExpectCode(DiagnosticCodes.MissingExpressionIfFalse, "durum: metin = eğer doğru => \"a\"");
        ExpectCode(DiagnosticCodes.MissingLoopArrow, "dön kullanıcılar kullanıcı\n{\n}");
        ExpectCode(DiagnosticCodes.MissingDoWhile, "yap\n{\n}");
        ExpectCode(DiagnosticCodes.UntypedLambdaParameter, "değer = (x) => x");
        ExpectCode(DiagnosticCodes.WildcardImport, "al *");
        ExpectCode(DiagnosticCodes.BothPreparations, "başında\n{\n}\n_başında\n{\n}\ndene\n{\n}");
        ExpectCode(DiagnosticCodes.ElseBeforeElseIf, "eğer doğru\n{\n}\ndeğilse\n{\n}\nedeğilse yanlış\n{\n}");
    }

    [Fact]
    public void TypeMismatchIsNotAParserError()
    {
        var result = Parsed.Clean("yaş: tamsayı = \"İbrahim\"");
        Assert.IsType<VariableDeclarationSyntax>(Assert.Single(result.Root.Members));
    }

    private static void ExpectCode(string code, string text)
    {
        var result = Parsed.File(text);
        Assert.Contains(result.Diagnostics.ToImmutableArray(), item => item.Code == code);
    }
}
