using Us.Runtime;

namespace Us.Runtime.Tests;

public class MemoryFileSystemTests
{
    [Fact]
    public void WriteThenReadKeepsTurkishText()
    {
        var files = new MemoryFileSystem();
        files.Write("not.txt", "İbrahim");
        Assert.Equal("İbrahim", files.Read("not.txt"));
        Assert.True(files.Exists("not.txt"));
        Assert.False(files.Exists("yok.txt"));
    }

    [Fact]
    public void AppendAndDeleteFollowTheFileRules()
    {
        var files = new MemoryFileSystem();
        files.Append("not.txt", "A");
        files.Append("not.txt", "B");
        Assert.Equal("AB", files.Read("not.txt"));
        files.Delete("not.txt");
        Assert.False(files.Exists("not.txt"));
        var missing = Assert.Throws<UsRuntimeException>(() => files.Delete("not.txt"));
        Assert.Equal(UsRuntimeCodes.FileNotFound, missing.Code);
    }

    [Fact]
    public void DirectoryListIsOrdinal()
    {
        var files = new MemoryFileSystem();
        files.CreateDirectory("kutu");
        files.Write("kutu/b.txt", "2");
        files.Write("kutu/a.txt", "1");
        Assert.Equal(["a.txt", "b.txt"], files.ListFiles("kutu"));
        Assert.True(files.DirectoryExists("kutu"));
    }
}

public class PathJailTests
{
    [Fact]
    public void ParentSegmentCannotLeaveTheRoot()
    {
        var files = new MemoryFileSystem();
        var escaped = Assert.Throws<UsRuntimeException>(() => files.Combine("..", "gizli"));
        Assert.Equal(UsRuntimeCodes.PathEscape, escaped.Code);
        Assert.Equal("a/b", files.Combine("a", "b"));
    }

    [Fact]
    public void SymbolicLinkIsRejected()
    {
        var files = new MemoryFileSystem();
        files.AddLink("bağ");
        var link = Assert.Throws<UsRuntimeException>(() => files.Write("bağ/not.txt", "x"));
        Assert.Equal(UsRuntimeCodes.SymbolicLink, link.Code);
    }
}
