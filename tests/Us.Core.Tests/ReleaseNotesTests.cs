using System.Text.RegularExpressions;

namespace Us.Core.Tests;

public class ReleaseNotesTests
{
    private static readonly Regex StatusLine = new(@"^- Status:\s*(?<status>\S+)", RegexOptions.Compiled | RegexOptions.Multiline);

    [Fact]
    public void ReleaseNotesListBlockedTaskStatuses()
    {
        var root = FindRoot();
        var notesPath = Path.Combine(root, "docs", "release-notes", "v0.1-rc.md");
        Assert.True(File.Exists(notesPath), notesPath);
        var notes = File.ReadAllText(notesPath);

        Assert.Contains("## Tamamlanan taşlar", notes, StringComparison.Ordinal);
        Assert.Contains("## Görev durumu: BLOCKED", notes, StringComparison.Ordinal);
        Assert.Contains("## Çalışmayan dış entegrasyonlar", notes, StringComparison.Ordinal);
        Assert.Contains("## Derleyici sözleşmesi", notes, StringComparison.Ordinal);
        Assert.Contains("compiler-parity.md", notes, StringComparison.Ordinal);

        var blocked = new List<string>();
        var tasksDir = Path.Combine(root, "tasks");
        if (Directory.Exists(tasksDir))
        {
            foreach (var path in Directory.EnumerateFiles(tasksDir, "TASK-*.md"))
            {
                var text = File.ReadAllText(path);
                var status = StatusLine.Match(text);
                if (!status.Success)
                {
                    continue;
                }

                var value = status.Groups["status"].Value;
                if (value is "BLOCKED" or "BLOCKED_EXTERNAL_INTEGRATION" or "BLOCKED_DESIGN_DECISION")
                {
                    var id = Path.GetFileNameWithoutExtension(path);
                    blocked.Add(id);
                    Assert.Contains(id, notes, StringComparison.Ordinal);
                    Assert.Contains(value, notes, StringComparison.Ordinal);
                }
            }
        }

        if (blocked.Count == 0)
        {
            Assert.Contains("olan görev **yoktur**", notes, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReleaseNotesDoNotInventForeignSyntax()
    {
        var notes = File.ReadAllText(Path.Combine(FindRoot(), "docs", "release-notes", "v0.1-rc.md"));
        Assert.DoesNotContain("async function", notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class MyClass", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("public static void Main", notes, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Us.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
