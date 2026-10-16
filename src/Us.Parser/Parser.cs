using System.Collections.Immutable;
using Us.Core;
using Us.Lexer;

namespace Us.Parser;

public sealed class Parser
{
    private readonly SourceFile _file;
    private readonly ImmutableArray<Token> _tokens;
    private readonly DiagnosticBag _diagnostics;
    private int _index;

    private Parser(SourceFile file, ImmutableArray<Token> tokens, DiagnosticBag diagnostics)
    {
        _file = file;
        _tokens = tokens;
        _diagnostics = diagnostics;
    }

    public static ParseResult Parse(SourceFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var lexed = global::Us.Lexer.Lexer.Tokenize(file);
        var diagnostics = new DiagnosticBag();
        foreach (var diagnostic in lexed.Diagnostics.ToImmutableArray())
        {
            diagnostics.Add(diagnostic);
        }

        var parser = new Parser(file, lexed.Tokens, diagnostics);
        var root = parser.ParseCompilationUnit();
        return new ParseResult(root, diagnostics);
    }

    private Token Current => _tokens[_index];

    private CompilationUnitSyntax ParseCompilationUnit()
    {
        var members = ParseMembers(stopAtCloseBrace: false);
        var end = Current.Kind == TokenKind.EndOfFile
            ? Eat()
            : SyntaxToken.Missing(TokenKind.EndOfFile, Current.Span.Start);
        return new CompilationUnitSyntax(members, end);
    }

    private ImmutableArray<SyntaxNode> ParseMembers(bool stopAtCloseBrace)
    {
        var builder = ImmutableArray.CreateBuilder<SyntaxNode>();
        while (!IsStop(stopAtCloseBrace))
        {
            var start = _index;
            builder.Add(ParseMember());
            if (_index == start)
            {
                Error(DiagnosticCodes.UnexpectedToken, "Beklenmeyen belirteç.", Current);
                builder.Add(new ErrorStatementSyntax(Eat()));
            }
        }

        return builder.ToImmutable();
    }

    private bool IsStop(bool stopAtCloseBrace) =>
        Current.Kind == TokenKind.EndOfFile || (stopAtCloseBrace && Current.Kind == TokenKind.CloseBrace);

    private SyntaxNode ParseMember()
    {
        var route = Current.Kind == TokenKind.Rota ? ParseRoute() : null;
        var modifiers = ParseModifiers();
        if (route is not null || Current.Kind is TokenKind.Fonksiyon or TokenKind.Afonksiyon)
        {
            return ParseFunction(modifiers, route);
        }

        switch (Current.Kind)
        {
            case TokenKind.Semicolon:
                return new EmptyStatementSyntax(Eat());
            case TokenKind.OpenBrace:
                return ParseBlock();
            case TokenKind.EqualsGreater:
                return ParseResultStatement();
            case TokenKind.Eger:
                return ParseIfOrExpressionIf();
            case TokenKind.Don:
                return ParseDon();
            case TokenKind.Tekrarla:
                return ParseWhile();
            case TokenKind.Yap:
                return ParseDoWhile();
            case TokenKind.Sonsuzdon:
                return ParseInfinite();
            case TokenKind.Cik:
                return new BreakStatementSyntax(Eat(), TrySemicolon());
            case TokenKind.Devam:
                return new ContinueStatementSyntax(Eat(), TrySemicolon());
            case TokenKind.Model:
                return ParseModel(modifiers);
            case TokenKind.Sinif:
                return ParseClass(modifiers);
            case TokenKind.Olustur:
                return ParseConstructor(modifiers);
            case TokenKind.Arayuz:
                return ParseInterface();
            case TokenKind.Secenek:
                return ParseOption();
            case TokenKind.Sec:
                return ParseSelect();
            case TokenKind.Basinda:
            case TokenKind.BasindaUnderscore:
            case TokenKind.Dene:
                return ParseTry();
            case TokenKind.Firlat:
                return new ThrowStatementSyntax(Eat(), ParseExpression(), TrySemicolon());
            case TokenKind.Olay:
                return ParseEvent(modifiers);
            case TokenKind.Eesislem:
                return new ConcurrentStatementSyntax(Eat(), ParseBlock());
            case TokenKind.Dogrula:
                return ParseValidation();
            case TokenKind.Donusum:
                return ParseMapping();
            case TokenKind.Alan:
                return ParseNamespace();
            case TokenKind.Al:
                return ParseImport();
            case TokenKind.Veritabani:
                return ParseDatabase();
            case TokenKind.Islem:
                return new TransactionStatementSyntax(Eat(), ParseBlock());
            case TokenKind.Onayla:
                return new CommitStatementSyntax(Eat(), TrySemicolon());
            case TokenKind.Gerial:
                return new RollbackStatementSyntax(Eat(), TrySemicolon());
            case TokenKind.Kontrolcu:
                return ParseController();
            case TokenKind.Servis when Peek(1) is TokenKind.Dot or TokenKind.OpenParen:
                break;
            case TokenKind.Servis:
                return ParseService();
            case TokenKind.Test:
                return ParseTest();
            case TokenKind.Beklenen:
                return new AssertionStatementSyntax(Eat(), ParseExpression(), TrySemicolon());
            case TokenKind.Bagla:
                return ParseBinding();
            case TokenKind.Edegilse:
            case TokenKind.Degilse:
                Error(DiagnosticCodes.ElseBeforeElseIf, "`edeğilse` ve `değilse` bir `eğer` gövdesinden sonra gelmelidir.", Current);
                return new ErrorStatementSyntax(Eat());
        }

        if (LooksLikeDeclaration(Current))
        {
            return ParseVariable(modifiers);
        }

        if (IsExpectedError())
        {
            return ParseExpectedError();
        }

        if (modifiers.Length > 0 && IsName(Current.Kind))
        {
            return ParseVariable(modifiers);
        }

        var expression = ParseExpression();
        return new ExpressionStatementSyntax(expression, TrySemicolon());
    }

    private ImmutableArray<SyntaxToken> ParseModifiers()
    {
        var builder = ImmutableArray.CreateBuilder<SyntaxToken>();
        while (IsModifier(Current.Kind))
        {
            builder.Add(Eat());
        }

        return builder.ToImmutable();
    }

    private StatementSyntax ParseIfOrExpressionIf()
    {
        var eger = Eat();
        var condition = ParsePrecedence(OperatorPrecedence.NullCoalesce);
        if (Current.Kind == TokenKind.EqualsGreater)
        {
            var expression = FinishExpressionIf(eger, condition);
            return new ExpressionStatementSyntax(expression, TrySemicolon());
        }

        var body = ParseBody();
        var elseIfs = ImmutableArray.CreateBuilder<ElseIfClauseSyntax>();
        while (Current.Kind == TokenKind.Edegilse)
        {
            var keyword = Eat();
            var elseCondition = ParseExpression();
            elseIfs.Add(new ElseIfClauseSyntax(keyword, elseCondition, ParseBody()));
        }

        SyntaxToken? degilse = null;
        StatementSyntax? elseBody = null;
        if (Current.Kind == TokenKind.Degilse)
        {
            degilse = Eat();
            elseBody = ParseBody();
        }

        if (Current.Kind == TokenKind.Edegilse)
        {
            Error(DiagnosticCodes.ElseBeforeElseIf, "`edeğilse`, `değilse` kolundan sonra yazılamaz.", Current);
        }

        return new IfStatementSyntax(eger, condition, body, elseIfs.ToImmutable(), degilse, elseBody);
    }

