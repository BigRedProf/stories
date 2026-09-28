using CommandLine;

namespace BigRedProf.Stories.StoriesCli
{
	[Verb("verify-all", HelpText = "Check every segment of a generation decodes, joins up, and is no longer than the live story.")]
	public sealed class VerifyAllOptions : BaseCommandLineOptions
	{
		#region properties
		[Option("tapeRoot", Required = true, HelpText = "Directory holding one subdirectory per generation.")]
		public string TapeRoot { get; set; } = default!;

		[Option("generation", Required = false, HelpText = "Generation to verify. Defaults to the newest one with a manifest.")]
		public string? Generation { get; set; }
		#endregion
	}
}
