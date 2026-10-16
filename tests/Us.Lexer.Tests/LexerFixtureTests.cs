using Us.Lexer;

namespace Us.Lexer.Tests;

public class LexerFixtureTests
{
    [Fact]
    public void FixturesMatchKindLists()
    {
        var root = FindFixtures();
        var sources = Directory.GetFiles(root, "*.us");
        Assert.NotEmpty(sources);
        foreach (var sourcePath in sources)
        {
            var expectedPath = Path.ChangeExtension(sourcePath, ".tokens");
            var source = File.ReadAllText(sourcePath);
            var expected = File.ReadAllLines(expectedPath)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Select(Enum.Parse<TokenKind>)
                .ToArray();
            var actual = Lexer.Tokenize(Us.Core.SourceFile.FromText(sourcePath, source)).Tokens
                .Select(token => token.Kind)
                .ToArray();
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void CaseDifferenceSurvivesAFixture()
    {
        var lexed = Lexed.Of("Kullanıcı kullanıcı");
        Assert.Equal("Kullanıcı", lexed.TextOf(lexed.Significant()[0]));
        Assert.Equal("kullanıcı", lexed.TextOf(lexed.Significant()[1]));
        Assert.NotEqual(lexed.TextOf(lexed.Significant()[0]), lexed.TextOf(lexed.Significant()[1]));
    }

    private static string FindFixtures()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Us.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "tests", "Us.Lexer.Tests", "fixtures");
    }
}
