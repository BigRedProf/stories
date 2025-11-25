using CommandLine;

namespace BigRedProf.Stories.StoriesCli
{
	[Verb("backup", HelpText = "One-shot snapshot of a story to a tape series.")]
	public sealed class BackupOptions : BaseCommandLineOptions
	{
		[Option("tapeRoot", Required = true, HelpText = "Root path for the DiskLibrary (e.g., D:\\Backups).")]
		public string TapeRoot { get; set; } = default!;

		[Option("storyId", Required = true, HelpText = "Story Identifier (e.g., myapp/user/data/events).")]
		public string StoryId { get; set; } = default!;

		[Option("seriesId", Required = true, HelpText = "Tape series identifier under the root (e.g., 00000000-000a-000b-000c-000000000001).")]
		public Guid SeriesId { get; set; }

		[Option("incrementalBackup", Required = false, HelpText = "Allows incremental backups. If true, picks up from last checkpoint. If false, does full backup.")]
		public bool? IncrementalBackup { get; set; }
	}
}
