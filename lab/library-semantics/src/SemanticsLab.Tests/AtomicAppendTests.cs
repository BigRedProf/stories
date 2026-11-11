using BigRedProf.Data.Core;

namespace BigRedProf.Stories.LibrarySemanticsLab.Tests
{
	public class AtomicAppendTests
	{
		[Fact]
		public void LengthPrefixed_TornWrite_DropsTheWholeBatch()
		{
			string path = LabPaths.Create(nameof(LengthPrefixed_TornWrite_DropsTheWholeBatch));
			try
			{
				Library library = Library.Create(path, BatchFormat.LengthPrefixed);
				RecordDraft kept = new RecordDraft(RetentionClass.Stone, LabCode.FromText("kept"));
				StoryAppend keptStory = new StoryAppend("room", 0, new RecordDraft[] { kept });
				AppendRequest keptRequest = new AppendRequest("kept", library.CurrentEditionId, new StoryAppend[] { keptStory });
				Assert.Equal(AppendStatus.Committed, library.Append(keptRequest, null).Status);
				string published = library.CurrentEditionId;
				FaultInjector faults = new FaultInjector();
				faults.TearWrite = true;
				faults.ArmedPoint = FaultPoint.DuringFrameWrite;
				RecordDraft[] batch = new RecordDraft[]
				{
					new RecordDraft(RetentionClass.Stone, LabCode.FromText("seed")),
					new RecordDraft(RetentionClass.Stone, LabCode.FromText("ready"))
				};
				StoryAppend batchStory = new StoryAppend("room", 1, batch);
				AppendRequest batchRequest = new AppendRequest("init", published, new StoryAppend[] { batchStory });
				Assert.Throws<CrashInjectedException>(() => library.Append(batchRequest, faults));
				library.Dispose();

				using (Library reopened = Library.Open(path))
				{
					Assert.Equal(published, reopened.CurrentEditionId);
					IReadOnlyList<StoredRecord> records = reopened.ReadCommitted("room");
					Assert.Single(records);
					Assert.Equal("kept", LabCode.ToText(records[0].Body));
					AppendResult retry = reopened.Append(batchRequest, null);
					Assert.Equal(AppendStatus.Committed, retry.Status);
					Assert.Equal(1, retry.FramesFlushed);
					records = reopened.ReadCommitted("room");
					Assert.Equal(3, records.Count);
					Assert.Equal("seed", LabCode.ToText(records[1].Body));
					Assert.Equal("ready", LabCode.ToText(records[2].Body));
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void LostAck_AfterAnotherCommit_ReturnsTheOriginalEdition()
		{
			string path = LabPaths.Create(nameof(LostAck_AfterAnotherCommit_ReturnsTheOriginalEdition));
			try
			{
				using (Library library = Library.Create(path, BatchFormat.LengthPrefixed))
				{
					string expected = library.CurrentEditionId;
					AppendResult first = Digihouse.Purchase(library, "chair", "ada", null);
					Assert.Equal(AppendStatus.Committed, first.Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.Purchase(library, "lamp", "bea", null).Status);
					RecordDraft draft = new RecordDraft(RetentionClass.Stone, LabCode.FromText("purchase|chair|ada"));
					StoryAppend story = new StoryAppend(Digihouse.OwnershipStoryId, 0, new RecordDraft[] { draft });
					AppendRequest retry = new AppendRequest("purchase:chair:ada", expected, new StoryAppend[] { story });
					AppendResult result = library.Append(retry, null);
					Assert.Equal(AppendStatus.AlreadyCommitted, result.Status);
					Assert.Equal(first.EditionId, result.EditionId);
					Assert.NotEqual(library.CurrentEditionId, result.EditionId);
					Assert.Single(library.ReadEdition(first.EditionId).Records);
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void CommitMarker_CrashBeforeMarker_HidesBytesANaiveReaderWouldKeep()
		{
			string path = LabPaths.Create(nameof(CommitMarker_CrashBeforeMarker_HidesBytesANaiveReaderWouldKeep));
			try
			{
				FaultInjector faults = new FaultInjector();
				faults.ArmedPoint = FaultPoint.AfterDataBeforeMarker;
				Library library = Library.Create(path, BatchFormat.CommitMarker);
				Assert.Throws<CrashInjectedException>(() => Digihouse.InitializeRoom(library, "parlor", faults));
				library.Dispose();

				using (Library reopened = Library.Open(path))
				{
					Assert.Empty(reopened.ReadCommitted(Digihouse.CatalogStoryId));
					Assert.NotEmpty(reopened.ReadPhysical(Digihouse.CatalogStoryId));
					AppendResult retry = Digihouse.InitializeRoom(reopened, "parlor", null);
					Assert.Equal(AppendStatus.Committed, retry.Status);
					Assert.Equal(5, retry.FramesFlushed);
					Assert.Single(reopened.ReadCommitted(Digihouse.CatalogStoryId));
					Assert.Equal(2, reopened.ReadCommitted(Digihouse.RoomStoryId("parlor")).Count);
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void CommitMarker_CrashAfterFramesBeforePublish_RetriesWithoutDuplicating()
		{
			string path = LabPaths.Create(nameof(CommitMarker_CrashAfterFramesBeforePublish_RetriesWithoutDuplicating));
			try
			{
				FaultInjector faults = new FaultInjector();
				faults.ArmedPoint = FaultPoint.AfterFrameBeforePublish;
				Library library = Library.Create(path, BatchFormat.CommitMarker);
				Assert.Throws<CrashInjectedException>(() => Digihouse.InitializeRoom(library, "parlor", faults));
				library.Dispose();

				using (Library reopened = Library.Open(path))
				{
					Assert.Equal(2, reopened.ReadCommitted(Digihouse.RoomStoryId("parlor")).Count);
					Assert.DoesNotContain(reopened.ReadCurrent().Stories, story => story.StoryId == Digihouse.RoomStoryId("parlor"));
					AppendResult retry = Digihouse.InitializeRoom(reopened, "parlor", null);
					Assert.Equal(AppendStatus.Committed, retry.Status);
					Assert.Equal(0, retry.FramesFlushed);
					Assert.Equal(2, reopened.ReadCommitted(Digihouse.RoomStoryId("parlor")).Count);
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void PerFrame_CrashAfterFirstRecord_IsVisibleOnTheStoryHeadOnly()
		{
			string path = LabPaths.Create(nameof(PerFrame_CrashAfterFirstRecord_IsVisibleOnTheStoryHeadOnly));
			try
			{
				FaultInjector faults = new FaultInjector();
				faults.ArmedPoint = FaultPoint.AfterRecord(0);
				Library library = Library.Create(path, BatchFormat.PerFrame);
				string before = library.CurrentEditionId;
				Assert.Throws<CrashInjectedException>(() => Digihouse.InitializeRoom(library, "parlor", faults));
				library.Dispose();

				using (Library reopened = Library.Open(path))
				{
					Assert.Equal(before, reopened.CurrentEditionId);
					Assert.Single(reopened.ReadCommitted(Digihouse.CatalogStoryId));
					AppendResult retry = Digihouse.InitializeRoom(reopened, "parlor", null);
					Assert.Equal(AppendStatus.Committed, retry.Status);
					Assert.Equal(3, retry.FramesFlushed);
					Assert.Single(reopened.ReadCommitted(Digihouse.CatalogStoryId));
					Assert.Contains("parlor", Digihouse.Read(reopened, reopened.CurrentEditionId).RoomsReady);
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void TwoWriters_SameExpectedEdition_OnlyOneCommits()
		{
			string path = LabPaths.Create(nameof(TwoWriters_SameExpectedEdition_OnlyOneCommits));
			try
			{
				using (Library library = Library.Create(path, BatchFormat.LengthPrefixed))
				{
					string expected = library.CurrentEditionId;
					Barrier barrier = new Barrier(2);
					AppendResult[] results = new AppendResult[2];
					Thread[] threads = new Thread[2];
					for (int i = 0; i < 2; i++)
					{
						int writer = i;
						threads[i] = new Thread(delegate()
						{
							barrier.SignalAndWait();
							RecordDraft draft = new RecordDraft(
								RetentionClass.Stone,
								LabCode.FromText("purchase|chair|writer" + writer.ToString())
							);
							StoryAppend story = new StoryAppend(Digihouse.OwnershipStoryId, 0, new RecordDraft[] { draft });
							AppendRequest request = new AppendRequest(
								"purchase:chair:writer" + writer.ToString(),
								expected,
								new StoryAppend[] { story }
							);
							results[writer] = library.Append(request, null);
						});
					}

					threads[0].Start();
					threads[1].Start();
					threads[0].Join();
					threads[1].Join();
					int committed = 0;
					int conflict = 0;
					for (int i = 0; i < 2; i++)
					{
						if (results[i].Status == AppendStatus.Committed)
							committed++;
						if (results[i].Status == AppendStatus.PositionConflict)
							conflict++;
					}

					Assert.Equal(1, committed);
					Assert.Equal(1, conflict);
					Assert.Single(library.ReadCommitted(Digihouse.OwnershipStoryId));
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void SecondOpen_IsRejectedWhileTheWriterLockIsHeld()
		{
			string path = LabPaths.Create(nameof(SecondOpen_IsRejectedWhileTheWriterLockIsHeld));
			try
			{
				using (Library library = Library.Create(path, BatchFormat.LengthPrefixed))
				{
					IOException exception = Assert.Throws<IOException>(() => Library.Open(path));
					Assert.Contains("already has a writer", exception.Message);
					Assert.Equal(ManifestText.EmptyId(), library.CurrentEditionId);
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void LostAck_RetryWithThePreCommitEdition_DoesNotDuplicate()
		{
			string path = LabPaths.Create(nameof(LostAck_RetryWithThePreCommitEdition_DoesNotDuplicate));
			try
			{
				Library library = Library.Create(path, BatchFormat.LengthPrefixed);
				string expected = library.CurrentEditionId;
				FaultInjector faults = new FaultInjector();
				faults.ArmedPoint = FaultPoint.AfterPublishBeforeAck;
				Assert.Throws<CrashInjectedException>(() => Digihouse.Purchase(library, "chair", "ada", faults));
				library.Dispose();

				using (Library reopened = Library.Open(path))
				{
					Assert.Equal("ada", Digihouse.Read(reopened, reopened.CurrentEditionId).Owners["chair"]);
					RecordDraft draft = new RecordDraft(RetentionClass.Stone, LabCode.FromText("purchase|chair|ada"));
					StoryAppend story = new StoryAppend(Digihouse.OwnershipStoryId, 0, new RecordDraft[] { draft });
					AppendRequest retry = new AppendRequest(
						"purchase:chair:ada",
						expected,
						new StoryAppend[] { story }
					);
					AppendResult result = reopened.Append(retry, null);
					Assert.Equal(AppendStatus.AlreadyCommitted, result.Status);
					Assert.Single(reopened.ReadCommitted(Digihouse.OwnershipStoryId));
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void LostAck_DifferentBody_Conflicts()
		{
			string path = LabPaths.Create(nameof(LostAck_DifferentBody_Conflicts));
			try
			{
				Library library = Library.Create(path, BatchFormat.LengthPrefixed);
				FaultInjector faults = new FaultInjector();
				faults.ArmedPoint = FaultPoint.AfterPublishBeforeAck;
				Assert.Throws<CrashInjectedException>(() => Digihouse.Purchase(library, "chair", "ada", faults));
				library.Dispose();

				using (Library reopened = Library.Open(path))
				{
					RecordDraft draft = new RecordDraft(RetentionClass.Stone, LabCode.FromText("purchase|chair|bea"));
					StoryAppend story = new StoryAppend(Digihouse.OwnershipStoryId, 0, new RecordDraft[] { draft });
					AppendRequest retry = new AppendRequest("purchase:chair:ada", reopened.CurrentEditionId, new StoryAppend[] { story });
					AppendResult result = reopened.Append(retry, null);
					Assert.Equal(AppendStatus.IdempotencyConflict, result.Status);
					Assert.Equal("ada", Digihouse.Read(reopened, reopened.CurrentEditionId).Owners["chair"]);
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void TimeoutAfterCommit_IsNotARollback()
		{
			string path = LabPaths.Create(nameof(TimeoutAfterCommit_IsNotARollback));
			try
			{
				Library library = Library.Create(path, BatchFormat.LengthPrefixed);
				FaultInjector faults = new FaultInjector();
				faults.ArmedPoint = FaultPoint.AfterPublishBeforeAck;
				Assert.Throws<CrashInjectedException>(() => Digihouse.Purchase(library, "chair", "ada", faults));
				library.Dispose();

				using (Library reopened = Library.Open(path))
				{
					Assert.Equal("ada", Digihouse.Read(reopened, reopened.CurrentEditionId).Owners["chair"]);
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void SkippingTheOffsetCheck_LetsBothWritersLand()
		{
			string path = LabPaths.Create(nameof(SkippingTheOffsetCheck_LetsBothWritersLand));
			try
			{
				using (Library library = Library.Create(path, BatchFormat.LengthPrefixed))
				{
					for (int i = 0; i < 2; i++)
					{
						RecordDraft draft = new RecordDraft(
							RetentionClass.Stone,
							LabCode.FromText("purchase|chair|writer" + i.ToString())
						);
						StoryAppend story = new StoryAppend(Digihouse.OwnershipStoryId, 0, new RecordDraft[] { draft });
						AppendRequest request = new AppendRequest(
							"unchecked:" + i.ToString(),
							library.CurrentEditionId,
							new StoryAppend[] { story }
						);
						request.SkipExpectedOffsetCheck = true;
						AppendResult result = library.Append(request, null);
						Assert.Equal(AppendStatus.Committed, result.Status);
					}

					Assert.Equal(2, library.ReadCommitted(Digihouse.OwnershipStoryId).Count);
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void Codes_KeepBitLengthThroughARoundTrip()
		{
			string path = LabPaths.Create(nameof(Codes_KeepBitLengthThroughARoundTrip));
			string backupPath = LabPaths.Create(nameof(Codes_KeepBitLengthThroughARoundTrip) + "-backup");
			string restoredPath = LabPaths.Create(nameof(Codes_KeepBitLengthThroughARoundTrip) + "-restored");
			try
			{
				Code empty = new Code(0);
				Code midByte = new Code("10101");
				Code text = LabCode.FromText("chair");
				using (Library library = Library.Create(path, BatchFormat.LengthPrefixed))
				{
					RecordDraft[] drafts = new RecordDraft[]
					{
						new RecordDraft(RetentionClass.Stone, empty),
						new RecordDraft(RetentionClass.Stone, midByte),
						new RecordDraft(RetentionClass.Stone, text)
					};
					StoryAppend story = new StoryAppend("bits", 0, drafts);
					AppendRequest request = new AppendRequest("bits", library.CurrentEditionId, new StoryAppend[] { story });
					Assert.Equal(AppendStatus.Committed, library.Append(request, null).Status);
					using (Library backup = Library.Create(backupPath, BatchFormat.LengthPrefixed))
					{
						Assert.True(Preserver.Preserve(library, backup, null, null).Published);
					}
				}

				using (Library backup = Library.Open(backupPath))
				using (Library restored = Library.Create(restoredPath, BatchFormat.LengthPrefixed))
				{
					RestoreResult restore = Preserver.Restore(backup, backup.CurrentEditionId, restored, new RestoreOptions());
					Assert.True(restore.Restored);
					IReadOnlyList<StoredRecord> records = restored.ReadCommitted("bits");
					Assert.Equal(0, records[0].Body.Length);
					Assert.Equal(empty, records[0].Body);
					Assert.Equal(5, records[1].Body.Length);
					Assert.Equal(midByte, records[1].Body);
					Assert.Equal("chair", LabCode.ToText(records[2].Body));
					Assert.Equal(text, records[2].Body);
				}
			}
			finally
			{
				LabPaths.Delete(path);
				LabPaths.Delete(backupPath);
				LabPaths.Delete(restoredPath);
			}
		}
	}
}