    private StatementSyntax ParseBody() =>
        Current.Kind == TokenKind.OpenBrace ? ParseBlock() : ParseStatementAvoidingDeclaration();

    private StatementSyntax ParseStatementAvoidingDeclaration()
    {
        if (Current.Kind == TokenKind.OpenBrace)
        {
            return ParseBlock();
        }

        var node = ParseMember();
        return node as StatementSyntax ?? new ExpressionStatementSyntax(new IdentifierExpressionSyntax(SyntaxToken.Missing(TokenKind.Identifier, node.Span.Start)), null);
    }

    private StatementSyntax ParseDon()
    {
        var don = Eat();
        var first = ParseExpression();
        if (Current.Kind == TokenKind.Ile && Peek(1) != TokenKind.OpenBrace)
        {
            var ile = Eat();
            var end = ParseExpression();
            SyntaxToken? adim = null;
            ExpressionSyntax? step = null;
            if (Current.Kind == TokenKind.Adim)
            {
                adim = Eat();
                step = ParseExpression();
            }

            var arrow = Expect(TokenKind.Arrow, DiagnosticCodes.MissingLoopArrow, "`->` bekleniyor.");
            var name = ExpectName("Ad bekleniyor.");
            return new RangeForStatementSyntax(don, first, ile, end, adim, step, arrow, name, ParseBody());
        }

        var eachArrow = Expect(TokenKind.Arrow, DiagnosticCodes.MissingLoopArrow, "`->` bekleniyor.");
        var eachName = ExpectName("Ad bekleniyor.");
        return new ForEachStatementSyntax(don, first, eachArrow, eachName, ParseBody());
    }

    private WhileStatementSyntax ParseWhile()
    {
        var keyword = Eat();
        var condition = ParseExpression();
        return new WhileStatementSyntax(keyword, condition, ParseBody());
    }

    private DoWhileStatementSyntax ParseDoWhile()
    {
        var yap = Eat();
        var body = ParseBody();
        var tekrarla = Expect(TokenKind.Tekrarla, DiagnosticCodes.MissingDoWhile, "`tekrarla` bekleniyor.");
        var condition = tekrarla.IsMissing
            ? new IdentifierExpressionSyntax(SyntaxToken.Missing(TokenKind.Identifier, tekrarla.Span.Start))
            : ParseExpression();
        return new DoWhileStatementSyntax(yap, body, tekrarla, condition);
    }

    private InfiniteLoopStatementSyntax ParseInfinite()
    {
        var keyword = Eat();
        return new InfiniteLoopStatementSyntax(keyword, ParseBody());
    }

    private ResultStatementSyntax ParseResultStatement()
    {
        var arrow = Eat();
        return new ResultStatementSyntax(arrow, ParseExpression(), TrySemicolon());
    }

