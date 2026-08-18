using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
			// Every verb below must appear here too, or its options class never gets parsed
			// and the verb looks like it doesn't exist.
			return CommandLine.Parser.Default.ParseArguments<
					ListenOptions,
					SyncLogsToSqlOptions,
					BackupOptions,
					RestoreOptions,
					VerifyOptions,
					InspectOptions
				>(args)
				.MapResult(
					(ListenOptions o) => RunOptions(serviceProvider, o),
					(SyncLogsToSqlOptions o) => RunOptions(serviceProvider, o),
					(BackupOptions o) => RunOptions(serviceProvider, o),
					(RestoreOptions o) => RunOptions(serviceProvider, o),
					(VerifyOptions o) => RunOptions(serviceProvider, o),
					(InspectOptions o) => RunOptions(serviceProvider, o),
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
			else if(options is BackupOptions)
			{
				ILogger<BackupCommand> logger = serviceProvider.GetService<ILogger<BackupCommand>>()!;
				ILogger<ApiClient> apiClientLogger = serviceProvider.GetService<ILogger<ApiClient>>()!;
				command = new BackupCommand(logger, apiClientLogger);
			}
			else if(options is RestoreOptions)
			{
				command = new RestoreCommand();
			}
			else if (options is VerifyOptions)
			{
				command = new VerifyCommand();
			}
			else if (options is InspectOptions)
			{
				command = new InspectCommand();
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
