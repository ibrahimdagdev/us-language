using Us.Lexer;
using Us.Parser;

namespace Us.Parser.Tests;

public class PrecedenceTests
{
    [Fact]
    public void MultiplicationBindsTighterThanAddition()
    {
        var binary = ExpressionOf("1 + 2 * 3");
        var add = Assert.IsType<BinaryExpressionSyntax>(binary);
        Assert.Equal(TokenKind.Plus, add.Operator.TokenKind);
        var multiply = Assert.IsType<BinaryExpressionSyntax>(add.Right);
        Assert.Equal(TokenKind.Star, multiply.Operator.TokenKind);
    }

    [Fact]
    public void ParenthesesOverridePrecedence()
    {
        var binary = Assert.IsType<BinaryExpressionSyntax>(ExpressionOf("(a + b) * c"));
        Assert.Equal(TokenKind.Star, binary.Operator.TokenKind);
        Assert.IsType<ParenthesizedExpressionSyntax>(binary.Left);
    }

    [Fact]
    public void AndBindsTighterThanOr()
    {
        var or = Assert.IsType<BinaryExpressionSyntax>(ExpressionOf("a || b && c"));
        Assert.Equal(TokenKind.PipePipe, or.Operator.TokenKind);
        var and = Assert.IsType<BinaryExpressionSyntax>(or.Right);
        Assert.Equal(TokenKind.AmpersandAmpersand, and.Operator.TokenKind);
    }

    [Fact]
    public void EqualityIsLooserThanComparison()
    {
        var equality = Assert.IsType<BinaryExpressionSyntax>(ExpressionOf("a < b == c"));
        Assert.Equal(TokenKind.EqualEqual, equality.Operator.TokenKind);
        var comparison = Assert.IsType<BinaryExpressionSyntax>(equality.Left);
        Assert.Equal(TokenKind.Less, comparison.Operator.TokenKind);
    }

    [Fact]
    public void UnaryBindsTighterThanAnd()
    {
        var and = Assert.IsType<BinaryExpressionSyntax>(ExpressionOf("!a && b"));
        Assert.IsType<UnaryExpressionSyntax>(and.Left);
    }

    [Fact]
    public void AssignmentIsRightAssociative()
    {
        var assignment = Assert.IsType<AssignmentExpressionSyntax>(ExpressionOf("a = b = c"));
        Assert.IsType<AssignmentExpressionSyntax>(assignment.Right);
    }

    [Fact]
    public void NullCoalesceIsLooserThanConditionalAccess()
    {
        var coalesce = Assert.IsType<BinaryExpressionSyntax>(ExpressionOf("kullanıcı?.ad ?? \"Misafir\""));
        Assert.Equal(TokenKind.QuestionQuestion, coalesce.Operator.TokenKind);
        var member = Assert.IsType<MemberAccessExpressionSyntax>(coalesce.Left);
        Assert.Equal(TokenKind.QuestionDot, member.Operator.TokenKind);
    }

    [Fact]
    public void PrecedenceTableOrdersTheOperators()
    {
        Assert.True(OperatorPrecedence.Infix(TokenKind.Star) > OperatorPrecedence.Infix(TokenKind.Plus));
        Assert.True(OperatorPrecedence.Infix(TokenKind.AmpersandAmpersand) > OperatorPrecedence.Infix(TokenKind.PipePipe));
        Assert.True(OperatorPrecedence.Infix(TokenKind.Less) > OperatorPrecedence.Infix(TokenKind.EqualEqual));
        Assert.True(OperatorPrecedence.IsAssignment(TokenKind.Equals));
    }

    private static ExpressionSyntax ExpressionOf(string text)
    {
        var result = Parsed.Clean(text);
        var statement = Assert.IsType<ExpressionStatementSyntax>(Assert.Single(result.Root.Members));
        return statement.Expression;
    }
}
