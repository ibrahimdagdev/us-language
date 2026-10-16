using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Us.Core;

namespace Us.Lexer;

public sealed class Lexer
{
    private readonly SourceFile _file;
    private readonly string _text;
    private readonly DiagnosticBag _diagnostics = new();
    private readonly List<Token> _tokens = [];
    private ImmutableArray<Trivia>? _pendingTrivia;
    private int _position;

    private Lexer(SourceFile file)
    {
        _file = file;
        _text = file.Text.ToString();
    }

    public static LexerResult Tokenize(SourceFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var lexer = new Lexer(file);
        lexer.Scan();
        return new LexerResult(lexer._tokens.ToImmutableArray(), lexer._diagnostics);
    }

    private void Scan()
    {
        while (_position < _text.Length)
        {
            var before = _position;
            var count = _tokens.Count;
            LexToken();
            if (_position == before && _tokens.Count == count)
            {
                var start = _position;
                var length = AdvanceRune();
                Diagnose(DiagnosticCodes.UnrecognizedCharacter, "Tanınmayan karakter.", start, Math.Max(length, 1));
                Emit(TokenKind.Bad, start);
            }
        }

        Emit(TokenKind.EndOfFile, _position);
    }

    private void LexToken()
    {
        EnsureTrivia();
        if (_position >= _text.Length)
        {
            return;
        }

        var start = _position;
        var ch = _text[_position];
        switch (ch)
        {
            case '"':
                LexString();
                return;
            case '\'':
                LexCharacter();
                return;
            case '(':
                _position++;
                Emit(TokenKind.OpenParen, start);
                return;
            case ')':
                _position++;
                Emit(TokenKind.CloseParen, start);
                return;
            case '{':
                _position++;
                Emit(TokenKind.OpenBrace, start);
                return;
            case '}':
                _position++;
                Emit(TokenKind.CloseBrace, start);
                return;
            case '[':
                _position++;
                Emit(TokenKind.OpenBracket, start);
                return;
            case ']':
                _position++;
                Emit(TokenKind.CloseBracket, start);
                return;
            case ',':
                _position++;
                Emit(TokenKind.Comma, start);
                return;
            case ':':
                _position++;
                Emit(TokenKind.Colon, start);
                return;
            case ';':
                _position++;
                Emit(TokenKind.Semicolon, start);
                return;
            case '.':
                _position++;
                Emit(TokenKind.Dot, start);
                return;
            case '@':
                _position++;
                Emit(TokenKind.At, start);
                return;
            case '~':
                _position++;
                Emit(TokenKind.Tilde, start);
                return;
            case '^':
                _position++;
                Emit(TokenKind.Caret, start);
                return;
            case '%':
                _position++;
                Emit(TokenKind.Percent, start);
                return;
            case '+':
                EmitOperator(start, ("++", TokenKind.PlusPlus), ("+=", TokenKind.PlusEquals), ("+", TokenKind.Plus));
                return;
            case '-':
                EmitOperator(start, ("->", TokenKind.Arrow), ("--", TokenKind.MinusMinus), ("-=", TokenKind.MinusEquals), ("-", TokenKind.Minus));
                return;
            case '*':
                EmitOperator(start, ("*=", TokenKind.StarEquals), ("*", TokenKind.Star));
                return;
            case '/':
                EmitOperator(start, ("/=", TokenKind.SlashEquals), ("/", TokenKind.Slash));
                return;
            case '!':
                EmitOperator(start, ("!=>", TokenKind.BangEqualsGreater), ("!=", TokenKind.BangEqual), ("!", TokenKind.Bang));
                return;
            case '=':
                EmitOperator(start, ("=>", TokenKind.EqualsGreater), ("==", TokenKind.EqualEqual), ("=", TokenKind.Equals));
                return;
            case '<':
                EmitOperator(start, ("<=", TokenKind.LessEqual), ("<", TokenKind.Less));
                return;
            case '>':
                EmitOperator(start, (">=", TokenKind.GreaterEqual), (">", TokenKind.Greater));
                return;
            case '&':
                EmitOperator(start, ("&&", TokenKind.AmpersandAmpersand), ("&", TokenKind.Ampersand));
                return;
            case '|':
                EmitOperator(start, ("||", TokenKind.PipePipe), ("|", TokenKind.Pipe));
                return;
            case '?':
                EmitOperator(start, ("?.", TokenKind.QuestionDot), ("??", TokenKind.QuestionQuestion), ("?", TokenKind.Question));
                return;
            default:
                if (IsAsciiDigit(ch))
                {
                    LexNumber();
                    return;
                }

                if (IsIdentifierStart(start))
                {
                    LexIdentifier();
                    return;
                }

                var length = AdvanceRune();
                Diagnose(DiagnosticCodes.UnrecognizedCharacter, "Tanınmayan karakter.", start, length);
                Emit(TokenKind.Bad, start);
                return;
        }
    }

