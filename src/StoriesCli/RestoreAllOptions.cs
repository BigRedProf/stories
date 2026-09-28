using CommandLine;

namespace BigRedProf.Stories.StoriesCli
{
	[Verb("restore-all", HelpText = "Replay every story in a generation into an EMPTY stories service.")]
	public sealed class RestoreAllOptions : BaseCommandLineOptions
	{
		#region properties
		[Option("tapeRoot", Required = true, HelpText = "Directory holding one subdirectory per generation.")]
		public string TapeRoot { get; set; } = default!;

		[Option("generation", Required = false, HelpText = "Generation to restore. Defaults to the newest one with a manifest.")]
		public string? Generation { get; set; }
		#endregion
	}
}
