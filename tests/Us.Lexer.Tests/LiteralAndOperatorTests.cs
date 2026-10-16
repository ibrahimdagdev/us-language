using Us.Core;
using Us.Lexer;

namespace Us.Lexer.Tests;

public class IntegerLiteralTests
{
    [Theory]
    [InlineData("38")]
    [InlineData("0")]
    [InlineData("00")]
    public void DecimalIntegerIsOneToken(string text)
    {
        var lexed = Lexed.Of(text);
        lexed.Kinds(TokenKind.IntegerLiteral);
        Assert.Equal(text, lexed.TextOf(lexed.Significant().Single()));
    }

    [Fact]
    public void LetterEndsTheNumber()
    {
        var lexed = Lexed.Of("38a");
        lexed.Kinds(TokenKind.IntegerLiteral, TokenKind.Identifier);
    }

    [Fact]
    public void MinusIsNotPartOfTheLiteral()
    {
        var lexed = Lexed.Of("-123");
        lexed.Kinds(TokenKind.Minus, TokenKind.IntegerLiteral);
        Assert.Equal("123", lexed.TextOf(lexed.Significant()[1]));
    }
}

public class RealLiteralTests
{
    [Theory]
    [InlineData("1.5")]
    [InlineData("199.99")]
    public void DottedNumberIsOneReal(string text)
    {
        var lexed = Lexed.Of(text);
        lexed.Kinds(TokenKind.RealLiteral);
    }

    [Fact]
    public void TrailingDotStaysSeparateForDurationSyntax()
    {
        var lexed = Lexed.Of("5.saniye");
        lexed.Kinds(TokenKind.IntegerLiteral, TokenKind.Dot, TokenKind.Identifier);
        Assert.Equal("saniye", lexed.TextOf(lexed.Significant()[2]));
        Assert.Equal("1.", Lexed.Of("1.").TextOf(Lexed.Of("1.").Significant()[0]) is "1" ? "1." : "");
        var split = Lexed.Of("1.");
        split.Kinds(TokenKind.IntegerLiteral, TokenKind.Dot);
    }
}

public class StringLiteralTests
{
    [Fact]
    public void EscapedStringIsOneLiteral()
    {
        var lexed = Lexed.Of("\"a\\nb\\\"\\\\\"");
        lexed.Kinds(TokenKind.StringLiteral);
        Assert.Equal("a\nb\"\\", LiteralText.Decode(lexed.File.Text, lexed.Significant().Single().Span, trimQuotes: true));
    }

    [Fact]
    public void UnterminatedStringProducesDiagnosticAndContinues()
    {
        var lexed = Lexed.Of("\"açık\nsyaz");
        Assert.Equal(TokenKind.StringLiteral, lexed.Significant()[0].Kind);
        Assert.Equal(TokenKind.Identifier, lexed.Significant()[1].Kind);
        Assert.Equal("syaz", lexed.TextOf(lexed.Significant()[1]));
        Assert.Contains(lexed.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.UnterminatedString);
    }

    [Fact]
    public void UnknownEscapeIsDiagnostic()
    {
        var lexed = Lexed.Of("\"\\q\"");
        lexed.Kinds(TokenKind.StringLiteral);
        var diagnostic = Assert.Single(lexed.Diagnostics);
        Assert.Equal(DiagnosticCodes.InvalidEscape, diagnostic.Code);
        Assert.Equal(2, diagnostic.Span.Length);
    }
}

public class InterpolationLexerTests
{
    [Fact]
    public void HoleIsSeparateFromText()
    {
        var lexed = Lexed.Of("\"Merhaba {ad}\"");
        lexed.Kinds(
            TokenKind.InterpolationStart,
            TokenKind.StringText,
            TokenKind.Identifier,
            TokenKind.InterpolationEnd);
        Assert.Equal("\"", lexed.TextOf(lexed.Significant()[0]));
        Assert.Equal("Merhaba ", lexed.TextOf(lexed.Significant()[1]));
        Assert.Equal("ad", lexed.TextOf(lexed.Significant()[2]));
        Assert.Equal("}\"", lexed.TextOf(lexed.Significant()[3]));
    }

