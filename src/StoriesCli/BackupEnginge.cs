using BigRedProf.Data.Core;
using BigRedProf.Data.Tape;
using BigRedProf.Stories.Models;
using Microsoft.Extensions.Logging;

namespace BigRedProf.Stories.StoriesCli
{
	public static class BackupEngine
	{
		#region constants
		private const long CheckpointInterval = 100_000;
		#endregion

		#region functions
		public static void BackupStoryToSeries(
			IPiedPiper piedPiper,
			IStoryteller storyteller,
			TapeLibrary tapeLibrary,
			Guid seriesId,
			bool incrementalBackup,
			ILogger logger
		)
		{
			BackupWizard backupWizard = BackupWizard.CreateNew(tapeLibrary, seriesId, "test tape", "blahblahblah");

			// Optional resume point via BackupWizard checkpoint
			long bookmark = 0;
			if (incrementalBackup)
			{
				try
				{
					Code bookmarkCode = backupWizard.GetLatestCheckpoint();
					bookmark = piedPiper.DecodeModel<long>(bookmarkCode, CoreSchema.Int64);

					logger.LogInformation("Resuming backup from offset {StartOffset}.", bookmark);
				}
				catch (InvalidOperationException)
				{
					logger.LogInformation("No checkpoint found; starting full backup from offset 0.");
				}
			}

			long lastOffset = storyteller.Bookmark;

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
					if (incrementalBackup == true)
					{
						Code lastOffsetCode = piedPiper.EncodeModel(lastOffset, CoreSchema.Int64);
						backupWizard.SetLatestCheckpoint(lastOffsetCode);
					}
				}
			}

			if (incrementalBackup)
			{
				Code lastOffsetCode = piedPiper.EncodeModel(lastOffset, CoreSchema.Int64);
				backupWizard.SetLatestCheckpoint(lastOffsetCode);
			}
			else
			{
				// HACKHACK: Force flush of underlying streams.
				backupWizard.SetLatestCheckpoint("0000000000000000");
			}

				logger.LogInformation("Backup complete. Series={SeriesId}, LastOffset={LastOffset}", seriesId, lastOffset);
		}

		public static void RestoreStoryFromSeries(
			IPiedPiper piedPiper,
			TapeLibrary tapeLibrary,
			Guid seriesId,
			IScribe scribe,
			ILogger logger
		)
		{
			RestorationWizard restorationWizard = RestorationWizard.OpenExistingTapeSeries(tapeLibrary, seriesId, 0);
			CodeReader reader = restorationWizard.CodeReader;
			PackRat<Code> codePackRat = piedPiper.GetPackRat<Code>(CoreSchema.Code);
			try
			{
				while (true)
				{
					Code thing = codePackRat.UnpackModel(reader);
					scribe.RecordSomething(thing);
				}
			}
			catch(InvalidOperationException)
			{
				// TODO: Need a proper way of detecting end-of-tape-series, but CodeReader doesn't currently
				// expose this.
			}
			
			logger.LogInformation("Restore complete. Series={SeriesId}", seriesId);
		}
		#endregion
	}
}
