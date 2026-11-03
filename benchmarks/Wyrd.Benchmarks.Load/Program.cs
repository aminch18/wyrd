using System.Globalization;
using Wyrd.Benchmarks.Load;

// The durable host under a steady load, each role in a process (a container) of its own, as an application runs it:
//   setup    creates the database: Wyrd's tables, the stock, pg_stat_statements
//   worker   one runtime running --loops worker loops (default 1); run as many containers as you like
//   load     sends orders at --rate per second for --duration seconds, whatever the workers' pace (an open loop)
//   ceiling  plain PostgreSQL inserts at --rate per second, one transaction each: what the database alone does
// The connection comes from WYRD_CONNECTION. See benchmarks/README.md and compose.yaml in this folder.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var connection = Environment.GetEnvironmentVariable("WYRD_CONNECTION")
    ?? throw new InvalidOperationException("Set WYRD_CONNECTION to the PostgreSQL connection string.");
var options = new Options(args.Skip(1).ToArray());

return args.FirstOrDefault() switch
{
    "setup" => await Setup.RunAsync(connection, options),
    "worker" => await Worker.RunAsync(connection, options),
    "load" => await Load.RunAsync(connection, options),
    "ceiling" => await Ceiling.RunAsync(connection, options),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("Usage: Wyrd.Benchmarks.Load setup|worker|load|ceiling [--option value ...]");
    return 2;
}
