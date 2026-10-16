using System.Text;
using Us.Compiler;
using Us.Core;
using Us.Semantics;

namespace Us.Compiler.Tests;

public class BackendContractTests
{
    [Fact]
    public void NoopBackendSucceeds()
    {
        var compilation = Compiler.BindOnly([SourceFile.FromText("a.us", "syaz \"x\"\n")]);
        var result = new NoopBackend().Emit(compilation);
        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void CompilerAcceptsBackendParameterWithoutCliBinding()
    {
        var file = SourceFile.FromText("a.us", "syaz \"Merhaba US!\"\n");
        var result = Compiler.Compile([file], new NoopBackend(), Path.Combine(Path.GetTempPath(), "us-noop-" + Guid.NewGuid().ToString("N")));
        Assert.True(result.Success);
    }
}

public class BoundWalkTests
{
    [Fact]
    public void MerhabaWalkHasNoUnresolvedBound()
    {
        var text = """
            ad: metin = "İbrahim"
            syaz "Merhaba {ad}"
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        var compilation = Compiler.BindOnly([SourceFile.FromText("merhaba.us", text)]);
        var walk = BoundTreeWalker.Walk(compilation, "merhaba.us");
        Assert.DoesNotContain(walk.Diagnostics, item => item.Code == DiagnosticCodes.UnresolvedBound);
        Assert.DoesNotContain(walk.Diagnostics, item => item.Code == DiagnosticCodes.AmbiguousConstructor);
        Assert.True(walk.NodeCount > 0);
    }
}

public class BackendDecisionTests
{
    [Fact]
    public void CompilerProjectHasNoRoslynPackage()
    {
        var root = M15Helpers.FindRepo();
        var compilerCsproj = File.ReadAllText(Path.Combine(root, "src", "Us.Compiler", "Us.Compiler.csproj"));
        Assert.DoesNotContain("Microsoft.CodeAnalysis", compilerCsproj, StringComparison.Ordinal);
        Assert.DoesNotContain("Roslyn", compilerCsproj, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DotNetEmitBackend", File.ReadAllText(Path.Combine(root, "docs", "compiler-parity.md")), StringComparison.Ordinal);
    }
}

public class EmitHelloTests
{
    [Fact]
    public void EmittedMerhabaWritesExpectedLineInSeparateProcess()
    {
        var text = """
            ad: metin = "İbrahim"
            syaz "Merhaba {ad}"
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        var file = SourceFile.FromText("merhaba.us", text);
        var bound = Compiler.BindOnly([file]);
        Assert.False(bound.Diagnostics.HasErrors);

        var output = Path.Combine(Path.GetTempPath(), "us-hello-" + Guid.NewGuid().ToString("N"));
        var emit = new DotNetEmitBackend().Emit(bound, output);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(item => item.Message)));
        Assert.NotNull(emit.ArtifactPath);
        Assert.True(File.Exists(emit.ArtifactPath));

        var ilBytes = File.ReadAllBytes(emit.ArtifactPath);
        Assert.True(ilBytes.Length > 64);
        var ascii = Encoding.ASCII.GetString(ilBytes);
        Assert.DoesNotContain("ProgramRunner", ascii, StringComparison.Ordinal);
        Assert.DoesNotContain("Evaluator", ascii, StringComparison.Ordinal);

        var process = EmittedProcess.Run(emit.ArtifactPath);
        Assert.Equal(0, process.ExitCode);
        Assert.Equal("Merhaba İbrahim\n", process.StandardOutput.EndsWith('\n') ? process.StandardOutput : process.StandardOutput + "\n");
        Assert.True(string.IsNullOrWhiteSpace(process.StandardError));
    }

    [Fact]
    public void EmittedSyazMerhabaUsRunsWithoutInterpreter()
    {
        var file = SourceFile.FromText("x.us", "syaz \"Merhaba US!\"\n");
        var bound = Compiler.BindOnly([file]);
        var output = Path.Combine(Path.GetTempPath(), "us-simple-" + Guid.NewGuid().ToString("N"));
        var emit = new DotNetEmitBackend().Emit(bound, output);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(item => item.Message)));
        var process = EmittedProcess.Run(emit.ArtifactPath!);
        Assert.Equal(0, process.ExitCode);
        Assert.Contains("Merhaba US!", process.StandardOutput, StringComparison.Ordinal);
    }
}

public class LayerBoundaryTests
{
    [Fact]
    public void CompilerSourcesDoNotContainLexerWriteAndCliAvoidsBinderEvaluator()
    {
        var root = M15Helpers.FindRepo();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src", "Us.Compiler"), "*.cs"))
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("Us.Lexer.Lexer", text, StringComparison.Ordinal);
            Assert.DoesNotContain("new Lexer", text, StringComparison.Ordinal);
        }

        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src", "Us.Cli"), "*.cs"))
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("Binder", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Evaluator", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Us.Semantics", text, StringComparison.Ordinal);
        }
    }
}

internal static class M15Helpers
{
    public static string FindRepo()
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