    private BlockSyntax ParseBlock()
    {
        var open = Expect(TokenKind.OpenBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        if (open.TokenKind != TokenKind.OpenBrace)
        {
            return new BlockSyntax(open, ImmutableArray<SyntaxNode>.Empty, SyntaxToken.Missing(TokenKind.CloseBrace, open.Span.Start));
        }

        var statements = ParseMembers(stopAtCloseBrace: true);
        var close = Current.Kind == TokenKind.CloseBrace
            ? Eat()
            : Expect(TokenKind.CloseBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        return new BlockSyntax(open, statements, close);
    }

    private FunctionDeclarationSyntax ParseFunction(ImmutableArray<SyntaxToken> modifiers, RouteDeclarationSyntax? route)
    {
        var keyword = Current.Kind is TokenKind.Fonksiyon or TokenKind.Afonksiyon
            ? Eat()
            : Expect(TokenKind.Fonksiyon, DiagnosticCodes.UnexpectedToken, "`fonksiyon` bekleniyor.");
        var name = ExpectName("Ad bekleniyor.");
        var typeParameters = Current.Kind == TokenKind.Less ? ParseTypeParameters() : null;
        var open = Expect(TokenKind.OpenParen, DiagnosticCodes.UnexpectedToken, "`(` bekleniyor.");
        var parameters = ParseParameters();
        var close = Expect(TokenKind.CloseParen, DiagnosticCodes.UnexpectedToken, "`)` bekleniyor.");
        SyntaxToken? colon = null;
        TypeSyntax? returnType = null;
        if (Current.Kind == TokenKind.Colon)
        {
            colon = Eat();
            returnType = ParseType();
        }

        SyntaxToken? arrow = null;
        SyntaxNode? body = null;
        if (Current.Kind == TokenKind.EqualsGreater)
        {
            arrow = Eat();
            body = Current.Kind == TokenKind.OpenBrace ? ParseBlock() : ParseExpression();
        }
        else if (Current.Kind == TokenKind.OpenBrace)
        {
            body = ParseBlock();
        }

        return new FunctionDeclarationSyntax(modifiers, route, keyword, name, typeParameters, open, parameters, close, colon, returnType, arrow, body);
    }

    private ImmutableArray<ParameterSyntax> ParseParameters()
    {
        var builder = ImmutableArray.CreateBuilder<ParameterSyntax>();
        while (Current.Kind is not TokenKind.CloseParen and not TokenKind.EndOfFile and not TokenKind.OpenBrace)
        {
            var start = _index;
            SyntaxToken? modifier = Current.Kind is TokenKind.Ref or TokenKind.Cikti ? Eat() : null;
            var name = ExpectName("Ad bekleniyor.");
            if (Current.Kind == TokenKind.Colon)
            {
                var colon = Eat();
                var type = ParseType();
                var (equalsToken, value) = ParseDefaultValue();
                var comma = Current.Kind == TokenKind.Comma ? Eat() : null;
                builder.Add(new ParameterSyntax(modifier, name, colon, type, equalsToken, value, comma));
            }
            else
            {
                Error(DiagnosticCodes.UnexpectedToken, "Tür bekleniyor.", name.Token);
                var colon = SyntaxToken.Missing(TokenKind.Colon, name.Span.End);
                var type = MissingType(name.Span.End);
                var comma = Current.Kind == TokenKind.Comma ? Eat() : null;
                builder.Add(new ParameterSyntax(modifier, name, colon, type, null, null, comma));
            }

            if (Current.Kind != TokenKind.Comma && _index == start)
            {
                break;
            }

            if (Current.Kind is TokenKind.CloseParen or TokenKind.EndOfFile)
            {
                break;
            }

            if (builder.Count > 0 && builder[^1].Comma is null)
            {
                break;
            }
        }

        return builder.ToImmutable();
    }

    private (SyntaxToken? EqualsToken, ExpressionSyntax? Value) ParseDefaultValue()
    {
        if (Current.Kind != TokenKind.Equals)
        {
            return (null, null);
        }

        var equals = Eat();
        return (equals, ParseExpression());
    }

    private bool IsExpectedError() =>
        Current.Kind == TokenKind.Identifier &&
        TokenText(Current) == "beklenenHata" &&
        Peek(1) == TokenKind.OpenBrace;

    private ExpectedErrorStatementSyntax ParseExpectedError()
    {
        var keyword = Eat();
        return new ExpectedErrorStatementSyntax(keyword, ParseBlock());
    }

    private VariableDeclarationSyntax ParseVariable(ImmutableArray<SyntaxToken> modifiers)
    {
        var names = ImmutableArray.CreateBuilder<SyntaxToken>();
        var commas = ImmutableArray.CreateBuilder<SyntaxToken>();
        names.Add(ExpectName("Ad bekleniyor."));
        while (Current.Kind == TokenKind.Comma)
        {
            commas.Add(Eat());
            names.Add(ExpectName("Ad bekleniyor."));
        }

        var colon = Expect(TokenKind.Colon, DiagnosticCodes.UnexpectedToken, "`:` bekleniyor.");
        var type = ParseType();
        SyntaxToken? equals = null;
        var initializers = ImmutableArray.CreateBuilder<ExpressionSyntax>();
        var initializerCommas = ImmutableArray.CreateBuilder<SyntaxToken>();
        if (Current.Kind == TokenKind.Equals)
        {
            equals = Eat();
            initializers.Add(ParseInitializerExpression());
            while (Current.Kind == TokenKind.Comma)
            {
                initializerCommas.Add(Eat());
                initializers.Add(ParseInitializerExpression());
            }
        }

        return new VariableDeclarationSyntax(modifiers, names.ToImmutable(), commas.ToImmutable(), colon, type, equals, initializers.ToImmutable(), initializerCommas.ToImmutable(), TrySemicolon());
    }

    private ExpressionSyntax ParseInitializerExpression()
    {
        if (HasLeadingNewline(Current) && (LooksLikeDeclaration(Current) || IsHardStatement(Current.Kind)))
        {
            Error(DiagnosticCodes.UnexpectedToken, "İfade bekleniyor.", Current);
            return new IdentifierExpressionSyntax(SyntaxToken.Missing(TokenKind.Identifier, Current.Span.Start));
        }

        return ParseExpression();
    }

    private ModelDeclarationSyntax ParseModel(ImmutableArray<SyntaxToken> modifiers)
    {
        var keyword = Eat();
        var name = ExpectName("Ad bekleniyor.");
        var typeParameters = Current.Kind == TokenKind.Less ? ParseTypeParameters() : null;
        SyntaxToken? colon = null;
        SyntaxToken? settingName = null;
        SyntaxToken? settingOpen = null;
        SyntaxToken? settingArgument = null;
        SyntaxToken? settingClose = null;
        if (Current.Kind == TokenKind.Colon)
        {
            colon = Eat();
            settingName = ExpectName("Ad bekleniyor.");
            settingOpen = Expect(TokenKind.OpenParen, DiagnosticCodes.UnexpectedToken, "`(` bekleniyor.");
            settingArgument = Expect(TokenKind.StringLiteral, DiagnosticCodes.UnexpectedToken, "Metin bekleniyor.");
            settingClose = Expect(TokenKind.CloseParen, DiagnosticCodes.UnexpectedToken, "`)` bekleniyor.");
        }

        var open = Expect(TokenKind.OpenBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        var fields = ImmutableArray.CreateBuilder<ModelFieldSyntax>();
        while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
        {
            var start = _index;
            var fieldModifiers = ParseModifiers();
            fields.Add(ParseModelField(fieldModifiers));
            if (_index == start)
            {
                Error(DiagnosticCodes.UnexpectedToken, "Beklenmeyen belirteç.", Current);
                if (Current.Kind is TokenKind.CloseBrace or TokenKind.EndOfFile)
                {
                    break;
                }

                Eat();
            }
        }

        var close = Expect(TokenKind.CloseBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        _ = modifiers;
        return new ModelDeclarationSyntax(keyword, name, typeParameters, open, fields.ToImmutable(), close, colon, settingName, settingOpen, settingArgument, settingClose);
    }

    private ModelFieldSyntax ParseModelField(ImmutableArray<SyntaxToken> modifiers)
    {
        var names = ImmutableArray.CreateBuilder<SyntaxToken>();
        var commas = ImmutableArray.CreateBuilder<SyntaxToken>();
        names.Add(ExpectName("Ad bekleniyor."));
        while (Current.Kind == TokenKind.Comma)
        {
            commas.Add(Eat());
            names.Add(ExpectName("Ad bekleniyor."));
        }

        var colon = Expect(TokenKind.Colon, DiagnosticCodes.UnexpectedToken, "`:` bekleniyor.");
        var type = ParseType();
        SyntaxToken? ruleColon = null;
        var constraints = ImmutableArray.CreateBuilder<ConstraintSyntax>();
        if (Current.Kind == TokenKind.Colon)
        {
            ruleColon = Eat();
            while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
            {
                if (HasLeadingNewline(Current) && LooksLikeDeclaration(Current))
                {
                    break;
                }

                var constraintName = ExpectName("Ad bekleniyor.");
                ArgumentListSyntax? arguments = Current.Kind == TokenKind.OpenParen ? ParseArguments() : null;
                var comma = Current.Kind == TokenKind.Comma ? Eat() : null;
                constraints.Add(new ConstraintSyntax(constraintName, arguments, comma));
                if (comma is null)
                {
                    break;
                }
            }
        }

        return new ModelFieldSyntax(modifiers, names.ToImmutable(), commas.ToImmutable(), colon, type, ruleColon, constraints.ToImmutable());
    }

    private ClassDeclarationSyntax ParseClass(ImmutableArray<SyntaxToken> modifiers)
    {
        var keyword = Eat();
        var name = ExpectName("Ad bekleniyor.");
        var typeParameters = Current.Kind == TokenKind.Less ? ParseTypeParameters() : null;
        SyntaxToken? colon = null;
        var bases = ImmutableArray.CreateBuilder<TypeSyntax>();
        var commas = ImmutableArray.CreateBuilder<SyntaxToken>();
        if (Current.Kind == TokenKind.Colon)
        {
            colon = Eat();
            bases.Add(ParseType());
            while (Current.Kind == TokenKind.Comma)
            {
                commas.Add(Eat());
                bases.Add(ParseType());
            }
        }

        var open = Expect(TokenKind.OpenBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        var members = ParseMembers(stopAtCloseBrace: true);
        var close = Expect(TokenKind.CloseBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        return new ClassDeclarationSyntax(modifiers, keyword, name, typeParameters, colon, bases.ToImmutable(), commas.ToImmutable(), open, members, close);
    }

    private ConstructorDeclarationSyntax ParseConstructor(ImmutableArray<SyntaxToken> modifiers)
    {
        var keyword = Eat();
        var open = Expect(TokenKind.OpenParen, DiagnosticCodes.UnexpectedToken, "`(` bekleniyor.");
        var parameters = ParseParameters();
        var close = Expect(TokenKind.CloseParen, DiagnosticCodes.UnexpectedToken, "`)` bekleniyor.");
        var body = Current.Kind == TokenKind.OpenBrace
            ? (SyntaxNode)ParseBlock()
            : ParseExpression();
        return new ConstructorDeclarationSyntax(modifiers, keyword, open, parameters, close, body);
    }

    private InterfaceDeclarationSyntax ParseInterface()
    {
        var keyword = Eat();
        var name = ExpectName("Ad bekleniyor.");
        var open = Expect(TokenKind.OpenBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        var members = ParseMembers(stopAtCloseBrace: true);
        var close = Expect(TokenKind.CloseBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        return new InterfaceDeclarationSyntax(keyword, name, open, members, close);
    }

    private OptionDeclarationSyntax ParseOption()
    {
        var keyword = Eat();
        var name = ExpectName("Ad bekleniyor.");
        var open = Expect(TokenKind.OpenBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        var members = ImmutableArray.CreateBuilder<SyntaxToken>();
        var commas = ImmutableArray.CreateBuilder<SyntaxToken>();
        while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
        {
            if (!IsName(Current.Kind))
            {
                Error(DiagnosticCodes.UnexpectedToken, "Ad bekleniyor.", Current);
                Eat();
                continue;
            }

            members.Add(Eat());
            if (Current.Kind == TokenKind.Comma)
            {
                commas.Add(Eat());
            }
        }

        var close = Expect(TokenKind.CloseBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        return new OptionDeclarationSyntax(keyword, name, open, members.ToImmutable(), commas.ToImmutable(), close);
    }

    private SelectStatementSyntax ParseSelect()
    {
        var keyword = Eat();
        var expression = ParseExpression();
        var open = Expect(TokenKind.OpenBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        var arms = ImmutableArray.CreateBuilder<SelectArmSyntax>();
        while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
        {
            var start = _index;
            var pattern = ParseExpression();
            var wildcard = pattern is IdentifierExpressionSyntax identifier && TokenText(identifier.Name.Token) == "_";
            var arrow = Expect(TokenKind.EqualsGreater, DiagnosticCodes.UnexpectedToken, "`=>` bekleniyor.");
            var body = ParseBody();
            arms.Add(new SelectArmSyntax(pattern, wildcard, arrow, body));
            if (_index == start)
            {
                Eat();
            }
        }

        var close = Expect(TokenKind.CloseBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        return new SelectStatementSyntax(keyword, expression, open, arms.ToImmutable(), close);
    }

    private TryStatementSyntax ParseTry()
    {
        SyntaxToken? preparation = null;
        var underscore = false;
        BlockSyntax? preparationBody = null;
        if (Current.Kind is TokenKind.Basinda or TokenKind.BasindaUnderscore)
        {
            underscore = Current.Kind == TokenKind.BasindaUnderscore;
            preparation = Eat();
            preparationBody = ParseBlock();
            if (Current.Kind is TokenKind.Basinda or TokenKind.BasindaUnderscore)
            {
                Error(DiagnosticCodes.BothPreparations, "`başında` ve `_başında` birlikte yazılamaz.", Current);
                Eat();
                _ = ParseBlock();
            }
        }

        var dene = Expect(TokenKind.Dene, DiagnosticCodes.UnexpectedToken, "`dene` bekleniyor.");
        var body = Current.Kind == TokenKind.OpenBrace ? ParseBlock() : null;
        var catches = ImmutableArray.CreateBuilder<CatchClauseSyntax>();
        while (Current.Kind == TokenKind.Yakala)
        {
            var keyword = Eat();
            var name = ExpectName("Ad bekleniyor.");
            SyntaxToken? colon = null;
            TypeSyntax? type = null;
            if (Current.Kind == TokenKind.Colon)
            {
                colon = Eat();
                type = ParseType();
            }

            catches.Add(new CatchClauseSyntax(keyword, name, colon, type, ParseBlock()));
        }

        SyntaxToken? sonunda = null;
        BlockSyntax? finallyBody = null;
        if (Current.Kind == TokenKind.Sonunda)
        {
            sonunda = Eat();
            finallyBody = ParseBlock();
        }

        return new TryStatementSyntax(preparation, underscore, preparationBody, dene, body, catches.ToImmutable(), sonunda, finallyBody);
    }

    private EventDeclarationSyntax ParseEvent(ImmutableArray<SyntaxToken> modifiers)
    {
        var keyword = Eat();
        var name = ExpectName("Ad bekleniyor.");
        var colon = Expect(TokenKind.Colon, DiagnosticCodes.UnexpectedToken, "`:` bekleniyor.");
        var type = ParseType();
        return new EventDeclarationSyntax(modifiers, keyword, name, colon, type, TrySemicolon());
    }

    private ValidationStatementSyntax ParseValidation()
    {
        var keyword = Eat();
        var expression = ParseExpression();
        var open = Expect(TokenKind.OpenBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        var rules = ImmutableArray.CreateBuilder<RuleClauseSyntax>();
        while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
        {
            var start = _index;
            var ruleKeyword = Expect(TokenKind.Kural, DiagnosticCodes.UnexpectedToken, "`kural` bekleniyor.");
            var condition = ParsePrecedence(OperatorPrecedence.NullCoalesce);
            var arrow = Expect(TokenKind.EqualsGreater, DiagnosticCodes.UnexpectedToken, "`=>` bekleniyor.");
            var message = ParseExpression();
            rules.Add(new RuleClauseSyntax(ruleKeyword, condition, arrow, message));
            if (_index == start)
            {
                Eat();
            }
        }

        var close = Expect(TokenKind.CloseBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        return new ValidationStatementSyntax(keyword, expression, open, rules.ToImmutable(), close);
    }

    private MappingDeclarationSyntax ParseMapping()
    {
        var keyword = Eat();
        var source = ParseType();
        var arrow = Expect(TokenKind.EqualsGreater, DiagnosticCodes.UnexpectedToken, "`=>` bekleniyor.");
        var destination = ParseType();
        var open = Expect(TokenKind.OpenBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        var members = ParseInitializers();
        var close = Expect(TokenKind.CloseBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        return new MappingDeclarationSyntax(keyword, source, arrow, destination, open, members, close);
    }

    private NamespaceDeclarationSyntax ParseNamespace()
    {
        var keyword = Eat();
        var (names, dots) = ParseDottedName();
        SyntaxToken? colon = null;
        SyntaxToken? visibility = null;
        if (Current.Kind == TokenKind.Colon)
        {
            colon = Eat();
            visibility = IsModifier(Current.Kind)
                ? Eat()
                : ExpectName("Ad bekleniyor.");
        }

        return new NamespaceDeclarationSyntax(keyword, names, dots, colon, visibility, TrySemicolon());
    }

    private ImportDeclarationSyntax ParseImport()
    {
        var keyword = Eat();
        var (names, dots) = ParseDottedName();
        SyntaxToken? olarak = null;
        SyntaxToken? alias = null;
        if (Current.Kind == TokenKind.Olarak)
        {
            olarak = Eat();
            alias = ExpectName("Ad bekleniyor.");
        }

        return new ImportDeclarationSyntax(keyword, names, dots, olarak, alias, TrySemicolon());
    }

    private (ImmutableArray<SyntaxToken> Names, ImmutableArray<SyntaxToken> Dots) ParseDottedName()
    {
        var names = ImmutableArray.CreateBuilder<SyntaxToken>();
        var dots = ImmutableArray.CreateBuilder<SyntaxToken>();
        names.Add(ParseNamePiece());
        while (Current.Kind == TokenKind.Dot)
        {
            dots.Add(Eat());
            names.Add(ParseNamePiece());
        }

        return (names.ToImmutable(), dots.ToImmutable());
    }

    private SyntaxToken ParseNamePiece()
    {
        if (Current.Kind == TokenKind.Star)
        {
            Error(DiagnosticCodes.WildcardImport, "`al` joker içe aktarmayı desteklemez.", Current);
            return Eat();
        }

        return ExpectName("Ad bekleniyor.");
    }

    private DatabaseDeclarationSyntax ParseDatabase()
    {
        var keyword = Eat();
        var name = ExpectName("Ad bekleniyor.");
        var open = Expect(TokenKind.OpenBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        var tables = ImmutableArray.CreateBuilder<TableDeclarationSyntax>();
        while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
        {
            var start = _index;
            var tableKeyword = Expect(TokenKind.Tablo, DiagnosticCodes.UnexpectedToken, "`tablo` bekleniyor.");
            var tableName = ExpectName("Ad bekleniyor.");
            var colon = Expect(TokenKind.Colon, DiagnosticCodes.UnexpectedToken, "`:` bekleniyor.");
            var type = ParseType();
            tables.Add(new TableDeclarationSyntax(tableKeyword, tableName, colon, type));
            if (_index == start)
            {
                Eat();
            }
        }

        var close = Expect(TokenKind.CloseBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        return new DatabaseDeclarationSyntax(keyword, name, open, tables.ToImmutable(), close);
    }

    private ControllerDeclarationSyntax ParseController()
    {
        var keyword = Eat();
        var name = ExpectName("Ad bekleniyor.");
        var colon = Expect(TokenKind.Colon, DiagnosticCodes.UnexpectedToken, "`:` bekleniyor.");
        var route = Expect(TokenKind.StringLiteral, DiagnosticCodes.UnexpectedToken, "Metin bekleniyor.");
        var open = Expect(TokenKind.OpenBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        var members = ParseMembers(stopAtCloseBrace: true);
        var close = Expect(TokenKind.CloseBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        return new ControllerDeclarationSyntax(keyword, name, colon, route, open, members, close);
    }

    private RouteDeclarationSyntax ParseRoute()
    {
        var keyword = Eat();
        var verb = ExpectName("Ad bekleniyor.");
        SyntaxNode path = Current.Kind == TokenKind.InterpolationStart
            ? ParseInterpolation()
            : Expect(TokenKind.StringLiteral, DiagnosticCodes.UnexpectedToken, "Metin bekleniyor.");
        return new RouteDeclarationSyntax(keyword, verb, path);
    }

    private ServiceDeclarationSyntax ParseService()
    {
        var keyword = Eat();
        var type = ParseType();
        SyntaxToken? colon = null;
        SyntaxToken? lifetime = null;
        if (Current.Kind == TokenKind.Colon)
        {
            colon = Eat();
            lifetime = Current.Kind is TokenKind.Kapsamli or TokenKind.Tekil or TokenKind.Gecici
                ? Eat()
                : ExpectName("Ömür bekleniyor.");
        }
        SyntaxToken? open = null;
        var members = ImmutableArray<SyntaxNode>.Empty;
        SyntaxToken? close = null;
        if (Current.Kind == TokenKind.OpenBrace)
        {
            open = Eat();
            members = ParseMembers(stopAtCloseBrace: true);
            close = Expect(TokenKind.CloseBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        }

        return new ServiceDeclarationSyntax(keyword, type, colon, lifetime, open, members, close);
    }

    private TestDeclarationSyntax ParseTest()
    {
        var keyword = Eat();
        var name = ParseExpression();
        return new TestDeclarationSyntax(keyword, name, ParseBlock());
    }

    private BindingStatementSyntax ParseBinding()
    {
        var keyword = Eat();
        var kind = ExpectName("Ad bekleniyor.");
        var colon = Expect(TokenKind.Colon, DiagnosticCodes.UnexpectedToken, "`:` bekleniyor.");
        var type = ParseType();
        return new BindingStatementSyntax(keyword, kind, colon, type, TrySemicolon());
    }

    private ExpressionSyntax ParseExpression()
    {
        var expression = ParsePrecedence(OperatorPrecedence.NullCoalesce);
        if (Current.Kind == TokenKind.Ile && Peek(1) == TokenKind.OpenBrace && !StopExpression())
        {
            expression = ParseCopy(expression);
        }

        while (OperatorPrecedence.IsAssignment(Current.Kind) && !StopExpression())
        {
            var op = Eat();
            var right = ParseExpression();
            expression = new AssignmentExpressionSyntax(expression, op, right);
        }

        return expression;
    }

    private ExpressionSyntax ParsePrecedence(int minimum)
    {
        var expression = ParseUnary();
        while (true)
        {
            var precedence = OperatorPrecedence.Infix(Current.Kind);
            if (precedence < minimum || StopExpression())
            {
                break;
            }

            var op = Eat();
            var right = ParsePrecedence(precedence + 1);
            expression = new BinaryExpressionSyntax(expression, op, right);
        }

        return expression;
    }

    private ExpressionSyntax ParseUnary()
    {
        if (Current.Kind is TokenKind.Bang or TokenKind.Minus or TokenKind.Plus or TokenKind.PlusPlus or TokenKind.MinusMinus)
        {
            var op = Eat();
            return new UnaryExpressionSyntax(op, ParseUnary());
        }

        if (Current.Kind == TokenKind.Bekle)
        {
            var bekle = Eat();
            return new AwaitExpressionSyntax(bekle, ParseUnary());
        }

        if (Current.Kind == TokenKind.At)
        {
            return ParseObjectCreation();
        }

        if (Current.Kind == TokenKind.Eger)
        {
            var eger = Eat();
            var condition = ParsePrecedence(OperatorPrecedence.NullCoalesce);
            return FinishExpressionIf(eger, condition);
        }

        return ParsePostfix();
    }

    private ExpressionIfSyntax FinishExpressionIf(SyntaxToken eger, ExpressionSyntax condition)
    {
        var arrow = Expect(TokenKind.EqualsGreater, DiagnosticCodes.UnexpectedToken, "`=>` bekleniyor.");
        var whenTrue = ParsePrecedence(OperatorPrecedence.NullCoalesce);
        var falseArrow = Current.Kind == TokenKind.BangEqualsGreater
            ? Eat()
            : Expect(TokenKind.BangEqualsGreater, DiagnosticCodes.MissingExpressionIfFalse, "`!=>` bekleniyor.");
        var whenFalse = falseArrow.IsMissing
            ? new IdentifierExpressionSyntax(SyntaxToken.Missing(TokenKind.Identifier, falseArrow.Span.Start))
            : ParseExpression();
        return new ExpressionIfSyntax(eger, condition, arrow, whenTrue, falseArrow, whenFalse);
    }

    private ExpressionSyntax ParsePostfix()
    {
        var expression = ParsePrimary();
        while (true)
        {
            if (Current.Kind is TokenKind.Dot or TokenKind.QuestionDot)
            {
                var op = Eat();
                var name = ExpectName("Ad bekleniyor.");
                expression = new MemberAccessExpressionSyntax(expression, op, name);
                continue;
            }

            if (Current.Kind == TokenKind.Less && LooksLikeGenericCall())
            {
                var types = ParseTypeArguments();
                var arguments = ParseArguments();
                expression = new InvocationExpressionSyntax(expression, types, arguments);
                continue;
            }

            if (Current.Kind == TokenKind.OpenParen)
            {
                expression = new InvocationExpressionSyntax(expression, null, ParseArguments());
                continue;
            }

            if (Current.Kind == TokenKind.OpenBracket)
            {
                var open = Eat();
                var index = ParseExpression();
                var close = Expect(TokenKind.CloseBracket, DiagnosticCodes.UnexpectedToken, "`]` bekleniyor.");
                expression = new IndexExpressionSyntax(expression, open, index, close);
                continue;
            }

            if (Current.Kind is TokenKind.PlusPlus or TokenKind.MinusMinus)
            {
                expression = new UnaryExpressionSyntax(expression, Eat());
                continue;
            }

            if (!HasLeadingNewline(Current) && CanJuxtapose(expression) && CanStartPrimary(Current.Kind))
            {
                var argument = ParsePrecedence(OperatorPrecedence.Postfix);
                var open = SyntaxToken.Missing(TokenKind.OpenParen, argument.Span.Start);
                var close = SyntaxToken.Missing(TokenKind.CloseParen, argument.Span.End);
                var list = new ArgumentListSyntax(open, [new ArgumentSyntax(null, argument, null)], close);
                expression = new InvocationExpressionSyntax(expression, null, list);
                continue;
            }

            break;
        }

        return expression;
    }

    private ExpressionSyntax ParsePrimary()
    {
        var keywordCall = IsName(Current.Kind) && Peek(1) is TokenKind.Dot or TokenKind.OpenParen;
        if (!keywordCall && (StopExpression() || Current.Kind is TokenKind.EqualsGreater or TokenKind.BangEqualsGreater or TokenKind.InterpolationMiddle or TokenKind.InterpolationEnd))
        {
            Error(DiagnosticCodes.UnexpectedToken, "İfade bekleniyor.", Current);
            return new IdentifierExpressionSyntax(SyntaxToken.Missing(TokenKind.Identifier, Current.Span.Start));
        }

        switch (Current.Kind)
        {
            case TokenKind.IntegerLiteral:
            case TokenKind.RealLiteral:
            case TokenKind.StringLiteral:
            case TokenKind.CharacterLiteral:
            case TokenKind.Dogru:
            case TokenKind.Yanlis:
            case TokenKind.Tilde:
                return new LiteralExpressionSyntax(Eat());
            case TokenKind.InterpolationStart:
                return ParseInterpolation();
            case TokenKind.Identifier:
                return new IdentifierExpressionSyntax(Eat());
            case TokenKind.Bu:
                return new ThisExpressionSyntax(Eat());
            case TokenKind.Temel:
                return new BaseExpressionSyntax(Eat());
            case TokenKind.Caret:
                return new CurrentItemExpressionSyntax(Eat());
            case TokenKind.OpenParen:
                return IsLambda() ? ParseLambda() : ParseParenthesized();
            case TokenKind.OpenBracket:
                return ParseArray();
        }

        if (IsName(Current.Kind))
        {
            return new IdentifierExpressionSyntax(Eat());
        }

        Error(DiagnosticCodes.UnexpectedToken, "Beklenmeyen belirteç.", Current);
        return new IdentifierExpressionSyntax(SyntaxToken.Missing(TokenKind.Identifier, Current.Span.Start));
    }

    private ParenthesizedExpressionSyntax ParseParenthesized()
    {
        var open = Eat();
        var expression = ParseExpression();
        var close = Expect(TokenKind.CloseParen, DiagnosticCodes.UnexpectedToken, "`)` bekleniyor.");
        return new ParenthesizedExpressionSyntax(open, expression, close);
    }

    private LambdaExpressionSyntax ParseLambda()
    {
        var open = Eat();
        var parameters = ImmutableArray.CreateBuilder<ParameterSyntax>();
        while (Current.Kind is not TokenKind.CloseParen and not TokenKind.EndOfFile)
        {
            SyntaxToken? modifier = Current.Kind is TokenKind.Ref or TokenKind.Cikti ? Eat() : null;
            var name = ExpectName("Ad bekleniyor.");
            SyntaxToken colon;
            TypeSyntax type;
            if (Current.Kind == TokenKind.Colon)
            {
                colon = Eat();
                type = ParseType();
            }
            else
            {
                Error(DiagnosticCodes.UntypedLambdaParameter, "Lambda parametresinin türü yazılmalıdır.", name.Token);
                colon = SyntaxToken.Missing(TokenKind.Colon, name.Span.End);
                type = MissingType(name.Span.End);
            }

            var (equalsToken, value) = ParseDefaultValue();
            var comma = Current.Kind == TokenKind.Comma ? Eat() : null;
            parameters.Add(new ParameterSyntax(modifier, name, colon, type, equalsToken, value, comma));
            if (comma is null)
            {
                break;
            }
        }

        var close = Expect(TokenKind.CloseParen, DiagnosticCodes.UnexpectedToken, "`)` bekleniyor.");
        var arrow = Expect(TokenKind.EqualsGreater, DiagnosticCodes.UnexpectedToken, "`=>` bekleniyor.");
        SyntaxNode body = Current.Kind == TokenKind.OpenBrace ? ParseBlock() : ParseExpression();
        return new LambdaExpressionSyntax(open, parameters.ToImmutable(), ImmutableArray<SyntaxToken>.Empty, close, arrow, body);
    }

    private bool IsLambda()
    {
        if (Current.Kind != TokenKind.OpenParen)
        {
            return false;
        }

        var depth = 0;
        var seenColon = false;
        var parameterShape = true;
        for (var offset = 0; offset < 80 && _index + offset < _tokens.Length; offset++)
        {
            var kind = _tokens[_index + offset].Kind;
            if (kind == TokenKind.OpenParen)
            {
                depth++;
            }
            else if (kind == TokenKind.CloseParen)
            {
                depth--;
                if (depth == 0)
                {
                    var next = _index + offset + 1 < _tokens.Length
                        ? _tokens[_index + offset + 1].Kind
                        : TokenKind.EndOfFile;
                    return next == TokenKind.EqualsGreater && (seenColon || parameterShape);
                }
            }
            else if (depth == 1 && kind == TokenKind.Colon)
            {
                seenColon = true;
            }
            else if (depth == 1 && kind is not (TokenKind.Identifier or TokenKind.Comma or TokenKind.Ref or TokenKind.Cikti or TokenKind.Less or TokenKind.Greater or TokenKind.Question or TokenKind.Dot) && !IsName(kind))
            {
                parameterShape = false;
            }

            if (kind is TokenKind.EndOfFile)
            {
                break;
            }
        }

        return false;
    }

    private ArrayExpressionSyntax ParseArray()
    {
        var open = Eat();
        var elements = ImmutableArray.CreateBuilder<ExpressionSyntax>();
        var commas = ImmutableArray.CreateBuilder<SyntaxToken>();
        while (Current.Kind is not TokenKind.CloseBracket and not TokenKind.EndOfFile)
        {
            var start = _index;
            elements.Add(ParseExpression());
            if (Current.Kind == TokenKind.Comma)
            {
                commas.Add(Eat());
                if (Current.Kind == TokenKind.CloseBracket)
                {
                    break;
                }
            }
            else
            {
                break;
            }

            if (_index == start)
            {
                Eat();
                break;
            }
        }

        var close = Expect(TokenKind.CloseBracket, DiagnosticCodes.UnexpectedToken, "`]` bekleniyor.");
        return new ArrayExpressionSyntax(open, elements.ToImmutable(), commas.ToImmutable(), close);
    }

    private ObjectCreationExpressionSyntax ParseObjectCreation()
    {
        var at = Eat();
        var type = ParseType();
        ArgumentListSyntax? arguments = Current.Kind == TokenKind.OpenParen ? ParseArguments() : null;
        SyntaxToken? open = null;
        var initializers = ImmutableArray<InitializerSyntax>.Empty;
        SyntaxToken? close = null;
        if (Current.Kind == TokenKind.OpenBrace)
        {
            open = Eat();
            initializers = ParseInitializers();
            close = Expect(TokenKind.CloseBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        }

        return new ObjectCreationExpressionSyntax(at, type, arguments, open, initializers, close);
    }

    private CopyExpressionSyntax ParseCopy(ExpressionSyntax source)
    {
        var ile = Eat();
        var open = Eat();
        var initializers = ParseInitializers();
        var close = Expect(TokenKind.CloseBrace, DiagnosticCodes.UnclosedBlock, "`}` bekleniyor.");
        return new CopyExpressionSyntax(source, ile, open, initializers, close);
    }

    private ImmutableArray<InitializerSyntax> ParseInitializers()
    {
        var builder = ImmutableArray.CreateBuilder<InitializerSyntax>();
        while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
        {
            var start = _index;
            var name = ExpectName("Ad bekleniyor.");
            var equals = Expect(TokenKind.Equals, DiagnosticCodes.UnexpectedToken, "`=` bekleniyor.");
            var value = ParseExpression();
            builder.Add(new InitializerSyntax(name, equals, value));
            if (Current.Kind == TokenKind.Comma)
            {
                Eat();
            }

            if (_index == start)
            {
                if (Current.Kind is TokenKind.CloseBrace or TokenKind.EndOfFile)
                {
                    break;
                }

                Eat();
                break;
            }
        }

        return builder.ToImmutable();
    }

    private InterpolationExpressionSyntax ParseInterpolation()
    {
        var pieces = ImmutableArray.CreateBuilder<SyntaxNode>();
        pieces.Add(Eat());
        while (Current.Kind is not TokenKind.InterpolationEnd and not TokenKind.EndOfFile)
        {
            if (Current.Kind is TokenKind.StringText or TokenKind.InterpolationMiddle or TokenKind.InterpolationStart)
            {
                pieces.Add(Eat());
                continue;
            }

            var start = _index;
            var hole = ParseExpression();
            pieces.Add(new InterpolationHoleSyntax(hole));
            if (_index == start)
            {
                break;
            }
        }

        if (Current.Kind == TokenKind.InterpolationEnd)
        {
            pieces.Add(Eat());
        }
        else
        {
            Error(DiagnosticCodes.UnexpectedToken, "Metin sabiti kapanmadı.", Current);
        }

        return new InterpolationExpressionSyntax(pieces.ToImmutable());
    }

    private ArgumentListSyntax ParseArguments()
    {
        var open = Expect(TokenKind.OpenParen, DiagnosticCodes.UnexpectedToken, "`(` bekleniyor.");
        var arguments = ImmutableArray.CreateBuilder<ArgumentSyntax>();
        while (Current.Kind is not TokenKind.CloseParen and not TokenKind.EndOfFile)
        {
            var start = _index;
            SyntaxToken? modifier = Current.Kind is TokenKind.Ref or TokenKind.Cikti ? Eat() : null;
            var expression = ParseExpression();
            var comma = Current.Kind == TokenKind.Comma ? Eat() : null;
            arguments.Add(new ArgumentSyntax(modifier, expression, comma));
            if (comma is null || _index == start)
            {
                break;
            }
        }

        var close = Expect(TokenKind.CloseParen, DiagnosticCodes.UnexpectedToken, "`)` bekleniyor.");
        return new ArgumentListSyntax(open, arguments.ToImmutable(), close);
    }

    private TypeSyntax ParseType()
    {
        if (!IsName(Current.Kind))
        {
            Error(DiagnosticCodes.UnexpectedToken, "Tür bekleniyor.", Current);
            return MissingType(Current.Span.Start);
        }

        var name = Eat();
        TypeArgumentListSyntax? arguments = null;
        if (Current.Kind == TokenKind.Less)
        {
            arguments = ParseTypeArguments();
        }

        var nullables = ImmutableArray.CreateBuilder<SyntaxToken>();
        while (Current.Kind == TokenKind.Question)
        {
            nullables.Add(Eat());
        }

        return new TypeSyntax(name, arguments, nullables.ToImmutable());
    }

    private TypeArgumentListSyntax ParseTypeArguments()
    {
        var less = Eat();
        var arguments = ImmutableArray.CreateBuilder<TypeSyntax>();
        var commas = ImmutableArray.CreateBuilder<SyntaxToken>();
        while (Current.Kind is not TokenKind.Greater and not TokenKind.EndOfFile and not TokenKind.CloseBrace)
        {
            var start = _index;
            arguments.Add(ParseType());
            if (Current.Kind == TokenKind.Comma)
            {
                commas.Add(Eat());
            }
            else
            {
                break;
            }

            if (_index == start)
            {
                break;
            }
        }

        var greater = Expect(TokenKind.Greater, DiagnosticCodes.UnexpectedToken, "`>` bekleniyor.");
        return new TypeArgumentListSyntax(less, arguments.ToImmutable(), commas.ToImmutable(), greater);
    }

    private TypeParameterListSyntax ParseTypeParameters()
    {
        var less = Eat();
        var parameters = ImmutableArray.CreateBuilder<TypeParameterSyntax>();
        var commas = ImmutableArray.CreateBuilder<SyntaxToken>();
        while (Current.Kind is not TokenKind.Greater and not TokenKind.EndOfFile)
        {
            var name = ExpectName("Ad bekleniyor.");
            SyntaxToken? colon = null;
            var constraints = ImmutableArray.CreateBuilder<TypeSyntax>();
            var constraintCommas = ImmutableArray.CreateBuilder<SyntaxToken>();
            if (Current.Kind == TokenKind.Colon)
            {
                colon = Eat();
                constraints.Add(ParseType());
                while (Current.Kind == TokenKind.Comma && !NextIsTypeParameter())
                {
                    constraintCommas.Add(Eat());
                    constraints.Add(ParseType());
                }
            }

            parameters.Add(new TypeParameterSyntax(name, colon, constraints.ToImmutable(), constraintCommas.ToImmutable()));
            if (Current.Kind == TokenKind.Comma)
            {
                commas.Add(Eat());
                continue;
            }

            break;
        }

        var greater = Expect(TokenKind.Greater, DiagnosticCodes.UnexpectedToken, "`>` bekleniyor.");
        return new TypeParameterListSyntax(less, parameters.ToImmutable(), commas.ToImmutable(), greater);
    }

    private bool NextIsTypeParameter()
    {
        if (Peek(1) != TokenKind.Identifier && !IsName(Peek(1)))
        {
            return false;
        }

        return Peek(2) == TokenKind.Colon;
    }

    private bool LooksLikeGenericCall()
    {
        var depth = 0;
        for (var offset = 0; offset < 80 && _index + offset < _tokens.Length; offset++)
        {
            var kind = _tokens[_index + offset].Kind;
            if (kind == TokenKind.Less)
            {
                depth++;
            }
            else if (kind == TokenKind.Greater)
            {
                depth--;
                if (depth == 0)
                {
                    return PeekFrom(offset + 1) == TokenKind.OpenParen;
                }
            }
            else if (depth > 0 && kind is TokenKind.Semicolon or TokenKind.OpenBrace or TokenKind.EqualsGreater or TokenKind.EndOfFile)
            {
                return false;
            }
        }

        return false;
    }

    private TypeSyntax MissingType(int position) =>
        new(SyntaxToken.Missing(TokenKind.Identifier, position), null, ImmutableArray<SyntaxToken>.Empty);

    private bool StopExpression() =>
        HasLeadingNewline(Current) && (LooksLikeDeclaration(Current) || IsHardStatement(Current.Kind));

    private bool LooksLikeDeclaration(Token token)
    {
        if (!IsName(token.Kind) || token.Kind is TokenKind.Eger or TokenKind.Bekle)
        {
            return false;
        }

        var index = IndexOf(token);
        if (index < 0)
        {
            return false;
        }

        var next = index + 1 < _tokens.Length ? _tokens[index + 1].Kind : TokenKind.EndOfFile;
        return next is TokenKind.Colon or TokenKind.Comma;
    }

    private int IndexOf(Token token)
    {
        if (_index < _tokens.Length && _tokens[_index].Span.Start == token.Span.Start && _tokens[_index].Kind == token.Kind)
        {
            return _index;
        }

        return -1;
    }

    private static bool CanJuxtapose(ExpressionSyntax expression) =>
        expression is IdentifierExpressionSyntax or MemberAccessExpressionSyntax or CurrentItemExpressionSyntax;

    private static bool CanStartPrimary(TokenKind kind) => kind is
        TokenKind.Identifier or
        TokenKind.IntegerLiteral or
        TokenKind.RealLiteral or
        TokenKind.StringLiteral or
        TokenKind.CharacterLiteral or
        TokenKind.InterpolationStart or
        TokenKind.Dogru or
        TokenKind.Yanlis or
        TokenKind.Tilde or
        TokenKind.OpenParen or
        TokenKind.OpenBracket or
        TokenKind.At or
        TokenKind.Caret or
        TokenKind.Bu or
        TokenKind.Temel or
        TokenKind.Eger or
        TokenKind.Bekle;

    private static bool IsHardStatement(TokenKind kind) => kind is
        TokenKind.Don or
        TokenKind.Tekrarla or
        TokenKind.Yap or
        TokenKind.Sonsuzdon or
        TokenKind.Cik or
        TokenKind.Devam or
        TokenKind.Fonksiyon or
        TokenKind.Afonksiyon or
        TokenKind.Model or
        TokenKind.Sinif or
        TokenKind.Olustur or
        TokenKind.Arayuz or
        TokenKind.Sec or
        TokenKind.Secenek or
        TokenKind.Dene or
        TokenKind.Yakala or
        TokenKind.Sonunda or
        TokenKind.Firlat or
        TokenKind.Basinda or
        TokenKind.BasindaUnderscore or
        TokenKind.Olay or
        TokenKind.Eesislem or
        TokenKind.Dogrula or
        TokenKind.Donusum or
        TokenKind.Bagla or
        TokenKind.Servis or
        TokenKind.Kontrolcu or
        TokenKind.Rota or
        TokenKind.Test or
        TokenKind.Beklenen or
        TokenKind.Islem or
        TokenKind.Onayla or
        TokenKind.Gerial or
        TokenKind.Veritabani or
        TokenKind.Alan or
        TokenKind.Al or
        TokenKind.Edegilse or
        TokenKind.Degilse;

    private static bool IsModifier(TokenKind kind) => kind is
        TokenKind.Genel or
        TokenKind.Ozel or
        TokenKind.Korumali or
        TokenKind.Dahili or
        TokenKind.Soyut or
        TokenKind.Statik or
        TokenKind.Sanal or
        TokenKind.Yerinegec or
        TokenKind.Muhurlu;

    private static bool IsName(TokenKind kind) =>
        kind == TokenKind.Identifier || kind is >= TokenKind.Eger and <= TokenKind.Birlikte;

    private bool HasLeadingNewline(Token token)
    {
        foreach (var trivia in token.LeadingTrivia)
        {
            if (trivia.Kind == TriviaKind.NewLine)
            {
                return true;
            }
        }

        return false;
    }

    private SyntaxToken? TrySemicolon() => Current.Kind == TokenKind.Semicolon ? Eat() : null;

    private SyntaxToken ExpectName(string message)
    {
        if (IsName(Current.Kind))
        {
            return Eat();
        }

        Error(DiagnosticCodes.UnexpectedToken, message, Current);
        return SyntaxToken.Missing(TokenKind.Identifier, Current.Span.Start);
    }

    private SyntaxToken Expect(TokenKind kind, string code, string message)
    {
        if (Current.Kind == kind)
        {
            return Eat();
        }

        Error(code, message, Current);
        return SyntaxToken.Missing(kind, Current.Span.Start);
    }

    private SyntaxToken Eat()
    {
        var token = new SyntaxToken(Current, false);
        if (Current.Kind != TokenKind.EndOfFile && _index < _tokens.Length)
        {
            _index++;
        }

        return token;
    }

    private TokenKind Peek(int offset) => PeekFrom(offset);

    private TokenKind PeekFrom(int offset)
    {
        var index = _index + offset;
        return index >= 0 && index < _tokens.Length ? _tokens[index].Kind : TokenKind.EndOfFile;
    }

    private string TokenText(Token token) =>
        token.Span.Length == 0 ? string.Empty : _file.Text.ToString(token.Span);

    private void Error(string code, string message, Token token)
    {
        var span = token.Kind == TokenKind.EndOfFile ? new TextSpan(token.Span.Start, 0) : token.Span;
        if (span.Length == 0 && token.Kind != TokenKind.EndOfFile)
        {
            span = new TextSpan(token.Span.Start, Math.Max(token.Span.Length, 0));
        }

        var (line, column) = _file.Lines.GetLineAndColumn(span.Start);
        _diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Error, message, _file.Path, span, line, column, null));
    }
}
