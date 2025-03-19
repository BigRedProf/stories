using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Diagnostics;

namespace BigRedProf.Stories.StoriesCli
{
	public class Program
	{
		#region static functions
		static private int Main(string[] args)
		{
			// Set up dependency injection
			var serviceProvider = new ServiceCollection()
				.AddLogging(builder =>
				{
					builder.AddConsole();
					builder.SetMinimumLevel(LogLevel.Warning); // Set log level
				})
				.BuildServiceProvider();

			// Parse arguments for multiple option classes
			return CommandLine.Parser.Default.ParseArguments<ListenOptions, SyncLogsToSqlOptions>(args)
				.MapResult(
					(ListenOptions listenOptions) => RunOptions(serviceProvider, listenOptions),
					(SyncLogsToSqlOptions syncOptions) => RunOptions(serviceProvider, syncOptions),
					errors => HandleParseError(errors)
				);
		}

		static private int RunOptions(ServiceProvider serviceProvider, BaseCommandLineOptions options)
		{
			Command command;
			if (options is ListenOptions)
			{
				ILogger<ApiClient> apiClientLogger = serviceProvider.GetService<ILogger<ApiClient>>()!;
				command = new ListenCommand(apiClientLogger);
			}
			else if (options is SyncLogsToSqlOptions)
			{
				ILogger<SyncLogsToSqlCommand> logger = serviceProvider.GetService<ILogger<SyncLogsToSqlCommand>>()!;
				ILogger<ApiClient> apiClientLogger = serviceProvider.GetService<ILogger<ApiClient>>()!;
				command = new SyncLogsToSqlCommand(logger, apiClientLogger);
			}
			else
			{
				Console.Error.WriteLine("Invalid command.");
				return 1;
			}

			return command.Run(options);
		}

		static private int HandleParseError(IEnumerable<Error> errors)
		{
			Console.WriteLine("Invalid usage.");
			foreach (Error error in errors)
			{
				Console.Error.WriteLine(error.ToString());
			}
			return 1;
		}
		#endregion
	}
}
