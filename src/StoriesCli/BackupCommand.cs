using BigRedProf.Data.Core;
using BigRedProf.Stories.Models;
using BigRedProf.Data.Tape;
using Microsoft.Extensions.Logging;
using BigRedProf.Data.Tape.Libraries;

namespace BigRedProf.Stories.StoriesCli
{
	public sealed class BackupCommand : Command
	{
		#region constants
		private const long TellLimit = 50_000;
		private const long CheckpointInterval = 100_000;
		#endregion

		#region fields
		private readonly ILogger<BackupCommand> _logger;
		private readonly ILogger<ApiClient> _apiClientLogger;
		#endregion

		#region constructors
		public BackupCommand(ILogger<BackupCommand> logger, ILogger<ApiClient> apiClientLogger)
		{
			_logger = logger;
			_apiClientLogger = apiClientLogger;
		}
		#endregion

		#region Command methods
		public override int Run(BaseCommandLineOptions baseOpts)
		{
			BackupOptions options = (BackupOptions)baseOpts;

			// PiedPiper & ApiClient
			IPiedPiper piedPiper = new PiedPiper();
			piedPiper.RegisterCorePackRats();
			piedPiper.RegisterPackRats(typeof(StoryThing).Assembly);

			ApiClient apiClient = new ApiClient(options.BaseUri!, piedPiper, _apiClientLogger, null);

			// Tape library + wizard (series-based)
			DiskLibrary diskLibrary = new DiskLibrary(options.TapeRoot);
			BackupWizard backupWizard = BackupWizard.OpenExisting(diskLibrary, options.SeriesId);

			// Optional resume point via BackupWizard checkpoint
			long bookmark = 0;
			if (options.IncrementalBackup == true)
			{
				try
				{
					Code bookmarkCode = backupWizard.GetLatestCheckpoint();
					bookmark = piedPiper.DecodeModel<long>(bookmarkCode, CoreSchema.Int64);

					_logger.LogInformation("Resuming backup from offset {StartOffset}.", bookmark);
				}
				catch (InvalidOperationException)
				{
					_logger.LogInformation("No checkpoint found; starting full backup from offset 0.");
				}
			}

			IStoryteller storyteller = apiClient.GetStoryteller(options.Story!, bookmark, TellLimit);

			long lastOffset = bookmark;

			while (storyteller.HasSomethingForMe)
			{
				StoryThing storyThing = storyteller.TellMeSomething();
				// Write the Code frame (offset implied by order in series)
				Code encodedThing = piedPiper.EncodeModel(storyThing.Thing, CoreSchema.Code);
				backupWizard.Append(encodedThing);

				lastOffset = storyThing.Offset + 1; // next offset expected

				// Opportunistic checkpointing if requested
				if (storyteller.Bookmark % CheckpointInterval == 0)
				{
					if (options.IncrementalBackup == true)
					{
						Code lastOffsetCode = piedPiper.EncodeModel(lastOffset, CoreSchema.Int64);
						backupWizard.SetLatestCheckpoint(lastOffsetCode);
					}
				}
			}

			if (options.IncrementalBackup == true)
			{
				Code lastOffsetCode = piedPiper.EncodeModel(lastOffset, CoreSchema.Int64);
				backupWizard.SetLatestCheckpoint(lastOffsetCode);
			}

			_logger.LogInformation("Backup complete. Series={SeriesId}, LastOffset={LastOffset}", options.SeriesId, lastOffset);
			return 0;
		}

		protected override void OnCancelKeyPress()
		{
		} 
		#endregion
	}
}