    [Fact]
    public void DoubledBracesDoNotStartAHole()
    {
        var lexed = Lexed.Of("\"a{{b}}\"");
        lexed.Kinds(TokenKind.StringLiteral);
        Assert.Equal("a{b}", LiteralText.Decode(lexed.File.Text, lexed.Significant().Single().Span, trimQuotes: true));
    }

    [Fact]
    public void MiddleDelimiterSeparatesHoles()
    {
        var lexed = Lexed.Of("\"{a}b{c}\"");
        lexed.Kinds(
            TokenKind.InterpolationStart,
            TokenKind.Identifier,
            TokenKind.InterpolationMiddle,
            TokenKind.Identifier,
            TokenKind.InterpolationEnd);
        Assert.Equal("}b{", lexed.TextOf(lexed.Significant()[2]));
    }

    [Fact]
    public void UnclosedHoleIsDiagnostic()
    {
        var lexed = Lexed.Of("\"{ad\"");
        Assert.Contains(lexed.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.UnterminatedInterpolation);
    }
}

public class SingleCharTokenTests
{
    [Theory]
    [InlineData("(", TokenKind.OpenParen)]
    [InlineData(")", TokenKind.CloseParen)]
    [InlineData("{", TokenKind.OpenBrace)]
    [InlineData("}", TokenKind.CloseBrace)]
    [InlineData("[", TokenKind.OpenBracket)]
    [InlineData("]", TokenKind.CloseBracket)]
    [InlineData(",", TokenKind.Comma)]
    [InlineData(":", TokenKind.Colon)]
    [InlineData(";", TokenKind.Semicolon)]
    [InlineData(".", TokenKind.Dot)]
    [InlineData("+", TokenKind.Plus)]
    [InlineData("-", TokenKind.Minus)]
    [InlineData("*", TokenKind.Star)]
    [InlineData("/", TokenKind.Slash)]
    [InlineData("%", TokenKind.Percent)]
    [InlineData("!", TokenKind.Bang)]
    [InlineData("=", TokenKind.Equals)]
    [InlineData("<", TokenKind.Less)]
    [InlineData(">", TokenKind.Greater)]
    [InlineData("&", TokenKind.Ampersand)]
    [InlineData("|", TokenKind.Pipe)]
    [InlineData("?", TokenKind.Question)]
    public void SingleCharacterHasItsKind(string text, TokenKind kind)
    {
        Lexed.Of(text).Kinds(kind);
    }
}

public class LongestMatchTests
{
    [Theory]
    [InlineData("==", TokenKind.EqualEqual)]
    [InlineData("!=", TokenKind.BangEqual)]
    [InlineData("!=>", TokenKind.BangEqualsGreater)]
    [InlineData("=>", TokenKind.EqualsGreater)]
    [InlineData("<=", TokenKind.LessEqual)]
    [InlineData(">=", TokenKind.GreaterEqual)]
    [InlineData("?.", TokenKind.QuestionDot)]
    [InlineData("??", TokenKind.QuestionQuestion)]
    [InlineData("->", TokenKind.Arrow)]
    [InlineData("++", TokenKind.PlusPlus)]
    [InlineData("--", TokenKind.MinusMinus)]
    [InlineData("+=", TokenKind.PlusEquals)]
    [InlineData("-=", TokenKind.MinusEquals)]
    [InlineData("*=", TokenKind.StarEquals)]
    [InlineData("/=", TokenKind.SlashEquals)]
    [InlineData("&&", TokenKind.AmpersandAmpersand)]
    [InlineData("||", TokenKind.PipePipe)]
    public void LongOperatorIsOneToken(string text, TokenKind kind)
    {
        var lexed = Lexed.Of(text);
        lexed.Kinds(kind);
        Assert.Equal(text, lexed.TextOf(lexed.Significant().Single()));
    }

    [Fact]
    public void BangEqualsGreaterIsNotThreeTokens()
    {
        Assert.Single(Lexed.Of("!=>").Significant());
    }

    [Fact]
    public void QuestionFormsStayDistinct()
    {
        Lexed.Of("?").Kinds(TokenKind.Question);
        Lexed.Of("?.").Kinds(TokenKind.QuestionDot);
        Lexed.Of("??").Kinds(TokenKind.QuestionQuestion);
        Lexed.Of("??.").Kinds(TokenKind.QuestionQuestion, TokenKind.Dot);
    }
}

