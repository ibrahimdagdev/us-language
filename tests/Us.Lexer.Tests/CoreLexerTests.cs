using Us.Core;
using Us.Lexer;

namespace Us.Lexer.Tests;

public class TokenTests
{
    [Fact]
    public void TokenExposesKindSpanAndLeadingTrivia()
    {
        var lexed = Lexed.Of("  ad");
        var token = lexed.Significant().Single();
        Assert.Equal(TokenKind.Identifier, token.Kind);
        Assert.Equal(new TextSpan(2, 2), token.Span);
        Assert.Equal("ad", lexed.TextOf(token));
        var trivia = Assert.Single(token.LeadingTrivia);
        Assert.Equal(TriviaKind.Whitespace, trivia.Kind);
        Assert.Equal(new TextSpan(0, 2), trivia.Span);
    }
}

public class LexerDriverTests
{
    [Fact]
    public void EmptyFileIsOnlyEof()
    {
        var lexed = Lexed.Of("");
        var token = Assert.Single(lexed.Tokens);
        Assert.Equal(TokenKind.EndOfFile, token.Kind);
        Assert.Equal(0, token.Span.Length);
        Assert.Empty(lexed.Diagnostics);
    }

    [Fact]
    public void UnknownCharacterProducesDiagnosticAndToken()
    {
        var lexed = Lexed.Of("#");
        lexed.Kinds(TokenKind.Bad);
        var diagnostic = Assert.Single(lexed.Diagnostics);
        Assert.Equal(DiagnosticCodes.UnrecognizedCharacter, diagnostic.Code);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(1, diagnostic.Column);
        Assert.Equal(new TextSpan(0, 1), diagnostic.Span);
    }

    [Fact]
    public void CursorDoesNotPassTheEnd()
    {
        var lexed = Lexed.Of("a");
        Assert.Equal(2, lexed.Tokens.Length);
        Assert.Equal(TokenKind.EndOfFile, lexed.Tokens[^1].Kind);
        Assert.Equal(1, lexed.Tokens[^1].Span.Start);
    }
}

public class WhitespaceTriviaTests
{
    [Fact]
    public void WhitespaceIsNotPartOfTheTokenText()
    {
        var lexed = Lexed.Of(" \t\vad");
        var token = lexed.Significant().Single();
        Assert.Equal("ad", lexed.TextOf(token));
        Assert.Equal(TriviaKind.Whitespace, Assert.Single(token.LeadingTrivia).Kind);
    }

    [Fact]
    public void CrLfIsOneNewlineTrivia()
    {
        var lexed = Lexed.Of("a\r\nb");
        var second = lexed.Significant()[1];
        var newline = Assert.Single(second.LeadingTrivia);
        Assert.Equal(TriviaKind.NewLine, newline.Kind);
        Assert.Equal(2, newline.Span.Length);
        Assert.Equal("b", lexed.TextOf(second));
    }
}

public class LineCommentTests
{
    [Fact]
    public void LineCommentDoesNotEmitTokens()
    {
        var lexed = Lexed.Of("a // yorum\nb");
        lexed.Kinds(TokenKind.Identifier, TokenKind.Identifier);
        var trivia = lexed.Significant()[1].LeadingTrivia;
        Assert.Contains(trivia, item => item.Kind == TriviaKind.LineComment);
        Assert.Contains(trivia, item => item.Kind == TriviaKind.NewLine);
        Assert.DoesNotContain("yorum", lexed.TextOf(lexed.Significant()[1]), StringComparison.Ordinal);
    }

    [Fact]
    public void SlashAloneIsDivision()
    {
        var lexed = Lexed.Of("a / b");
        lexed.Kinds(TokenKind.Identifier, TokenKind.Slash, TokenKind.Identifier);
    }
}

public class BlockCommentTests
{
    [Fact]
    public void MultilineCommentIsOneTrivia()
    {
        var lexed = Lexed.Of("a /* bir\niki */ b");
        lexed.Kinds(TokenKind.Identifier, TokenKind.Identifier);
        var trivia = Assert.Single(lexed.Significant()[1].LeadingTrivia, item => item.Kind == TriviaKind.BlockComment);
        Assert.Equal(TriviaKind.BlockComment, trivia.Kind);
        Assert.Contains("iki", lexed.File.Text.ToString(trivia.Span), StringComparison.Ordinal);
        Assert.Empty(lexed.Diagnostics);
    }

    [Fact]
    public void UnterminatedBlockCommentIsDiagnostic()
    {
        var lexed = Lexed.Of("/* açık");
        Assert.Equal(TokenKind.EndOfFile, Assert.Single(lexed.Tokens).Kind);
        var diagnostic = Assert.Single(lexed.Diagnostics);
        Assert.Equal(DiagnosticCodes.UnterminatedBlockComment, diagnostic.Code);
        Assert.Equal("*/", diagnostic.Suggestion);
    }
}

