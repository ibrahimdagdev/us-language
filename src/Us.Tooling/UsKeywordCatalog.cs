using Us.Lexer;

namespace Us.Tooling;

/// <summary>
/// Editor-facing keyword surface sourced from <see cref="Keywords"/> (single language definition).
/// </summary>
public static class UsKeywordCatalog
{
    public static IReadOnlyList<string> All { get; } = Keywords.All.Select(item => item.Text).ToArray();
}
