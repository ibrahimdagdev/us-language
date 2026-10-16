using Us.Cli;
using Us.Data.SqlServer;

SqlServerRegistration.Register();

var host = new CliHost(Directory.GetCurrentDirectory())
{
    In = Console.In,
    Out = Console.Out,
    Error = Console.Error,
};
var result = await UsCli.RunAsync(args, host).ConfigureAwait(false);
return result.ExitCode;
