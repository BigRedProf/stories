using CommandLine;

namespace BigRedProf.Stories.StoriesCli
{
	[Verb("verify", HelpText = "Validate that all frames are readable Codes.")]
	public sealed class VerifyOptions : BaseCommandLineOptions
	{
		[Option("tape", Required = true, HelpText = "Tape path (file) to read.")]
		public string TapePath { get; set; } = default!;
	}
}
