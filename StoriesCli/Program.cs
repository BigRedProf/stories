using CommandLine;
using System.Collections.Generic;

namespace BigRedProf.Stories.StoriesCli
{
	public class Program
	{
		#region static functions
		static private int Main(string[] args)
		{
			// Parse arguments for multiple option classes
			return CommandLine.Parser.Default.ParseArguments<ListenOptions, SyncLogsToSqlOptions>(args)
				.MapResult(
					(ListenOptions listenOptions) => RunOptions(listenOptions),
					(SyncLogsToSqlOptions syncOptions) => RunOptions(syncOptions),
					errors => HandleParseError(errors)
				);
		}

		static private int RunOptions(BaseCommandLineOptions options)
		{
			Command command;
			if (options is ListenOptions)
			{
				command = new ListenCommand();
			}
			else if (options is SyncLogsToSqlOptions)
			{
				command = new SyncLogsToSqlCommand();
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
