using Us.Interpreter;

namespace Us.Conformance.Tests;

public class NativeTestConformanceTests
{
    [Fact]
    public async Task FixturesMatchStructuredResults()
    {
        var root = Path.Combine(FindRoot(), "tests", "conformance", "native");
        var pass = await Run(root, "test-pass.us");
        Assert.Equal(1, pass.Passed);

        var fail = await Run(root, "test-fail.us");
        Assert.Equal(TestStatus.Failed, Assert.Single(fail.Tests).Status);
        Assert.Equal(TestFailureKind.AssertionFailed, fail.Tests[0].Failure!.Kind);

        var error = await Run(root, "test-error.us");
        Assert.Equal(TestStatus.Error, Assert.Single(error.Tests).Status);
        Assert.Equal(TestFailureKind.UnexpectedError, error.Tests[0].Failure!.Kind);

        var expected = await Run(root, "expected-error.us");
        Assert.Equal(1, expected.Passed);

        var missing = await Run(root, "expected-error-fail.us");
        Assert.Equal(TestFailureKind.ExpectedErrorMissing, Assert.Single(missing.Tests).Failure!.Kind);

        var asyncTest = await Run(root, "async-test.us");
        Assert.Equal(1, asyncTest.Passed);

        var isolated = await Run(root, "test-isolation.us");
        Assert.Equal(2, isolated.Passed);
        Assert.Equal(0, isolated.Failed);
    }

    private static ValueTask<TestRunResult> Run(string root, string name) =>
        TestRunner.RunTextAsync(Path.Combine(root, name), File.ReadAllText(Path.Combine(root, name)));

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
