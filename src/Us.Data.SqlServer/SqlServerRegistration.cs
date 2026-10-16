using System.Runtime.CompilerServices;
using Us.Runtime;

namespace Us.Data.SqlServer;

/// <summary>Registers SQL Server backend factory with <see cref="UsEmitDatabase"/>.</summary>
public static class SqlServerRegistration
{
#pragma warning disable CA2255 // Library registration when assembly is loaded by host/tests/CLI.
    [ModuleInitializer]
    internal static void Init() => Register();
#pragma warning restore CA2255

    public static void Register()
    {
        UsEmitDatabase.RegisterBackendFactory(connection => new SqlServerBackend(connection));
        UsEmitDatabase.RegisterSchemaSync(static (schemas, approve) =>
        {
            _ = SqlSchemaSync.TryApplyFromEnvironment(schemas, approve);
        });
    }
}
