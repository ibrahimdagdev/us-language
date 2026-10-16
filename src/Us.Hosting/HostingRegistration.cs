using System.Runtime.CompilerServices;
using Us.Runtime;

namespace Us.Hosting;

/// <summary>Wires Kestrel listen hook into <see cref="UsEmitHttp"/> when this assembly is loaded.</summary>
public static class HostingRegistration
{
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Init() => Register();
#pragma warning restore CA2255

    public static void Register() =>
        UsEmitHttp.ProductionListen = static () => UsKestrelHost.MaybeListen();
}