    private void EmitOperator(int start, params (string Text, TokenKind Kind)[] options)
    {
        foreach (var (text, kind) in options)
        {
            if (TryConsume(text))
            {
                Emit(kind, start);
                return;
            }
        }
    }

    private void LexNumber()
    {
        var start = _position;
        while (_position < _text.Length && IsAsciiDigit(_text[_position]))
        {
            _position++;
        }

        if (_position < _text.Length && _text[_position] == '.'
            && _position + 1 < _text.Length && IsAsciiDigit(_text[_position + 1]))
        {
            _position++;
            while (_position < _text.Length && IsAsciiDigit(_text[_position]))
            {
                _position++;
            }

            Emit(TokenKind.RealLiteral, start);
            return;
        }

        Emit(TokenKind.IntegerLiteral, start);
    }

    private void LexIdentifier()
    {
        var start = _position;
        AdvanceRune();
        while (_position < _text.Length && IsIdentifierPart(_position))
        {
            AdvanceRune();
        }

        var text = _text.AsSpan(start, _position - start);
        if (text.Equals("_başında", StringComparison.Ordinal))
        {
            Emit(TokenKind.BasindaUnderscore, start);
            return;
        }

        if (Keywords.TryMatch(text, out var keyword))
        {
            Emit(keyword, start);
            return;
        }

        Emit(TokenKind.Identifier, start);
    }

    private void LexString()
    {
        var quote = _position;
        _position++;
        var delimiterStart = -1;

        while (true)
        {
            if (_position >= _text.Length || IsNewline(_text[_position]))
            {
                Diagnose(DiagnosticCodes.UnterminatedString, "Metin sabiti kapanmadı.", quote, _position - quote);
                if (delimiterStart >= 0)
                {
                    Emit(TokenKind.InterpolationEnd, delimiterStart);
                }
                else if (_tokens.Count == 0 || _tokens[^1].Span.Start != quote)
                {
                    Emit(TokenKind.StringLiteral, quote);
                }

                return;
            }

            var ch = _text[_position];
            if (ch == '\\')
            {
                ConsumeEscape();
                continue;
            }

            if (ch == '{' && _position + 1 < _text.Length && _text[_position + 1] == '{')
            {
                _position += 2;
                continue;
            }

            if (ch == '}' && _position + 1 < _text.Length && _text[_position + 1] == '}')
            {
                _position += 2;
                continue;
            }

            if (ch == '"')
            {
                _position++;
                if (delimiterStart >= 0)
                {
                    Emit(TokenKind.InterpolationEnd, delimiterStart);
                }
                else if (_tokens.Count == 0 || _tokens[^1].Span.Start != quote)
                {
                    Emit(TokenKind.StringLiteral, quote);
                }

                return;
            }

            if (ch == '{')
            {
                if (delimiterStart >= 0)
                {
                    _position++;
                    Emit(TokenKind.InterpolationMiddle, delimiterStart);
                }
                else
                {
                    var hole = _position;
                    _position = quote + 1;
                    Emit(TokenKind.InterpolationStart, quote);
                    _position = hole;
                    if (hole > quote + 1)
                    {
                        Emit(TokenKind.StringText, quote + 1);
                    }

                    _position = hole + 1;
                }

                LexHole(Math.Max(quote, _position - 1));
                delimiterStart = _position;
                if (_position < _text.Length && _text[_position] == '}')
                {
                    _position++;
                }

                continue;
            }

            AdvanceRune();
        }
    }

