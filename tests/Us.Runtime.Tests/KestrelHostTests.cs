using System.Net;
using System.Text;
using System.Text.Json;
using Us.Hosting;
using Us.Runtime;

namespace Us.Runtime.Tests;

public class KestrelHostTests : IDisposable
{
    public KestrelHostTests()
    {
        UsEmitHttp.ResetRoutes();
        UsEmitDi.Reset();
        UsEmitDatabase.Reset();
    }

    public void Dispose()
    {
        UsEmitHttp.ResetRoutes();
        UsEmitDi.Reset();
        UsEmitDatabase.Reset();
    }

    [Fact]
    public async Task GetRouteReturnsBodyOverRealTcp()
    {
        UsEmitHttp.Register(
            "GET",
            "/api/selam",
            [],
            [],
            [],
            [],
            [],
            _ => UsEmitHttp.Result(200, UsValue.FromText("merhaba")));

        await using var host = await UsKestrelHost.StartAsync(UsEmitHttp.DispatchRequestAsync, "http://127.0.0.1:0");
        using var client = new HttpClient { BaseAddress = host.BaseAddress };
        var response = await client.GetAsync("/api/selam");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("merhaba", body);
    }

    [Fact]
    public async Task GetRouteParameterAndMethodsWork()
    {
        UsEmitHttp.Register(
            "GET",
            "/kullanicilar/{id}",
            ["id"],
            ["route"],
            ["tamsayı"],
            [""],
            [],
            args => UsEmitHttp.Result(200, UsValue.FromText("id=" + args[0].Int)));
        UsEmitHttp.Register(
            "PUT",
            "/kullanicilar/{id}",
            ["id"],
            ["route"],
            ["tamsayı"],
            [""],
            [],
            args => UsEmitHttp.Result(200, UsValue.FromText("put=" + args[0].Int)));
        UsEmitHttp.Register(
            "PATCH",
            "/kullanicilar/{id}",
            ["id"],
            ["route"],
            ["tamsayı"],
            [""],
            [],
            args => UsEmitHttp.Result(200, UsValue.FromText("patch=" + args[0].Int)));
        UsEmitHttp.Register(
            "DELETE",
            "/kullanicilar/{id}",
            ["id"],
            ["route"],
            ["tamsayı"],
            [""],
            [],
            args => UsEmitHttp.Result(200, UsValue.FromText("del=" + args[0].Int)));

        await using var host = await UsKestrelHost.StartAsync(UsEmitHttp.DispatchRequestAsync, "http://127.0.0.1:0");
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        Assert.Equal("id=42", await (await client.GetAsync("/kullanicilar/42")).Content.ReadAsStringAsync());
        Assert.Equal("put=7", await (await client.PutAsync("/kullanicilar/7", new StringContent(""))).Content.ReadAsStringAsync());
        Assert.Equal(
            "patch=8",
            await (await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, "/kullanicilar/8"))).Content.ReadAsStringAsync());
        Assert.Equal("del=9", await (await client.DeleteAsync("/kullanicilar/9")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PostJsonBodyAndUnsupportedMedia()
    {
        var shape = UsEmitJson.EncodeShape("Kişi", ["ad"], ["metin"], [false], [true], [""]);
        UsEmitHttp.Register(
            "POST",
            "/kisi",
            ["girdi"],
            ["body"],
            ["Kişi"],
            [shape],
            [],
            args =>
            {
                var ad = UsEmitObject.ReadField(args[0], "ad", 0, 0);
                return UsEmitHttp.Result(201, ad);
            });

        await using var host = await UsKestrelHost.StartAsync(UsEmitHttp.DispatchRequestAsync, "http://127.0.0.1:0");
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        using var json = new StringContent("""{"ad":"Ada"}""", Encoding.UTF8, "application/json");
        var created = await client.PostAsync("/kisi", json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("Ada", await created.Content.ReadAsStringAsync());

        using var plain = new StringContent("ad=Ada", Encoding.UTF8, "text/plain");
        var rejected = await client.PostAsync("/kisi", plain);
        Assert.Equal((HttpStatusCode)415, rejected.StatusCode);
        Assert.Contains("application/json", await rejected.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownRouteIs404AndMethodMismatchIs405()
    {
        UsEmitHttp.Register(
            "GET",
            "/yalniz-get",
            [],
            [],
            [],
            [],
            [],
            _ => UsEmitHttp.Result(200, UsValue.FromText("ok")));

        await using var host = await UsKestrelHost.StartAsync(UsEmitHttp.DispatchRequestAsync, "http://127.0.0.1:0");
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        var missing = await client.GetAsync("/yok");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var mismatch = await client.PostAsync("/yalniz-get", new StringContent(""));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, mismatch.StatusCode);
        Assert.True(mismatch.Content.Headers.Contains("Allow") || mismatch.Headers.Contains("Allow"));
    }

    [Fact]
    public async Task AsyncRouteHandlerWorks()
    {
        UsEmitHttp.RegisterAsync(
            "GET",
            "/async",
            [],
            [],
            [],
            [],
            [],
            async _ =>
            {
                await Task.Delay(10).ConfigureAwait(false);
                return UsEmitHttp.Result(200, UsValue.FromText("async-ok"));
            });

        await using var host = await UsKestrelHost.StartAsync(UsEmitHttp.DispatchRequestAsync, "http://127.0.0.1:0");
        using var client = new HttpClient { BaseAddress = host.BaseAddress };
        var body = await (await client.GetAsync("/async")).Content.ReadAsStringAsync();
        Assert.Equal("async-ok", body);
    }

    [Fact]
    public async Task ConcurrentRequestsStayIsolated()
    {
        UsEmitHttp.Register(
            "GET",
            "/iso/{id}",
            ["id"],
            ["route"],
            ["metin"],
            [""],
            [],
            args => UsEmitHttp.Result(200, UsValue.FromText("got:" + args[0].Text)));
        UsEmitHttp.Register(
            "POST",
            "/iso",
            ["girdi"],
            ["body"],
            ["Kutu"],
            [UsEmitJson.EncodeShape("Kutu", ["ad"], ["metin"], [false], [true], [""])],
            [],
            args =>
            {
                var ad = UsEmitObject.ReadField(args[0], "ad", 0, 0);
                return UsEmitHttp.Result(200, UsValue.FromText("body:" + ad.Text));
            });

        await using var host = await UsKestrelHost.StartAsync(UsEmitHttp.DispatchRequestAsync, "http://127.0.0.1:0");
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        var tasks = Enumerable.Range(0, 32).Select(async i =>
        {
            if (i % 2 == 0)
            {
                var response = await client.GetAsync("/iso/r" + i);
                var text = await response.Content.ReadAsStringAsync();
                Assert.Equal("got:r" + i, text);
            }
            else
            {
                using var content = new StringContent("{\"ad\":\"a" + i + "\"}", Encoding.UTF8, "application/json");
                var response = await client.PostAsync("/iso", content);
                var text = await response.Content.ReadAsStringAsync();
                Assert.Equal("body:a" + i, text);
            }
        });
        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task MemoryDatabaseRouteEndToEnd()
    {
        UsEmitDatabase.RegisterTable(
            "kullanıcılar",
            "Kullanıcı",
            ["id", "ad"],
            ["tamsayı", "metin"],
            [false, false],
            [true, true],
            "id");

        UsEmitHttp.RegisterAsync(
            "POST",
            "/kullanicilar",
            ["girdi"],
            ["body"],
            ["Kullanıcı"],
            [UsEmitJson.EncodeShape("Kullanıcı", ["id", "ad"], ["tamsayı", "metin"], [false, false], [true, true], ["", ""])],
            [],
            async args =>
            {
                await AwaitUs(UsEmitDatabase.Insert(UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0), args[0], 0, 0)).ConfigureAwait(false);
                return UsEmitHttp.Result(201, args[0]);
            });
        UsEmitHttp.RegisterAsync(
            "GET",
            "/kullanicilar/{id}",
            ["id"],
            ["route"],
            ["tamsayı"],
            [""],
            [],
            async args =>
            {
                var all = await AwaitUs(UsEmitDatabase.All(UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0), 0, 0)).ConfigureAwait(false);
                var items = ((UsEmitList)all.Reference!).Items;
                var id = args[0].Int;
                foreach (var item in items)
                {
                    if (UsEmitObject.ReadField(item, "id", 0, 0).Int == id)
                    {
                        return UsEmitHttp.Result(200, item);
                    }
                }

                return UsEmitHttp.Result(404, UsValue.FromText("yok"));
            });

        await using var host = await UsKestrelHost.StartAsync(UsEmitHttp.DispatchRequestAsync, "http://127.0.0.1:0");
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        using var post = new StringContent("""{"id":1,"ad":"Ada"}""", Encoding.UTF8, "application/json");
        var created = await client.PostAsync("/kullanicilar", post);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var got = await client.GetAsync("/kullanicilar/1");
        Assert.Equal(HttpStatusCode.OK, got.StatusCode);
        using var doc = JsonDocument.Parse(await got.Content.ReadAsStringAsync());
        Assert.Equal("Ada", doc.RootElement.GetProperty("ad").GetString());
    }

    [Fact]
    public async Task ShutdownReleasesPort()
    {
        UsEmitHttp.Register(
            "GET",
            "/",
            [],
            [],
            [],
            [],
            [],
            _ => UsEmitHttp.Result(200, UsValue.FromText("x")));

        await using (var host = await UsKestrelHost.StartAsync(UsEmitHttp.DispatchRequestAsync, "http://127.0.0.1:0"))
        {
            using var client = new HttpClient { BaseAddress = host.BaseAddress };
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        }

        await using var again = await UsKestrelHost.StartAsync(UsEmitHttp.DispatchRequestAsync, "http://127.0.0.1:0");
        using var client2 = new HttpClient { BaseAddress = again.BaseAddress };
        Assert.Equal(HttpStatusCode.OK, (await client2.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task DefaultBindingIsLoopback()
    {
        Assert.StartsWith("http://127.0.0.1:", UsKestrelHost.ResolveUrls(), StringComparison.Ordinal);
        Environment.SetEnvironmentVariable("US_PORT", "0");
        try
        {
            Assert.Equal("http://127.0.0.1:0", UsKestrelHost.ResolveUrls());
        }
        finally
        {
            Environment.SetEnvironmentVariable("US_PORT", null);
        }
    }

    [Fact]
    public void HttpClientTransportIsInjectable()
    {
        var previous = UsEmitHost.Transport;
        try
        {
            var transport = new HttpClientTransport();
            UsEmitHost.Transport = transport;
            Assert.Same(transport, UsEmitHost.Transport);
            UsEmitHost.UseHttpClientTransport();
            Assert.IsType<HttpClientTransport>(UsEmitHost.Transport);
        }
        finally
        {
            UsEmitHost.Transport = previous;
        }
    }

    private static Task<UsValue> AwaitUs(UsValue value)
    {
        var operation = Assert.IsType<UsOperation>(value.Reference);
        return operation.Task;
    }
}
