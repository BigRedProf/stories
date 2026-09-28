using CommandLine;

namespace BigRedProf.Stories.StoriesCli
{
	[Verb("backup-all", HelpText = "Append every story's new things to this week's tapes, then commit the manifest.")]
	public sealed class BackupAllOptions : BaseCommandLineOptions
	{
		#region properties
		[Option("tapeRoot", Required = true, HelpText = "Directory holding one subdirectory per generation.")]
		public string TapeRoot { get; set; } = default!;

		[Option("generation", Required = false, HelpText = "Generation to back up into. Defaults to this ISO week (UTC), e.g. 2026-W39.")]
		public string? Generation { get; set; }
		#endregion
	}
}
