using CommandLine;

namespace BigRedProf.Stories.StoriesCli
{
	[Verb("inspect", HelpText = "Show quick stats about a tape.")]
	public sealed class InspectOptions : BaseCommandLineOptions
	{
		[Option("tape", Required = true, HelpText = "Tape path (file) to read.")]
		public string TapePath { get; set; } = default!;
	}
}
