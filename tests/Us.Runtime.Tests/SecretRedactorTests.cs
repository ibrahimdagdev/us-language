using Us.Runtime;

namespace Us.Runtime.Tests;

public class SecretRedactorTests
{
    [Fact]
    public void NamedValuesAndAuthorizationAreMasked()
    {
        Assert.Equal(SecretRedactor.Mask, SecretRedactor.RedactNamed("parola", "gizli"));
        Assert.Equal(SecretRedactor.Mask, SecretRedactor.RedactNamed("apiToken", "gizli"));
        Assert.Equal(SecretRedactor.Mask, SecretRedactor.RedactNamed("client_secret", "gizli"));
        Assert.Equal("açık", SecretRedactor.RedactNamed("sunucu", "açık"));

        var headers = SecretRedactor.HeadersForLog(new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer abc",
            ["Accept"] = "application/json",
        });
        Assert.Equal(SecretRedactor.Mask, headers["Authorization"]);
        Assert.Equal("application/json", headers["Accept"]);
    }
}
