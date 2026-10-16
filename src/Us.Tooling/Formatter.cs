using System.Text;
using Us.Core;
using Us.Lexer;
using Us.Parser;

namespace Us.Tooling;

public static class Formatter
{
    public static string Format(string source, string path = "format.us")
    {
        ArgumentNullException.ThrowIfNull(source);
        source = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var file = SourceFile.FromText(path, source);
        var parsed = global::Us.Parser.Parser.Parse(file);
        var writer = new FormatWriter(file.Text);
        writer.WriteCompilationUnit(parsed.Root);
        var result = writer.ToString();
        if (result.Length > 0 && !result.EndsWith('\n'))
        {
            result += "\n";
        }

        return result;
    }
}

internal sealed class FormatWriter
{
    private readonly SourceText _text;
    private readonly StringBuilder _builder = new();
    private int _indent;
    private bool _atLineStart = true;

    public FormatWriter(SourceText text) => _text = text;

    public override string ToString() => _builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);

    public void WriteCompilationUnit(CompilationUnitSyntax unit)
    {
        for (var i = 0; i < unit.Members.Length; i++)
        {
            if (i > 0)
            {
                EnsureSingleTrailingNewline();
            }

            WriteMember(unit.Members[i]);
        }
    }

    private void EnsureSingleTrailingNewline()
    {
        while (_builder.Length > 0 && _builder[^1] == '\n')
        {
            _builder.Length--;
        }

        _builder.Append('\n');
        _atLineStart = true;
    }

    private void WriteMember(SyntaxNode node)
    {
        switch (node)
        {
            case IfStatementSyntax ifStatement:
                WriteIf(ifStatement);
                break;
            case BlockSyntax block:
                WriteBlock(block);
                break;
            case FunctionDeclarationSyntax function:
                WriteFunction(function);
                break;
            case ModelDeclarationSyntax model:
                WriteModel(model);
                break;
            case ClassDeclarationSyntax cls:
                WriteClass(cls);
                break;
            case VariableDeclarationSyntax variable:
                WriteVariable(variable);
                NewLine();
                break;
            case ExpressionStatementSyntax expression:
                WriteLeadingComments(expression);
                WriteExpression(expression.Expression);
                NewLine();
                break;
            case WhileStatementSyntax whileStatement:
                WriteToken(whileStatement.Keyword);
                Space();
                WriteExpression(whileStatement.Condition);
                NewLine();
                WriteStatementBody(whileStatement.Body);
                break;
            case DoWhileStatementSyntax doWhile:
                WriteToken(doWhile.Yap);
                NewLine();
                WriteStatementBody(doWhile.Body);
                WriteToken(doWhile.Tekrarla);
                Space();
                WriteExpression(doWhile.Condition);
                NewLine();
                break;
            case RangeForStatementSyntax range:
                WriteRangeFor(range);
                break;
            case ForEachStatementSyntax each:
                WriteForEach(each);
                break;
            case InfiniteLoopStatementSyntax infinite:
                WriteToken(infinite.Keyword);
                NewLine();
                WriteStatementBody(infinite.Body);
                break;
            case TryStatementSyntax tryStatement:
                WriteTry(tryStatement);
                break;
            case ResultStatementSyntax result:
                WriteToken(result.Arrow);
                Space();
                WriteExpression(result.Expression);
                NewLine();
                break;
            case BreakStatementSyntax breakStatement:
                WriteToken(breakStatement.Keyword);
                NewLine();
                break;
            case ContinueStatementSyntax continueStatement:
                WriteToken(continueStatement.Keyword);
                NewLine();
                break;
            case ThrowStatementSyntax throwStatement:
                WriteToken(throwStatement.Keyword);
                Space();
                WriteExpression(throwStatement.Expression);
                NewLine();
                break;
            default:
                WriteFallback(node);
                NewLine();
                break;
        }
    }

    private void WriteStatementBody(StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
        {
            WriteBlock(block);
            return;
        }

        _indent++;
        WriteMember(statement);
        _indent--;
    }

    private void WriteIf(IfStatementSyntax node)
    {
        WriteLeadingComments(node.Eger);
        WriteToken(node.Eger);
        Space();
        WriteExpression(node.Condition);
        NewLine();
        WriteStatementBody(node.Body);
        foreach (var clause in node.ElseIfs)
        {
            WriteToken(clause.Keyword);
            Space();
            WriteExpression(clause.Condition);
            NewLine();
            WriteStatementBody(clause.Body);
        }

        if (node.Degilse is not null && node.ElseBody is not null)
        {
            WriteToken(node.Degilse);
            NewLine();
            WriteStatementBody(node.ElseBody);
        }
    }

    private void WriteBlock(BlockSyntax block)
    {
        WriteLeadingComments(block.OpenBrace);
        WriteToken(block.OpenBrace);
        NewLine();
        _indent++;
        foreach (var statement in block.Statements)
        {
            WriteMember(statement);
        }

        _indent--;
        WriteToken(block.CloseBrace);
        NewLine();
    }

    private void WriteFunction(FunctionDeclarationSyntax function)
    {
        foreach (var modifier in function.Modifiers)
        {
            WriteToken(modifier);
            Space();
        }

        WriteToken(function.Keyword);
        Space();
        WriteToken(function.Name);
        if (function.TypeParameters is not null)
        {
            WriteTypeParameterList(function.TypeParameters);
        }

        WriteToken(function.OpenParen);
        for (var i = 0; i < function.Parameters.Length; i++)
        {
            if (i > 0)
            {
                Write(",");
                Space();
            }

            WriteParameter(function.Parameters[i]);
        }

        WriteToken(function.CloseParen);
        if (function.Colon is not null && function.ReturnType is not null)
        {
            Write(":");
            Space();
            WriteType(function.ReturnType);
        }

        if (function.Arrow is not null && function.Body is ExpressionSyntax expressionBody)
        {
            Space();
            WriteToken(function.Arrow);
            Space();
            WriteExpression(expressionBody);
            NewLine();
            return;
        }

        NewLine();
        if (function.Body is StatementSyntax statementBody)
        {
            WriteStatementBody(statementBody);
        }
        else if (function.Body is not null)
        {
            WriteFallback(function.Body);
            NewLine();
        }
    }

    private void WriteParameter(ParameterSyntax parameter)
    {
        if (parameter.Modifier is not null)
        {
            WriteToken(parameter.Modifier);
            Space();
        }

        WriteToken(parameter.Name);
        Write(":");
        Space();
        WriteType(parameter.Type);
        if (parameter.EqualsToken is not null && parameter.DefaultValue is not null)
        {
            Space();
            WriteToken(parameter.EqualsToken);
            Space();
            WriteExpression(parameter.DefaultValue);
        }
    }

    private void WriteModel(ModelDeclarationSyntax model)
    {
        WriteToken(model.Keyword);
        Space();
        WriteToken(model.Name);
        if (model.TypeParameters is not null)
        {
            WriteTypeParameterList(model.TypeParameters);
        }

        NewLine();
        WriteToken(model.OpenBrace);
        NewLine();
        _indent++;
        foreach (var field in model.Fields)
        {
            WriteModelField(field);
            NewLine();
        }

        _indent--;
        WriteToken(model.CloseBrace);
        NewLine();
    }

    private void WriteModelField(ModelFieldSyntax field)
    {
        foreach (var modifier in field.Modifiers)
        {
            WriteToken(modifier);
            Space();
        }

        for (var i = 0; i < field.Names.Length; i++)
        {
            if (i > 0)
            {
                Write(",");
                Space();
            }

            WriteToken(field.Names[i]);
        }

        Write(":");
        Space();
        WriteType(field.Type);
    }

    private void WriteClass(ClassDeclarationSyntax cls)
    {
        foreach (var modifier in cls.Modifiers)
        {
            WriteToken(modifier);
            Space();
        }

        WriteToken(cls.Keyword);
        Space();
        WriteToken(cls.Name);
        NewLine();
        WriteToken(cls.OpenBrace);
        NewLine();
        _indent++;
        foreach (var member in cls.Members)
        {
            WriteMember(member);
        }

        _indent--;
        WriteToken(cls.CloseBrace);
        NewLine();
    }

    private void WriteVariable(VariableDeclarationSyntax variable)
    {
        WriteLeadingComments(variable);
        for (var i = 0; i < variable.Names.Length; i++)
        {
            if (i > 0)
            {
                Write(",");
                Space();
            }

            WriteToken(variable.Names[i]);
        }

        Write(":");
        Space();
        WriteType(variable.Type);
        if (variable.EqualsToken is not null)
        {
            Space();
            WriteToken(variable.EqualsToken);
            Space();
            for (var i = 0; i < variable.Initializers.Length; i++)
            {
                if (i > 0)
                {
                    Write(",");
                    Space();
                }

                WriteExpression(variable.Initializers[i]);
            }
        }
    }

    private void WriteRangeFor(RangeForStatementSyntax range)
    {
        WriteToken(range.Don);
        Space();
        WriteExpression(range.Start);
        Space();
        WriteToken(range.Ile);
        Space();
        WriteExpression(range.End);
        if (range.Adim is not null && range.Step is not null)
        {
            Space();
            WriteToken(range.Adim);
            Space();
            WriteExpression(range.Step);
        }

        Space();
        WriteToken(range.Arrow);
        Space();
        WriteToken(range.Name);
        NewLine();
        WriteStatementBody(range.Body);
    }

    private void WriteForEach(ForEachStatementSyntax each)
    {
        WriteToken(each.Don);
        Space();
        WriteExpression(each.Collection);
        Space();
        WriteToken(each.Arrow);
        Space();
        WriteToken(each.Name);
        NewLine();
        WriteStatementBody(each.Body);
    }

    private void WriteTry(TryStatementSyntax node)
    {
        if (node.Dene is not null)
        {
            WriteToken(node.Dene);
            NewLine();
        }

        if (node.Body is not null)
        {
            WriteBlock(node.Body);
        }

        foreach (var clause in node.Catches)
        {
            WriteToken(clause.Keyword);
            Space();
            WriteToken(clause.Name);
            if (clause.Colon is not null && clause.Type is not null)
            {
                Write(":");
                Space();
                WriteType(clause.Type);
            }

            NewLine();
            WriteBlock(clause.Body);
        }

        if (node.Sonunda is not null && node.Finally is not null)
        {
            WriteToken(node.Sonunda);
            NewLine();
            WriteBlock(node.Finally);
        }
    }

    private void WriteType(TypeSyntax type) => WriteFallback(type);

    private void WriteTypeParameterList(TypeParameterListSyntax list)
    {
        WriteToken(list.Less);
        for (var i = 0; i < list.Parameters.Length; i++)
        {
            if (i > 0)
            {
                Write(",");
                Space();
            }

            WriteFallback(list.Parameters[i]);
        }

        WriteToken(list.Greater);
    }

    private void WriteExpression(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case BinaryExpressionSyntax binary:
                WriteExpression(binary.Left);
                Space();
                WriteToken(binary.Operator);
                Space();
                WriteExpression(binary.Right);
                break;
            case UnaryExpressionSyntax unary when unary.IsPostfix:
                WriteExpression(unary.Operand);
                WriteToken(unary.Operator);
                break;
            case UnaryExpressionSyntax unary:
                WriteToken(unary.Operator);
                WriteExpression(unary.Operand);
                break;
            case AssignmentExpressionSyntax assignment:
                WriteExpression(assignment.Left);
                Space();
                WriteToken(assignment.Operator);
                Space();
                WriteExpression(assignment.Right);
                break;
            case MemberAccessExpressionSyntax member:
                WriteExpression(member.Expression);
                WriteToken(member.Operator);
                WriteToken(member.Name);
                break;
            case InvocationExpressionSyntax call:
                WriteExpression(call.Expression);
                if (call.TypeArguments is not null)
                {
                    WriteFallback(call.TypeArguments);
                }

                WriteArgumentList(call.Arguments);
                break;
            case ParenthesizedExpressionSyntax paren:
                WriteToken(paren.OpenParen);
                WriteExpression(paren.Expression);
                WriteToken(paren.CloseParen);
                break;
            case IndexExpressionSyntax index:
                WriteExpression(index.Expression);
                WriteToken(index.OpenBracket);
                WriteExpression(index.Index);
                WriteToken(index.CloseBracket);
                break;
            case ArrayExpressionSyntax array:
                WriteToken(array.OpenBracket);
                for (var i = 0; i < array.Elements.Length; i++)
                {
                    if (i > 0)
                    {
                        Write(",");
                        Space();
                    }

                    WriteExpression(array.Elements[i]);
                }

                WriteToken(array.CloseBracket);
                break;
            case InterpolationExpressionSyntax interpolation:
                WriteInterpolation(interpolation);
                break;
            case ObjectCreationExpressionSyntax creation:
                WriteObjectCreation(creation);
                break;
            case ExpressionIfSyntax conditional:
                WriteToken(conditional.Eger);
                Space();
                WriteExpression(conditional.Condition);
                Space();
                WriteToken(conditional.Arrow);
                Space();
                WriteExpression(conditional.WhenTrue);
                Space();
                WriteToken(conditional.FalseArrow);
                Space();
                WriteExpression(conditional.WhenFalse);
                break;
            default:
                WriteFallback(expression);
                break;
        }
    }

    private void WriteArgumentList(ArgumentListSyntax list)
    {
        // Bare calls like `syaz "x"` parse with missing parentheses; keep that surface form.
        var parenStyle = !list.OpenParen.IsMissing;
        if (parenStyle)
        {
            WriteToken(list.OpenParen);
        }
        else if (list.Arguments.Length > 0)
        {
            Space();
        }

        for (var i = 0; i < list.Arguments.Length; i++)
        {
            if (i > 0)
            {
                if (parenStyle)
                {
                    Write(",");
                    Space();
                }
                else
                {
                    Space();
                }
            }

            var argument = list.Arguments[i];
            if (argument.Modifier is not null)
            {
                WriteToken(argument.Modifier);
                Space();
            }

            WriteExpression(argument.Expression);
        }

        if (parenStyle)
        {
            WriteToken(list.CloseParen);
        }
    }

    private void WriteInterpolation(InterpolationExpressionSyntax interpolation)
    {
        foreach (var child in interpolation.Children)
        {
            switch (child)
            {
                case SyntaxToken token:
                    WriteToken(token);
                    break;
                case InterpolationHoleSyntax hole:
                    WriteExpression(hole.Expression);
                    break;
                case ExpressionSyntax expression:
                    WriteExpression(expression);
                    break;
            }
        }
    }

    private void WriteObjectCreation(ObjectCreationExpressionSyntax creation)
    {
        WriteToken(creation.At);
        WriteType(creation.Type);
        if (creation.Arguments is not null)
        {
            WriteArgumentList(creation.Arguments);
        }

        if (creation.OpenBrace is not null)
        {
            NewLine();
            WriteToken(creation.OpenBrace);
            NewLine();
            _indent++;
            foreach (var initializer in creation.Initializers)
            {
                WriteFallback(initializer);
                NewLine();
            }

            _indent--;
            if (creation.CloseBrace is not null)
            {
                WriteToken(creation.CloseBrace);
            }
        }
    }

    private void WriteFallback(SyntaxNode node)
    {
        var tokens = new List<SyntaxToken>();
        CollectTokens(node, tokens);
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.IsMissing || token.TokenKind == TokenKind.EndOfFile)
            {
                continue;
            }

            WriteLeadingComments(token);
            if (i > 0 && NeedsSpace(tokens[i - 1].TokenKind, token.TokenKind))
            {
                Space();
            }

            WriteTokenText(token);
        }
    }

    private static void CollectTokens(SyntaxNode node, List<SyntaxToken> tokens)
    {
        if (node is SyntaxToken token)
        {
            tokens.Add(token);
            return;
        }

        foreach (var child in node.Children)
        {
            CollectTokens(child, tokens);
        }
    }

    private static bool NeedsSpace(TokenKind previous, TokenKind next)
    {
        if (previous is TokenKind.OpenParen or TokenKind.OpenBracket or TokenKind.Dot or TokenKind.QuestionDot or TokenKind.At)
        {
            return false;
        }

        if (next is TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.Comma or TokenKind.Semicolon or TokenKind.Dot or TokenKind.QuestionDot)
        {
            return false;
        }

        if (previous is TokenKind.PlusPlus or TokenKind.MinusMinus)
        {
            return false;
        }

        if (next is TokenKind.PlusPlus or TokenKind.MinusMinus)
        {
            return false;
        }

        if (previous == TokenKind.Bang)
        {
            return false;
        }

        return true;
    }

    private void WriteLeadingComments(SyntaxNode node)
    {
        foreach (var token in FlattenTokens(node))
        {
            WriteLeadingComments(token);
            break;
        }
    }

    private void WriteLeadingComments(SyntaxToken token)
    {
        if (token.Token.LeadingTrivia.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var trivia in token.Token.LeadingTrivia)
        {
            if (trivia.Kind is TriviaKind.LineComment or TriviaKind.BlockComment)
            {
                WriteIndent();
                _builder.Append(_text.ToString(trivia.Span));
                NewLine();
            }
        }
    }

    private static IEnumerable<SyntaxToken> FlattenTokens(SyntaxNode node)
    {
        if (node is SyntaxToken token)
        {
            yield return token;
            yield break;
        }

        foreach (var child in node.Children)
        {
            foreach (var nested in FlattenTokens(child))
            {
                yield return nested;
            }
        }
    }

    private void WriteToken(SyntaxToken token)
    {
        if (token.IsMissing)
        {
            return;
        }

        WriteLeadingComments(token);
        WriteTokenText(token);
    }

    private void WriteTokenText(SyntaxToken token)
    {
        WriteIndent();
        if (token.Span.Length > 0)
        {
            _builder.Append(_text.ToString(token.Span));
        }

        _atLineStart = false;
    }

    private void Write(string text)
    {
        WriteIndent();
        _builder.Append(text);
        _atLineStart = false;
    }

    private void Space()
    {
        if (_atLineStart || _builder.Length == 0)
        {
            return;
        }

        if (_builder[^1] is ' ' or '\n')
        {
            return;
        }

        _builder.Append(' ');
    }

    private void NewLine()
    {
        if (_builder.Length > 0 && _builder[^1] != '\n')
        {
            _builder.Append('\n');
        }

        _atLineStart = true;
    }

    private void WriteIndent()
    {
        if (!_atLineStart)
        {
            return;
        }

        _builder.Append(' ', _indent * 4);
        _atLineStart = false;
    }
}