public class AsciiIdentifierTests
{
    [Fact]
    public void PreservesCase()
    {
        var lexed = Lexed.Of("Kullanici kullanıcı KULLANICI");
        var tokens = lexed.Significant();
        Assert.All(tokens, token => Assert.Equal(TokenKind.Identifier, token.Kind));
        Assert.Equal("Kullanici", lexed.TextOf(tokens[0]));
        Assert.Equal("kullanıcı", lexed.TextOf(tokens[1]));
        Assert.Equal("KULLANICI", lexed.TextOf(tokens[2]));
    }

    [Fact]
    public void DigitDoesNotStartAnIdentifier()
    {
        var lexed = Lexed.Of("38ad");
        lexed.Kinds(TokenKind.IntegerLiteral, TokenKind.Identifier);
    }

    [Fact]
    public void UnderscoreAloneIsAnIdentifier()
    {
        var lexed = Lexed.Of("_");
        lexed.Kinds(TokenKind.Identifier);
        Assert.Equal("_", lexed.TextOf(lexed.Significant().Single()));
    }
}

public class TurkishIdentifierTests
{
    [Theory]
    [InlineData("öğrenci")]
    [InlineData("çalışıyor")]
    [InlineData("ürünFiyatı")]
    [InlineData("İstanbul")]
    [InlineData("İ")]
    public void TurkishWordIsOneIdentifier(string word)
    {
        var lexed = Lexed.Of(word);
        lexed.Kinds(TokenKind.Identifier);
        Assert.Equal(word, lexed.TextOf(lexed.Significant().Single()));
    }

    [Fact]
    public void DoesNotNormalizeToAscii()
    {
        var dotted = Lexed.Of("çağrı");
        var plain = Lexed.Of("cagri");
        Assert.NotEqual(dotted.TextOf(dotted.Significant().Single()), plain.TextOf(plain.Significant().Single()));
        Assert.NotEqual("İ", Lexed.Of("i").TextOf(Lexed.Of("i").Significant().Single()));
    }

    [Fact]
    public void CombiningMarkContinuesTheIdentifier()
    {
        var word = "a\u0301";
        var lexed = Lexed.Of(word);
        lexed.Kinds(TokenKind.Identifier);
        Assert.Equal(2, lexed.Significant().Single().Span.Length);
    }

    [Fact]
    public void SupplementaryLetterIsOneIdentifierAndEmojiIsNotSplit()
    {
        var letter = char.ConvertFromUtf32(0x10280);
        var lexed = Lexed.Of(letter);
        lexed.Kinds(TokenKind.Identifier);
        Assert.Equal(2, lexed.Significant().Single().Span.Length);

        var emoji = "a" + char.ConvertFromUtf32(0x1F600) + "b";
        var mixed = Lexed.Of(emoji);
        mixed.Kinds(TokenKind.Identifier, TokenKind.Bad, TokenKind.Identifier);
        Assert.Equal(2, mixed.Significant()[1].Span.Length);
        Assert.Empty(mixed.Diagnostics.Where(item => item.Span.Length == 1 && item.Code == DiagnosticCodes.UnrecognizedCharacter && item.Column == 2));
    }
}

public class KeywordTests
{
    public static IEnumerable<object[]> SpecKeywords() =>
        SpecKeywordText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(word => new object[] { word });

    [Theory]
    [MemberData(nameof(SpecKeywords))]
    public void EachSpecKeywordHasItsOwnKind(string word)
    {
        var lexed = Lexed.Of(word);
        var token = lexed.Significant().Single();
        Assert.NotEqual(TokenKind.Identifier, token.Kind);
        Assert.Equal(word, lexed.TextOf(token));
        Assert.Contains(Keywords.All, keyword => keyword.Text == word && keyword.Kind == token.Kind);
    }

    [Fact]
    public void WrongCaseStaysIdentifier()
    {
        var lexed = Lexed.Of("Eğer EĞER Fonksiyon");
        Assert.All(lexed.Significant(), token => Assert.Equal(TokenKind.Identifier, token.Kind));
    }

    [Theory]
    [InlineData("eğerki")]
    [InlineData("fonksiyonel")]
    [InlineData("doğrulama")]
    [InlineData("alanAdı")]
    public void KeywordPrefixStaysIdentifier(string word)
    {
        var lexed = Lexed.Of(word);
        lexed.Kinds(TokenKind.Identifier);
    }

    [Fact]
    public void ContextualNamesAreNotKeywords()
    {
        foreach (var word in new[] { "anahtar", "zorunlu", "uzunluk", "benzersiz", "aralık", "varsayılan", "Ayar", "saniye", "milisaniye" })
        {
            Assert.Equal(TokenKind.Identifier, Lexed.Of(word).Significant().Single().Kind);
        }
    }

    private const string SpecKeywordText = """
        eğer edeğilse değilse
        dön ile adım tekrarla yap sonsuzdön çık devam
        fonksiyon afonksiyon bekle
        model sınıf oluştur bu temel
        genel özel korumalı dahili
        arayüz soyut statik sanal yerinegeç mühürlü
        alan al olarak
        seç seçenek
        ref çıktı
        dene yakala sonunda fırlat
        başında
        olay
        eşişlem
        doğru yanlış
        doğrula kural
        dönüşüm
        bağla
        servis kapsamlı tekil geçici
        kontrolcü rota
        test beklenen
        işlem onayla gerial
        veritabanı tablo birlikte
        """;
}
