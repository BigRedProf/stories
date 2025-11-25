using CommandLine;

namespace BigRedProf.Stories.StoriesCli
{
	[Verb("restore", HelpText = "Replay a tape into a story (via IScribe).")]
	public sealed class RestoreOptions : BaseCommandLineOptions
	{
		[Option("tapeRoot", Required = true, HelpText = "Root path for the DiskLibrary (e.g., D:\\Backups).")]
		public string TapeRoot { get; set; } = default!;

		[Option("seriesId", Required = true, HelpText = "Tape series identifier under the root (e.g., 00000000-000a-000b-000c-000000000001).")]
		public Guid SeriesId { get; set; }
	}
}
