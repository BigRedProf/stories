using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace BigRedProf.Stories.LibrarySemanticsLab.Tests
{
	public class RoundThreeTests
	{
		public RoundThreeTests(Xunit.Abstractions.ITestOutputHelper output)
		{
			_output = output;
		}

		private readonly Xunit.Abstractions.ITestOutputHelper _output;

		[Fact]
		public void PublishedHead_HidesFlushedAndTornBytes_AcrossProcessKills()
		{
			string flushed = LabPaths.Create(nameof(PublishedHead_HidesFlushedAndTornBytes_AcrossProcessKills) + "-flushed");
			string torn = LabPaths.Create(nameof(PublishedHead_HidesFlushedAndTornBytes_AcrossProcessKills) + "-torn");
			string framed = LabPaths.Create(nameof(PublishedHead_HidesFlushedAndTornBytes_AcrossProcessKills) + "-frame");
			try
			{
				SeedKept(flushed, BatchFormat.PerFrame);
				using (PublishedListener listener = new PublishedListener(flushed, 2))
				{
					CrashRun killed = Run("kill-append", flushed, "per-frame", FaultPoint.AfterFrameBeforePublish, "2");
					Assert.NotEqual(0, killed.ExitCode);
					Thread.Sleep(250);
					List<string> raw = RawTexts(flushed);
					Assert.Contains("record-0", raw);
					Assert.Contains("record-1", raw);
					Assert.Equal(new string[] { "kept" }, HeadTexts(flushed));
					AssertNever(listener, "record-0");
					AssertNever(listener, "record-1");
					using (Library library = Library.Open(flushed))
					{
						RecordDraft draft = new RecordDraft(RetentionClass.Stone, LabCode.FromText("replacement"));
						StoryAppend story = new StoryAppend("durability", library.HighWater("durability"), new RecordDraft[] { draft });
						AppendRequest request = new AppendRequest("replacement", library.CurrentEditionId, new StoryAppend[] { story });
						Assert.Equal(AppendStatus.Committed, library.Append(request, null).Status);
					}

					Assert.True(listener.WaitForText("replacement", 5000));
					AssertNever(listener, "record-0");
					AssertNever(listener, "record-1");
					Assert.Contains("replacement", TextsOf(listener.Snapshot()));
				}

				SeedKept(torn, BatchFormat.PerFrame);
				CrashRun tornRun = Run("kill-append", torn, "per-frame", FaultPoint.DuringFrameWrite, "2", "--tear-flushed");
				Assert.NotEqual(0, tornRun.ExitCode);
				string tornPath = LibraryLayout.Generation(torn, "durability", 0);
				LogImage tornImage = StoryLog.Read(tornPath);
				Assert.True(tornImage.TornTail);
				Assert.Contains("record-0", TextsOf(tornImage.Committed));
				Assert.DoesNotContain("record-1", TextsOf(tornImage.Committed));
				Assert.True(new FileInfo(tornPath).Length > tornImage.ValidLength);
				Assert.Equal(new string[] { "kept" }, HeadTexts(torn));

				SeedKept(framed, BatchFormat.LengthPrefixed);
				CrashRun framedRun = Run("kill-append", framed, "length-prefixed", FaultPoint.AfterFrameBeforePublish, "2");
				Assert.NotEqual(0, framedRun.ExitCode);
				List<string> rawFrame = RawTexts(framed);
				Assert.Contains("record-0", rawFrame);
				Assert.Contains("record-1", rawFrame);
				Assert.Equal(new string[] { "kept" }, HeadTexts(framed));
			}
			finally
			{
				LabPaths.Delete(flushed);
				LabPaths.Delete(torn);
				LabPaths.Delete(framed);
			}
		}

		[Fact]
		public void PublishedListener_LatencyAfterAppend()
		{
			string path = LabPaths.Create(nameof(PublishedListener_LatencyAfterAppend));
			try
			{
				using (Library library = Library.Create(path, BatchFormat.PerFrame))
				using (PublishedListener listener = new PublishedListener(path, 1))
				{
					List<double> endToEnd = new List<double>();
					List<double> afterReturn = new List<double>();
					List<double> afterRaw = new List<double>();
					for (int i = 0; i < 9; i++)
					{
						string text = "tick-" + i.ToString(CultureInfo.InvariantCulture);
						string storyPath = LibraryLayout.Generation(path, "latency", 0);
						long rawTicks = -1;
						Stopwatch watch = Stopwatch.StartNew();
						Thread raw = new Thread(delegate()
						{
							while (Interlocked.Read(ref rawTicks) < 0)
							{
								if (!File.Exists(storyPath))
									continue;

								try
								{
									LogImage image = StoryLog.Read(storyPath);
									for (int r = 0; r < image.Committed.Count; r++)
									{
										if (LabCode.ToText(image.Committed[r].Body) == text)
										{
											Interlocked.Exchange(ref rawTicks, watch.ElapsedTicks);
											return;
										}
									}
								}
								catch (InvalidDataException)
								{
								}
								catch (IOException)
								{
								}
							}
						});
						raw.IsBackground = true;
						raw.Start();
						RecordDraft draft = new RecordDraft(RetentionClass.Stone, LabCode.FromText(text));
						StoryAppend story = new StoryAppend("latency", library.HighWater("latency"), new RecordDraft[] { draft });
						AppendRequest request = new AppendRequest(text, library.CurrentEditionId, new StoryAppend[] { story });
						Assert.Equal(AppendStatus.Committed, library.Append(request, null).Status);
						double returned = watch.Elapsed.TotalMilliseconds;
						Assert.True(listener.WaitForText(text, 5000));
						double heard = watch.Elapsed.TotalMilliseconds;
						Assert.True(raw.Join(2000));
						long seen = Interlocked.Read(ref rawTicks);
						Assert.True(seen >= 0);
						double rawAt = seen * 1000.0 / Stopwatch.Frequency;
						endToEnd.Add(heard);
						afterReturn.Add(heard - returned);
						afterRaw.Add(heard - rawAt);
					}

					double endMedian = Median(endToEnd);
					double returnMedian = Median(afterReturn);
					double rawMedian = Median(afterRaw);
					string report = "published-head latency, 9 appends, 1 ms poll, one story, per-frame, fsync included"
						+ "\nmedian ms from append start until the listener yielded the record\t" + endMedian.ToString("0.0", CultureInfo.InvariantCulture)
						+ "\nmedian ms from append return until the listener yielded the record\t" + returnMedian.ToString("0.0", CultureInfo.InvariantCulture)
						+ "\nmedian ms the listener trailed a raw head read of the same file\t" + rawMedian.ToString("0.0", CultureInfo.InvariantCulture)
						+ "\n";
					_output.WriteLine(report);
					File.WriteAllText(Evidence("round3-head.txt"), report);
					Assert.True(endMedian < 500);
					Assert.True(returnMedian < 100);
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void Journal_ReadEdition_IsOnePointInTime()
		{
			string path = LabPaths.Create(nameof(Journal_ReadEdition_IsOnePointInTime));
			try
			{
				using (JournalLibrary library = JournalLibrary.Create(path, 1000))
				{
					string first = AppendText(library, "notes", "one", "k1");
					string second = AppendText(library, "notes", "two", "k2");
					Assert.NotEqual(first, second);
					Assert.Equal(new string[] { "one" }, EditionTexts(library.ReadEdition(first)));
					Assert.Equal(new string[] { "one", "two" }, EditionTexts(library.ReadEdition(second)));
					Assert.Equal(first, library.ReadEdition(second).ParentId);
					AppendResult retry = AppendExisting(library, "notes", "one", "k1", 0);
					Assert.Equal(AppendStatus.AlreadyCommitted, retry.Status);
					Assert.Equal(first, retry.EditionId);
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void Journal_TwoStories_AppearTogether()
		{
			string path = LabPaths.Create(nameof(Journal_TwoStories_AppearTogether));
			try
			{
				JournalLibrary library = JournalLibrary.Create(path, 1000);
				string before = AppendText(library, "base", "kept", "kept");
				FaultInjector faults = new FaultInjector();
				faults.ArmedPoint = FaultPoint.AfterFrameBeforePublish;
				RecordDraft left = new RecordDraft(RetentionClass.Stone, LabCode.FromText("left-new"));
				RecordDraft right = new RecordDraft(RetentionClass.Stone, LabCode.FromText("right-new"));
				StoryAppend[] stories = new StoryAppend[]
				{
					new StoryAppend("left", 0, new RecordDraft[] { left }),
					new StoryAppend("right", 0, new RecordDraft[] { right })
				};
				AppendRequest request = new AppendRequest("pair", before, stories);
				Assert.Throws<CrashInjectedException>(() => library.Append(request, faults));
				library.Dispose();
				using (JournalLibrary reopened = JournalLibrary.Open(path))
				{
					Assert.Equal(before, reopened.CurrentEditionId);
					Assert.Empty(reopened.ReadHead("left"));
					Assert.Empty(reopened.ReadHead("right"));
					Assert.Contains("left-new", RawTexts(path, "left"));
					Assert.Contains("right-new", RawTexts(path, "right"));
					string after = AppendText(reopened, "left", "left-real", "left-real");
					Assert.NotEqual(before, after);
					Assert.Equal(new string[] { "left-real" }, TextsOf(reopened.ReadHead("left")));
					Assert.Empty(reopened.ReadHead("right"));
					Assert.Equal(new string[] { "kept" }, EditionTexts(reopened.ReadEdition(before)));
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void Journal_ProcessKill_BeforePointer_HidesTheBatch()
		{
			string path = LabPaths.Create(nameof(Journal_ProcessKill_BeforePointer_HidesTheBatch));
			try
			{
				using (JournalLibrary library = JournalLibrary.Create(path, 1000))
					AppendText(library, "durability", "kept", "kept");

				CrashRun killed = Run("kill-journal", path, FaultPoint.AfterJournalBeforePointer, "1");
				Assert.NotEqual(0, killed.ExitCode);
				Assert.Contains("record-0", RawTexts(path, "durability"));
				using (JournalLibrary library = JournalLibrary.Open(path))
				{
					Assert.Equal(new string[] { "kept" }, TextsOf(library.ReadHead("durability")));
					string edition = library.CurrentEditionId;
					AppendResult retry = AppendExisting(library, "durability", "record-0", "durability-batch", library.HighWater("durability"));
					Assert.Equal(AppendStatus.Committed, retry.Status);
					Assert.Equal(new string[] { "kept", "record-0" }, TextsOf(library.ReadHead("durability")));
					Assert.Equal(1, CountText(library.ReadHead("durability"), "record-0"));
					Assert.Equal(new string[] { "kept" }, EditionTexts(library.ReadEdition(edition)));
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void Journal_ProcessKill_AfterPointer_KeepsTheEdition()
		{
			string path = LabPaths.Create(nameof(Journal_ProcessKill_AfterPointer_KeepsTheEdition));
			try
			{
				using (JournalLibrary library = JournalLibrary.Create(path, 1000))
					AppendText(library, "durability", "kept", "kept");

				CrashRun killed = Run("kill-journal", path, FaultPoint.AfterDirectoryFsync, "1");
				Assert.NotEqual(0, killed.ExitCode);
				using (JournalLibrary library = JournalLibrary.Open(path))
					Assert.Equal(new string[] { "kept", "record-0" }, TextsOf(library.ReadHead("durability")));
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void Journal_Checkpoint_ReadsAfterThePrefixIsDamaged()
		{
			string path = LabPaths.Create(nameof(Journal_Checkpoint_ReadsAfterThePrefixIsDamaged));
			try
			{
				string[] ids = new string[4];
				using (JournalLibrary library = JournalLibrary.Create(path, 2))
				{
					for (int i = 0; i < ids.Length; i++)
						ids[i] = AppendText(library, "notes", "step-" + i.ToString(CultureInfo.InvariantCulture), "step-" + i.ToString(CultureInfo.InvariantCulture));
				}

				string index = Path.Combine(path, "checkpoints", "index.txt");
				Assert.True(File.Exists(Path.Combine(path, "checkpoints", ids[1] + ".txt")));
				Assert.True(File.Exists(Path.Combine(path, "checkpoints", ids[3] + ".txt")));
				long cut = 0;
				string[] lines = File.ReadAllLines(index);
				for (int i = 0; i < lines.Length; i++)
				{
					string[] parts = lines[i].Split('\t');
					if (parts.Length == 2 && parts[1] == ids[1])
						cut = long.Parse(parts[0], CultureInfo.InvariantCulture);
				}

				Assert.True(cut > 0);
				using (FileStream stream = new FileStream(Path.Combine(path, "journal.bin"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
				{
					byte[] zeros = new byte[cut];
					stream.Write(zeros, 0, zeros.Length);
					stream.Flush(true);
				}

				using (JournalLibrary library = JournalLibrary.Open(path))
				{
					Assert.Equal(ids[3], library.CurrentEditionId);
					Assert.Equal(4, library.ReadEdition(ids[3]).Records.Count);
					Assert.Equal(2, library.ReadEdition(ids[1]).Records.Count);
					Assert.Equal(3, library.ReadEdition(ids[2]).Records.Count);
					Assert.Equal("step-3", LabCode.ToText(library.ReadEdition(ids[3]).Records[3].Body));
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void Publish_JournalCost_TracksChangedStories()
		{
			string manifestPath = LabPaths.Create(nameof(Publish_JournalCost_TracksChangedStories) + "-manifest");
			string journalPath = LabPaths.Create(nameof(Publish_JournalCost_TracksChangedStories) + "-journal");
			string warmManifest = LabPaths.Create(nameof(Publish_JournalCost_TracksChangedStories) + "-warm-m");
			string warmJournal = LabPaths.Create(nameof(Publish_JournalCost_TracksChangedStories) + "-warm-j");
			try
			{
				using (Library warm = Library.Create(warmManifest, BatchFormat.PerFrame))
					AppendText(warm, "warm", "warm", "warm");

				using (JournalLibrary warm = JournalLibrary.Create(warmJournal, 1000))
					AppendText(warm, "warm", "warm", "warm");

				using (Library manifest = Library.Create(manifestPath, BatchFormat.PerFrame))
				using (JournalLibrary journal = JournalLibrary.Create(journalPath, 1000))
				{
					int manifestHave = 0;
					int journalHave = 0;
					int[] sizes = new int[] { 10, 1000, 5000, 10000 };
					double[] manifestMs = new double[sizes.Length];
					double[] journalMs = new double[sizes.Length];
					for (int i = 0; i < sizes.Length; i++)
					{
						SeedManifest(manifest, sizes[i], ref manifestHave);
						SeedJournal(journal, sizes[i], ref journalHave);
						manifestMs[i] = TimeManifest(manifest, sizes[i]);
						journalMs[i] = TimeJournal(journal, sizes[i]);
						_output.WriteLine(sizes[i].ToString(CultureInfo.InvariantCulture) + "\t" + manifestMs[i].ToString("0.0", CultureInfo.InvariantCulture) + "\t" + journalMs[i].ToString("0.0", CultureInfo.InvariantCulture));
					}

					StringBuilder report = new StringBuilder();
					report.Append("one movement, milliseconds, fsync included\n");
					report.Append("stories\tfull manifest\tjournal delta\n");
					for (int i = 0; i < sizes.Length; i++)
					{
						report.Append(sizes[i].ToString(CultureInfo.InvariantCulture));
						report.Append('\t');
						report.Append(manifestMs[i].ToString("0.0", CultureInfo.InvariantCulture));
						report.Append('\t');
						report.Append(journalMs[i].ToString("0.0", CultureInfo.InvariantCulture));
						report.Append('\n');
					}

					File.WriteAllText(Evidence("round3-publish.txt"), report.ToString());
					Assert.True(manifestMs[3] > manifestMs[0] * 5, "The full manifest did not get more expensive as the library grew.");
					Assert.True(journalMs[3] < manifestMs[1], "The journal at 10,000 stories was not cheaper than a full manifest at 1,000.");
					Assert.True(journalMs[3] < journalMs[0] * 20 + 30, "The journal movement grew with the library.");
				}
			}
			finally
			{
				LabPaths.Delete(manifestPath);
				LabPaths.Delete(journalPath);
				LabPaths.Delete(warmManifest);
				LabPaths.Delete(warmJournal);
			}
		}

		[Fact]
		public void Compaction_IncrementalCopiesTheNewGeneration_AtHouseScale()
		{
			string sourcePath = LabPaths.Create(nameof(Compaction_IncrementalCopiesTheNewGeneration_AtHouseScale));
			string backupPath = LabPaths.Create(nameof(Compaction_IncrementalCopiesTheNewGeneration_AtHouseScale) + "-backup");
			try
			{
				using (Library source = Library.Create(sourcePath, BatchFormat.PerFrame))
				using (Library backup = Library.Create(backupPath, BatchFormat.PerFrame))
				{
					string[] rooms = new string[]
					{
						"parlor", "hall", "kitchen", "study", "cellar", "attic",
						"nursery", "gallery", "pantry", "porch", "loft", "yard"
					};
					for (int i = 0; i < rooms.Length; i++)
						Assert.Equal(AppendStatus.Committed, Digihouse.InitializeRoom(source, rooms[i], null).Status);

					for (int i = 0; i < rooms.Length; i++)
					{
						int moves = rooms[i] == "parlor" ? 400 : 40;
						for (int m = 0; m < moves; m++)
							Assert.Equal(AppendStatus.Committed, Digihouse.Move(source, rooms[i], "chair", m, 0, m, null).Status);
					}

					Assert.True(Preserver.Preserve(source, backup, null, null).Published);
					Assert.Equal(AppendStatus.Committed, Digihouse.Move(source, "parlor", "chair", 400, 1, 400, null).Status);
					PreserveResult suffix = Preserver.Preserve(source, backup, null, null);
					Assert.True(suffix.Published);
					string parlor = Digihouse.RoomStoryId("parlor");
					long oldGeneration = new FileInfo(LibraryLayout.Generation(backup.Root, parlor, 0)).Length;
					Assert.Equal(AppendStatus.Committed, Digihouse.CompactMoves(source, "parlor", null).Status);
					long newGeneration = new FileInfo(source.PublishedStoryPath(parlor)).Length;
					int newRecords = 0;
					EditionView compacted = source.ReadCurrent();
					for (int i = 0; i < compacted.Stories.Count; i++)
					{
						if (compacted.Stories[i].StoryId == parlor)
							newRecords = compacted.Stories[i].RecordCount;
					}

					PreserveResult after = Preserver.Preserve(source, backup, null, null);
					Assert.True(after.Published);
					long manifestBytes = new FileInfo(LibraryLayout.Manifest(source.Root, after.EditionId)).Length;
					long oldStillThere = new FileInfo(LibraryLayout.Generation(backup.Root, parlor, 0)).Length;
					string report = "Digihouse house: 12 rooms, 400 parlor moves plus one more, 40 moves in each other room. No photo blobs.\n"
						+ "old parlor generation bytes still on the backup\t" + oldGeneration.ToString(CultureInfo.InvariantCulture) + "\n"
						+ "one-move suffix bytes copied\t" + suffix.BytesCopied.ToString(CultureInfo.InvariantCulture) + "\n"
						+ "new parlor generation bytes\t" + newGeneration.ToString(CultureInfo.InvariantCulture) + "\n"
						+ "new parlor record count\t" + newRecords.ToString(CultureInfo.InvariantCulture) + "\n"
						+ "bytes copied by the preserve after compaction\t" + after.BytesCopied.ToString(CultureInfo.InvariantCulture) + "\n"
						+ "new edition manifest bytes, not included in the copy count\t" + manifestBytes.ToString(CultureInfo.InvariantCulture) + "\n"
						+ "old generation bytes on the backup after that preserve\t" + oldStillThere.ToString(CultureInfo.InvariantCulture) + "\n";
					_output.WriteLine(report);
					File.WriteAllText(Evidence("round3-compaction.txt"), report);
					Assert.Equal(newGeneration, after.BytesCopied);
					Assert.True(newGeneration < oldGeneration);
					Assert.True(after.BytesCopied > suffix.BytesCopied);
					Assert.Equal(oldGeneration, oldStillThere);
					Assert.Equal(CheckResult.Pass, backup.Verify(after.EditionId, null).Integrity);
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(backupPath);
			}
		}

		#region private functions
		private static void SeedKept(string path, BatchFormat format)
		{
			using (Library library = Library.Create(path, format))
				AppendText(library, "durability", "kept", "kept");
		}

		private static string AppendText(Library library, string storyId, string text, string key)
		{
			RecordDraft draft = new RecordDraft(RetentionClass.Stone, LabCode.FromText(text));
			StoryAppend story = new StoryAppend(storyId, library.HighWater(storyId), new RecordDraft[] { draft });
			AppendRequest request = new AppendRequest(key, library.CurrentEditionId, new StoryAppend[] { story });
			AppendResult result = library.Append(request, null);
			Assert.Equal(AppendStatus.Committed, result.Status);
			return result.EditionId;
		}

		private static string AppendText(JournalLibrary library, string storyId, string text, string key)
		{
			AppendResult result = AppendExisting(library, storyId, text, key, library.HighWater(storyId));
			Assert.Equal(AppendStatus.Committed, result.Status);
			return result.EditionId;
		}

		private static AppendResult AppendExisting(JournalLibrary library, string storyId, string text, string key, long offset)
		{
			RecordDraft draft = new RecordDraft(RetentionClass.Stone, LabCode.FromText(text));
			StoryAppend story = new StoryAppend(storyId, offset, new RecordDraft[] { draft });
			AppendRequest request = new AppendRequest(key, library.CurrentEditionId, new StoryAppend[] { story });
			return library.Append(request, null);
		}

		private static void SeedManifest(Library library, int target, ref int have)
		{
			while (have < target)
			{
				int batch = Math.Min(1000, target - have);
				List<StoryAppend> stories = new List<StoryAppend>();
				for (int i = 0; i < batch; i++)
				{
					int index = have + i;
					RecordDraft draft = new RecordDraft(RetentionClass.Stone, LabCode.FromText("open-" + index.ToString(CultureInfo.InvariantCulture)));
					stories.Add(new StoryAppend("bench/" + index.ToString("D5", CultureInfo.InvariantCulture), 0, new RecordDraft[] { draft }));
				}

				AppendRequest request = new AppendRequest("seed-m-" + have.ToString(CultureInfo.InvariantCulture), library.CurrentEditionId, stories);
				Assert.Equal(AppendStatus.Committed, library.Append(request, null).Status);
				have += batch;
			}
		}

		private static void SeedJournal(JournalLibrary library, int target, ref int have)
		{
			while (have < target)
			{
				int batch = Math.Min(1000, target - have);
				List<StoryAppend> stories = new List<StoryAppend>();
				for (int i = 0; i < batch; i++)
				{
					int index = have + i;
					RecordDraft draft = new RecordDraft(RetentionClass.Stone, LabCode.FromText("open-" + index.ToString(CultureInfo.InvariantCulture)));
					stories.Add(new StoryAppend("bench/" + index.ToString("D5", CultureInfo.InvariantCulture), 0, new RecordDraft[] { draft }));
				}

				AppendRequest request = new AppendRequest("seed-j-" + have.ToString(CultureInfo.InvariantCulture), library.CurrentEditionId, stories);
				Assert.Equal(AppendStatus.Committed, library.Append(request, null).Status);
				have += batch;
			}
		}

		private static double TimeManifest(Library library, int size)
		{
			RecordDraft draft = new RecordDraft(RetentionClass.Sand, LabCode.FromText("move-" + size.ToString(CultureInfo.InvariantCulture)));
			StoryAppend story = new StoryAppend("bench/00000", library.HighWater("bench/00000"), new RecordDraft[] { draft });
			AppendRequest request = new AppendRequest("move-m-" + size.ToString(CultureInfo.InvariantCulture), library.CurrentEditionId, new StoryAppend[] { story });
			Stopwatch watch = Stopwatch.StartNew();
			AppendResult result = library.Append(request, null);
			watch.Stop();
			Assert.Equal(AppendStatus.Committed, result.Status);
			return watch.Elapsed.TotalMilliseconds;
		}

		private static double TimeJournal(JournalLibrary library, int size)
		{
			RecordDraft draft = new RecordDraft(RetentionClass.Sand, LabCode.FromText("move-" + size.ToString(CultureInfo.InvariantCulture)));
			StoryAppend story = new StoryAppend("bench/00000", library.HighWater("bench/00000"), new RecordDraft[] { draft });
			AppendRequest request = new AppendRequest("move-j-" + size.ToString(CultureInfo.InvariantCulture), library.CurrentEditionId, new StoryAppend[] { story });
			Stopwatch watch = Stopwatch.StartNew();
			AppendResult result = library.Append(request, null);
			watch.Stop();
			Assert.Equal(AppendStatus.Committed, result.Status);
			return watch.Elapsed.TotalMilliseconds;
		}

		private static List<string> HeadTexts(string root)
		{
			return TextsOf(PublishedHead.ReadStory(root, "durability"));
		}

		private static List<string> RawTexts(string root)
		{
			return RawTexts(root, "durability");
		}

		private static List<string> RawTexts(string root, string storyId)
		{
			string path = LibraryLayout.Generation(root, storyId, 0);
			return TextsOf(StoryLog.Read(path).Committed);
		}

		private static List<string> EditionTexts(EditionView edition)
		{
			return TextsOf(edition.Records);
		}

		private static List<string> TextsOf(IReadOnlyList<StoredRecord> records)
		{
			List<string> texts = new List<string>();
			for (int i = 0; i < records.Count; i++)
				texts.Add(LabCode.ToText(records[i].Body));

			return texts;
		}

		private static List<string> TextsOf(IReadOnlyList<HeardRecord> records)
		{
			List<string> texts = new List<string>();
			for (int i = 0; i < records.Count; i++)
				texts.Add(records[i].Text);

			return texts;
		}

		private static int CountText(IReadOnlyList<StoredRecord> records, string text)
		{
			int count = 0;
			for (int i = 0; i < records.Count; i++)
			{
				if (LabCode.ToText(records[i].Body) == text)
					count++;
			}

			return count;
		}

		private static void AssertNever(PublishedListener listener, string text)
		{
			IReadOnlyList<HeardRecord> heard = listener.Snapshot();
			for (int i = 0; i < heard.Count; i++)
				Assert.NotEqual(text, heard[i].Text);
		}

		private static double Median(List<double> values)
		{
			values.Sort();
			return values[values.Count / 2];
		}

		private static CrashRun Run(params string[] args)
		{
			using (Process process = CrashClient.Start(args))
			{
				string error = process.StandardError.ReadToEnd();
				process.WaitForExit();
				CrashRun run = new CrashRun();
				run.ExitCode = process.ExitCode;
				run.Error = error;
				return run;
			}
		}

		private static string Evidence(string name)
		{
			string path = Path.GetFullPath(Path.Combine(
				AppContext.BaseDirectory,
				"..", "..", "..", "..", "..",
				"evidence",
				name
			));
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			return path;
		}
		#endregion
	}
}
