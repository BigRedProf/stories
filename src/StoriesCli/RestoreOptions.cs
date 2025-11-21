using CommandLine;

namespace BigRedProf.Stories.StoriesCli
{
	[Verb("restore", HelpText = "Replay a tape into a story (via IScribe).")]
	public sealed class RestoreOptions : BaseCommandLineOptions
	{
		[Option("tape", Required = true, HelpText = "Tape path (file) to read.")]
		public string TapePath { get; set; } = default!;
	}
}
