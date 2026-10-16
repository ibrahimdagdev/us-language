using Us.Runtime;

namespace Us.Runtime.Tests;

public class PathEscapeTests
{
    [Fact]
    public void AbsoluteParentMixedAndEmptyRootAreRejected()
    {
        var files = new MemoryFileSystem();

        var absolute = Assert.Throws<UsRuntimeException>(() => files.Write("C:/gizli.txt", "x"));
        Assert.Equal(UsRuntimeCodes.PathEscape, absolute.Code);

        var parent = Assert.Throws<UsRuntimeException>(() => files.Write("../gizli.txt", "x"));
        Assert.Equal(UsRuntimeCodes.PathEscape, parent.Code);

        var mixed = Assert.Throws<UsRuntimeException>(() => files.Write("alt\\..\\..\\gizli.txt", "x"));
        Assert.Equal(UsRuntimeCodes.PathEscape, mixed.Code);

        var emptyRoot = Assert.Throws<UsRuntimeException>(() => new DiskFileSystem("   "));
        Assert.Equal(UsRuntimeCodes.PathEscape, emptyRoot.Code);
    }

    [Fact]
    public void SuccessfulWriteStaysUnderRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "us-path-" + Guid.NewGuid().ToString("N"));
        try
        {
            var files = new DiskFileSystem(root);
            files.Write("not.txt", "merhaba");
            Assert.True(File.Exists(Path.Combine(root, "not.txt")));
            Assert.Equal("merhaba", File.ReadAllText(Path.Combine(root, "not.txt")));
            Assert.Equal("merhaba", files.Read("not.txt"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