    private void LexHole(int bracePosition)
    {
        var depth = 1;
        while (_position < _text.Length)
        {
            EnsureTrivia();
            if (_position >= _text.Length)
            {
                break;
            }

            var ch = _text[_position];
            if (ch == '}' && depth == 1)
            {
                return;
            }

            if (ch == '{')
            {
                var start = _position;
                _position++;
                Emit(TokenKind.OpenBrace, start);
                depth++;
                continue;
            }

            if (ch == '}')
            {
                var start = _position;
                _position++;
                Emit(TokenKind.CloseBrace, start);
                depth--;
                continue;
            }

            var before = _position;
            var count = _tokens.Count;
            LexToken();
            if (_position == before && _tokens.Count == count)
            {
                AdvanceRune();
            }
        }

        var length = Math.Max(1, _position - bracePosition);
        Diagnose(DiagnosticCodes.UnterminatedInterpolation, "Metin içindeki süslü parantez kapanmadı.", bracePosition, length);
    }

    private void LexCharacter()
    {
        var start = _position;
        _position++;
        var values = 0;
        var closed = false;
        while (_position < _text.Length && !IsNewline(_text[_position]))
        {
            if (_text[_position] == '\'')
            {
                _position++;
                closed = true;
                break;
            }

            if (_text[_position] == '\\')
            {
                ConsumeEscape();
                values++;
                continue;
            }

            AdvanceRune();
            values++;
        }

        if (!closed || values != 1)
        {
            Diagnose(DiagnosticCodes.InvalidCharacterLiteral, "Karakter sabiti geçersiz.", start, _position - start);
        }

        Emit(TokenKind.CharacterLiteral, start);
    }

    private void ConsumeEscape()
    {
        var start = _position;
        _position++;
        if (_position >= _text.Length || IsNewline(_text[_position]))
        {
            Diagnose(DiagnosticCodes.InvalidEscape, "Geçersiz kaçış dizisi.", start, _position - start);
            return;
        }

        var escaped = _text[_position];
        var length = escaped is '"' or '\\' or 'n' or 'r' or 't' or '\'' ? 1 : AdvanceRune();
        if (escaped is '"' or '\\' or 'n' or 'r' or 't' or '\'')
        {
            _position++;
            return;
        }

        Diagnose(DiagnosticCodes.InvalidEscape, "Geçersiz kaçış dizisi.", start, 1 + length);
    }