public class SpecialTokenTests
{
    [Fact]
    public void AtTildeAndCaretAreDistinct()
    {
        Lexed.Of("@").Kinds(TokenKind.At);
        Lexed.Of("~").Kinds(TokenKind.Tilde);
        Lexed.Of("^").Kinds(TokenKind.Caret);
    }

    [Fact]
    public void AtDoesNotAbsorbTheTypeName()
    {
        var lexed = Lexed.Of("@Kullanıcı");
        lexed.Kinds(TokenKind.At, TokenKind.Identifier);
        Assert.Equal("Kullanıcı", lexed.TextOf(lexed.Significant()[1]));
    }

    [Fact]
    public void CaretDotIdentifierIsThreeTokens()
    {
        var lexed = Lexed.Of("^.id");
        lexed.Kinds(TokenKind.Caret, TokenKind.Dot, TokenKind.Identifier);
        Assert.Equal("id", lexed.TextOf(lexed.Significant()[2]));
    }
}

public class BasindaTokenTests
{
    [Fact]
    public void UnderscoreBasindaIsOneToken()
    {
        var lexed = Lexed.Of("_başında");
        lexed.Kinds(TokenKind.BasindaUnderscore);
    }

    [Theory]
    [InlineData("_başlangıç")]
    [InlineData("_başında2")]
    [InlineData("_birşey")]
    public void LongerUnderscoreNameStaysIdentifier(string text)
    {
        var lexed = Lexed.Of(text);
        lexed.Kinds(TokenKind.Identifier);
        Assert.Equal(text, lexed.TextOf(lexed.Significant().Single()));
    }

    [Fact]
    public void BasindaWithoutUnderscoreIsKeyword()
    {
        Lexed.Of("başında").Kinds(TokenKind.Basinda);
    }
}

public class LexerRecoveryTests
{
    [Fact]
    public void ScanContinuesAfterBadPrefix()
    {
        var lexed = Lexed.Of("\"açık\n# syaz /*");
        Assert.Contains(lexed.Significant(), token => token.Kind == TokenKind.Identifier && lexed.TextOf(token) == "syaz");
        Assert.Equal(3, lexed.Diagnostics.Length);
        Assert.Contains(lexed.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.UnterminatedString);
        Assert.Contains(lexed.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.UnterminatedBlockComment);
        Assert.Contains(lexed.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.UnrecognizedCharacter);
    }
}

public class CharacterLiteralTests
{
    [Fact]
    public void TurkishCharacterIsOneLiteral()
    {
        var lexed = Lexed.Of("'ç'");
        lexed.Kinds(TokenKind.CharacterLiteral);
        Assert.Empty(lexed.Diagnostics);
    }

    [Fact]
    public void TwoCharactersAreDiagnostic()
    {
        var lexed = Lexed.Of("'ab'");
        lexed.Kinds(TokenKind.CharacterLiteral);
        Assert.Equal(DiagnosticCodes.InvalidCharacterLiteral, Assert.Single(lexed.Diagnostics).Code);
    }

    [Fact]
    public void UnterminatedCharacterContinues()
    {
        var lexed = Lexed.Of("'a\nsyaz");
        Assert.Equal(TokenKind.Identifier, lexed.Significant()[1].Kind);
        Assert.Contains(lexed.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.InvalidCharacterLiteral);
    }
}

public class TokenLocationTests
{
    [Fact]
    public void SecondLineTokenReportsLineTwo()
    {
        var lexed = Lexed.Of("a\nİstanbul");
        var token = lexed.Significant()[1];
        var (line, column) = lexed.File.Lines.GetLineAndColumn(token.Span.Start);
        Assert.Equal(2, line);
        Assert.Equal(1, column);
        Assert.Equal("İstanbul".Length, token.Span.Length);
    }

    [Fact]
    public void TriviaHasItsOwnSpan()
    {
        var trivia = Lexed.Of("\n\n").Tokens[^1].LeadingTrivia;
        Assert.Equal(2, trivia.Length);
        Assert.All(trivia, item => Assert.Equal(1, item.Span.Length));
    }
}
