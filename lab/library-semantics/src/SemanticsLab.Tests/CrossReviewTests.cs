using System.Diagnostics;

namespace BigRedProf.Stories.LibrarySemanticsLab.Tests
{
	public class CrossReviewTests
	{
		public CrossReviewTests(Xunit.Abstractions.ITestOutputHelper output)
		{
			_output = output;
		}

		private readonly Xunit.Abstractions.ITestOutputHelper _output;

		[Fact]
		public void OneMovement_ReusesUnchangedStoryHashes()
		{
			string path = LabPaths.Create(nameof(OneMovement_ReusesUnchangedStoryHashes));
			try
			{
				using (Library library = Library.Create(path, BatchFormat.PerFrame))
				{
					_output.WriteLine("stories\tmilliseconds");
					int[] sizes = new int[] { 10, 1000 };
					for (int s = 0; s < sizes.Length; s++)
					{
						SeedStories(library, sizes[s]);
						long milliseconds = TimeOneMovement(library, sizes[s]);
						_output.WriteLine(sizes[s].ToString() + "\t" + milliseconds.ToString());
						Assert.True(milliseconds < 30000);
					}
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void Compaction_NextIncrementalCopiesTheNewGeneration()
		{
			string sourcePath = LabPaths.Create(nameof(Compaction_NextIncrementalCopiesTheNewGeneration));
			string backupPath = LabPaths.Create(nameof(Compaction_NextIncrementalCopiesTheNewGeneration) + "-backup");
			try
			{
				using (Library source = Library.Create(sourcePath, BatchFormat.PerFrame))
				using (Library backup = Library.Create(backupPath, BatchFormat.PerFrame))
				{
					Assert.Equal(AppendStatus.Committed, Digihouse.InitializeRoom(source, "parlor", null).Status);
					for (int i = 0; i < 300; i++)
						Assert.Equal(AppendStatus.Committed, Digihouse.Move(source, "parlor", "chair", i, 0, i + 1, null).Status);

					PreserveResult full = Preserver.Preserve(source, backup, null, null);
					Assert.True(full.Published);
					Assert.Equal(AppendStatus.Committed, Digihouse.Move(source, "parlor", "chair", 300, 1, 301, null).Status);
					PreserveResult suffix = Preserver.Preserve(source, backup, null, null);
					Assert.True(suffix.Published);
					Assert.True(suffix.BytesCopied < full.BytesCopied);
					string beforeCompact = source.CurrentEditionId;
					Assert.Equal(AppendStatus.Committed, Digihouse.CompactMoves(source, "parlor", null).Status);
					Assert.NotEqual(beforeCompact, source.CurrentEditionId);
					long generationBytes = new FileInfo(source.PublishedStoryPath(Digihouse.RoomStoryId("parlor"))).Length;
					PreserveResult afterCompact = Preserver.Preserve(source, backup, null, null);
					Assert.True(afterCompact.Published);
					Assert.True(afterCompact.BytesCopied >= generationBytes);
					Assert.True(afterCompact.BytesCopied > suffix.BytesCopied);
					Assert.Equal(CheckResult.Pass, backup.Verify(afterCompact.EditionId, null).Integrity);
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(backupPath);
			}
		}

		#region private functions
		private static void SeedStories(Library library, int count)
		{
			while (library.ReadCurrent().Stories.Count < count)
			{
				int have = library.ReadCurrent().Stories.Count;
				int batch = Math.Min(500, count - have);
				List<StoryAppend> stories = new List<StoryAppend>();
				for (int i = 0; i < batch; i++)
				{
					int index = have + i;
					RecordDraft draft = new RecordDraft(RetentionClass.Stone, LabCode.FromText("open-" + index.ToString()));
					stories.Add(new StoryAppend("bench/" + index.ToString("D5"), 0, new RecordDraft[] { draft }));
				}

				AppendRequest request = new AppendRequest(
					"seed-" + have.ToString() + "-" + batch.ToString(),
					library.CurrentEditionId,
					stories
				);
				Assert.Equal(AppendStatus.Committed, library.Append(request, null).Status);
			}
		}

		private static long TimeOneMovement(Library library, int size)
		{
			RecordDraft draft = new RecordDraft(RetentionClass.Sand, LabCode.FromText("move-" + size.ToString()));
			long offset = library.HighWater("bench/00000");
			StoryAppend story = new StoryAppend("bench/00000", offset, new RecordDraft[] { draft });
			AppendRequest request = new AppendRequest(
				"move-" + size.ToString(),
				library.CurrentEditionId,
				new StoryAppend[] { story }
			);
			Stopwatch watch = Stopwatch.StartNew();
			AppendResult result = library.Append(request, null);
			watch.Stop();
			Assert.Equal(AppendStatus.Committed, result.Status);
			return watch.ElapsedMilliseconds;
		}
		#endregion
	}
}