    private ImmutableArray<Trivia> ReadTrivia()
    {
        var list = new List<Trivia>();
        while (_position < _text.Length)
        {
            if (IsNewline(_text[_position]))
            {
                var start = _position;
                if (_text[_position] == '\r' && _position + 1 < _text.Length && _text[_position + 1] == '\n')
                {
                    _position += 2;
                }
                else
                {
                    _position++;
                }

                list.Add(new Trivia(TriviaKind.NewLine, new TextSpan(start, _position - start)));
                continue;
            }

            if (IsWhitespaceAt(_position))
            {
                var start = _position;
                while (_position < _text.Length && IsWhitespaceAt(_position))
                {
                    AdvanceRune();
                }

                list.Add(new Trivia(TriviaKind.Whitespace, new TextSpan(start, _position - start)));
                continue;
            }

            if (_position + 1 < _text.Length && _text[_position] == '/' && _text[_position + 1] == '/')
            {
                var start = _position;
                _position += 2;
                while (_position < _text.Length && !IsNewline(_text[_position]))
                {
                    AdvanceRune();
                }

                list.Add(new Trivia(TriviaKind.LineComment, new TextSpan(start, _position - start)));
                continue;
            }

            if (_position + 1 < _text.Length && _text[_position] == '/' && _text[_position + 1] == '*')
            {
                var start = _position;
                _position += 2;
                var closed = false;
                while (_position < _text.Length)
                {
                    if (_text[_position] == '*' && _position + 1 < _text.Length && _text[_position + 1] == '/')
                    {
                        _position += 2;
                        closed = true;
                        break;
                    }

                    AdvanceRune();
                }

                if (!closed)
                {
                    Diagnose(DiagnosticCodes.UnterminatedBlockComment, "Blok yorumu kapanmadı.", start, _position - start, "*/");
                }

                list.Add(new Trivia(TriviaKind.BlockComment, new TextSpan(start, _position - start)));
                continue;
            }

            break;
        }

        return list.Count == 0 ? ImmutableArray<Trivia>.Empty : list.ToImmutableArray();
    }

    private void EnsureTrivia()
    {
        _pendingTrivia ??= ReadTrivia();
    }

    private void Emit(TokenKind kind, int start)
    {
        EnsureTrivia();
        var trivia = _pendingTrivia ?? ImmutableArray<Trivia>.Empty;
        _pendingTrivia = null;
        var length = Math.Max(0, _position - start);
        _tokens.Add(Token.Create(kind, new TextSpan(start, length), trivia));
    }

    private bool TryConsume(string value)
    {
        if (_position + value.Length > _text.Length)
        {
            return false;
        }

        if (!_text.AsSpan(_position, value.Length).Equals(value, StringComparison.Ordinal))
        {
            return false;
        }

        _position += value.Length;
        return true;
    }

    private void Diagnose(string code, string message, int start, int length, string? suggestion = null)
    {
        if (length < 0)
        {
            length = 0;
        }

        if (start > _text.Length)
        {
            start = _text.Length;
        }

        if (start + length > _text.Length)
        {
            length = _text.Length - start;
        }

        var span = new TextSpan(start, length);
        var (line, column) = _file.Lines.GetLineAndColumn(start);
        _diagnostics.Add(new Diagnostic(
            code,
            DiagnosticSeverity.Error,
            message,
            _file.Path,
            span,
            line,
            column,
            suggestion));
    }

    private int AdvanceRune()
    {
        if (_position >= _text.Length)
        {
            return 0;
        }

        var length = ReadRuneAt(_position).Length;
        _position += length;
        return length;
    }

    private (Rune Rune, int Length) ReadRuneAt(int index)
    {
        var status = Rune.DecodeFromUtf16(_text.AsSpan(index), out var rune, out var consumed);
        if (status != OperationStatus.Done || consumed <= 0)
        {
            return (Rune.ReplacementChar, 1);
        }

        return (rune, consumed);
    }

    private bool IsIdentifierStart(int index)
    {
        var (rune, _) = ReadRuneAt(index);
        return rune.Value == '_' || Rune.IsLetter(rune);
    }

    private bool IsIdentifierPart(int index)
    {
        var (rune, _) = ReadRuneAt(index);
        if (rune.Value == '_' || Rune.IsLetter(rune) || Rune.IsDigit(rune))
        {
            return true;
        }

        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
    }

    private bool IsWhitespaceAt(int index)
    {
        var (rune, _) = ReadRuneAt(index);
        return rune.Value is ' ' or '\t' or '\v' or '\f'
            || Rune.GetUnicodeCategory(rune) == UnicodeCategory.SpaceSeparator;
    }

    private static bool IsAsciiDigit(char ch) => ch is >= '0' and <= '9';

    private static bool IsNewline(char ch) => ch is '\n' or '\r';
}
