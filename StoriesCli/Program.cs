using CommandLine;

namespace BigRedProf.Stories.StoriesCli
{
	public class Program
	{
		#region static functions
		static private int Main(string[] args)
		{
			int exitCode = CommandLine.Parser.Default.ParseArguments<CommandLineOptions>(args)
				.MapResult<CommandLineOptions, int>(
					options => RunOptions(options),
					errors => HandleParseError(errors)
				);
			return exitCode;
		}

		static private int RunOptions(CommandLineOptions options)
		{
			Command command;
			switch(options.Command)
			{
				case "listen": command = new ListenCommand(); break;
				default: Console.Error.WriteLine("Invalid command."); return 1;
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
