using BigRedProf.Data.Core;

namespace BigRedProf.Stories.LibrarySemanticsLab.Tests
{
	public class PreservationAndRetentionTests
	{
		[Fact]
		public void Lifecycle_SurvivesBackupCompactionAndSourceLoss()
		{
			string sourcePath = LabPaths.Create(nameof(Lifecycle_SurvivesBackupCompactionAndSourceLoss));
			string backupPath = LabPaths.Create(nameof(Lifecycle_SurvivesBackupCompactionAndSourceLoss) + "-backup");
			string restoredPath = LabPaths.Create(nameof(Lifecycle_SurvivesBackupCompactionAndSourceLoss) + "-restored");
			try
			{
				string edition;
				string rendered;
				using (Library source = Library.Create(sourcePath, BatchFormat.LengthPrefixed))
				{
					Assert.Equal(AppendStatus.Committed, Digihouse.InitializeRoom(source, "parlor", null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.InitializeRoom(source, "kitchen", null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.Purchase(source, "chair", "ada", null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.Transfer(source, "chair", "ada", "bea", null).Status);
					Code photo = new Code("1011001");
					Assert.Equal(AppendStatus.Committed, Digihouse.AttachPhoto(source, "chair", photo, null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.Move(source, "parlor", "chair", 1, 1, 1, null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.Move(source, "parlor", "chair", 2, 1, 2, null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.Move(source, "parlor", "chair", 4, 2, 3, null).Status);
					edition = source.CurrentEditionId;
					rendered = Digihouse.Read(source, edition).Render();
					using (Library backup = Library.Create(backupPath, BatchFormat.LengthPrefixed))
					{
						PreserveResult preserved = Preserver.Preserve(source, backup, null, null);
						Assert.True(preserved.Published);
						Assert.Equal(edition, preserved.EditionId);
					}
				}

				Directory.Delete(sourcePath, true);
				Assert.False(Directory.Exists(sourcePath));
				using (Library backup = Library.Open(backupPath))
				using (Library restored = Library.Create(restoredPath, BatchFormat.LengthPrefixed))
				{
					RestoreResult restore = Preserver.Restore(backup, edition, restored, AcceptSand());
					Assert.True(restore.Restored);
					Assert.Contains("accepted resurrection", restore.Detail);
					Assert.Equal(edition, restored.CurrentEditionId);
					Assert.Equal(rendered, Digihouse.Read(restored, restored.CurrentEditionId).Render());
					VerificationReport report = restored.Verify(
						restored.CurrentEditionId,
						view => Digihouse.Read(restored, view.Id).Render() == rendered
					);
					Assert.Equal(CheckResult.Pass, report.Integrity);
					Assert.Equal(CheckResult.Pass, report.Completeness);
					Assert.Equal(CheckResult.Pass, report.RetentionCompliance);
					Assert.Equal(CheckResult.Pass, report.ApplicationReconstructed);
					Assert.True(report.IsPublished);
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(backupPath);
				LabPaths.Delete(restoredPath);
			}
		}

		[Fact]
		public void IncrementalPreserve_CopiesOnlyTheNewSuffix()
		{
			string sourcePath = LabPaths.Create(nameof(IncrementalPreserve_CopiesOnlyTheNewSuffix));
			string backupPath = LabPaths.Create(nameof(IncrementalPreserve_CopiesOnlyTheNewSuffix) + "-backup");
			try
			{
				using (Library source = Library.Create(sourcePath, BatchFormat.LengthPrefixed))
				using (Library backup = Library.Create(backupPath, BatchFormat.LengthPrefixed))
				{
					Assert.Equal(AppendStatus.Committed, Digihouse.Purchase(source, "chair", "ada", null).Status);
					PreserveResult first = Preserver.Preserve(source, backup, null, null);
					Assert.True(first.Published);
					long full = first.BytesCopied;
					Assert.Equal(AppendStatus.Committed, Digihouse.Purchase(source, "lamp", "bea", null).Status);
					PreserveResult second = Preserver.Preserve(source, backup, null, null);
					Assert.True(second.Published);
					Assert.True(second.BytesCopied > 0);
					Assert.True(second.BytesCopied < full);
					Assert.Equal(source.CurrentEditionId, backup.CurrentEditionId);
					Assert.Equal(
						Digihouse.Read(source, source.CurrentEditionId).Render(),
						Digihouse.Read(backup, backup.CurrentEditionId).Render()
					);
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(backupPath);
			}
		}

		[Fact]
		public void StoneHistory_SurvivesSandDeletionAndCompaction()
		{
			string path = LabPaths.Create(nameof(StoneHistory_SurvivesSandDeletionAndCompaction));
			try
			{
				using (Library library = Library.Create(path, BatchFormat.LengthPrefixed))
				{
					Digihouse.InitializeRoom(library, "parlor", null);
					Digihouse.Purchase(library, "chair", "ada", null);
					Digihouse.Transfer(library, "chair", "ada", "bea", null);
					string ownershipBefore = Describe(library.ReadCommitted(Digihouse.OwnershipStoryId));
					Code photo = LabCode.FromText("jpeg-bytes");
					Digihouse.AttachPhoto(library, "chair", photo, null);
					Digihouse.Move(library, "parlor", "chair", 1, 1, 1, null);
					Digihouse.Move(library, "parlor", "chair", 2, 2, 2, null);
					Digihouse.Move(library, "parlor", "chair", 3, 3, 3, null);
					string initBefore = LabCode.ToText(library.ReadCommitted(Digihouse.RoomStoryId("parlor"))[0].Body);
					Assert.Equal(AppendStatus.Committed, Digihouse.CompactMoves(library, "parlor", null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.ReleasePhoto(library, "chair", null).Status);
					Assert.Equal(ownershipBefore, Describe(library.ReadCommitted(Digihouse.OwnershipStoryId)));
					Assert.Equal(initBefore, LabCode.ToText(library.ReadCommitted(Digihouse.RoomStoryId("parlor"))[0].Body));
					Assert.False(File.Exists(library.ContentFilePath(Library.ContentIdFor(photo))));
					DigihouseState state = Digihouse.Read(library, library.CurrentEditionId);
					Assert.Equal("bea", state.Owners["chair"]);
					Assert.StartsWith("erased:", state.Photos["chair"]);
					Assert.Equal("3,3", state.Positions["parlor/chair"]);
					VerificationReport report = library.Verify(library.CurrentEditionId, null);
					Assert.Equal(CheckResult.Pass, report.Integrity);
					Assert.Equal(CheckResult.Pass, report.RetentionCompliance);
					Assert.Equal(CheckResult.Pass, report.Completeness);
					AppendResult replay = Digihouse.Move(library, "parlor", "chair", 1, 1, 1, null);
					Assert.Equal(AppendStatus.AlreadyCommitted, replay.Status);
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void PinnedBackup_KeepsBytesThatCompactionRemoves()
		{
			string sourcePath = LabPaths.Create(nameof(PinnedBackup_KeepsBytesThatCompactionRemoves));
			string backupPath = LabPaths.Create(nameof(PinnedBackup_KeepsBytesThatCompactionRemoves) + "-backup");
			try
			{
				using (Library source = Library.Create(sourcePath, BatchFormat.LengthPrefixed))
				using (Library backup = Library.Create(backupPath, BatchFormat.LengthPrefixed))
				{
					Digihouse.InitializeRoom(source, "parlor", null);
					Digihouse.Purchase(source, "chair", "ada", null);
					Code photo = new Code("1111000");
					Digihouse.AttachPhoto(source, "chair", photo, null);
					Digihouse.Move(source, "parlor", "chair", 1, 1, 1, null);
					Digihouse.Move(source, "parlor", "chair", 8, 9, 2, null);
					EditionPin pin = source.Pin();
					string pinned = pin.EditionId;
					FaultInjector faults = new FaultInjector();
					faults.Before = delegate(string point)
					{
						if (point.StartsWith("after-story-copied:", StringComparison.Ordinal))
						{
							Digihouse.CompactMoves(source, "parlor", null);
							Digihouse.ReleasePhoto(source, "chair", null);
						}
					};
					PreserveResult preserved = Preserver.Preserve(source, backup, faults, pin);
					Assert.True(preserved.Published);
					Assert.Equal(pinned, backup.CurrentEditionId);
					Assert.NotEqual(pinned, source.CurrentEditionId);
					Assert.False(File.Exists(source.ContentFilePath(Library.ContentIdFor(photo))));
					Assert.True(File.Exists(backup.ContentFilePath(Library.ContentIdFor(photo))));
					Assert.Equal("8,9", Digihouse.Read(backup, pinned).Positions["parlor/chair"]);
					Assert.Equal(CheckResult.Pass, backup.Verify(pinned, null).Integrity);
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(backupPath);
			}
		}

		[Fact]
		public void InterruptedIncrementalPreserve_KeepsThePreviousBlobs()
		{
			string sourcePath = LabPaths.Create(nameof(InterruptedIncrementalPreserve_KeepsThePreviousBlobs));
			string backupPath = LabPaths.Create(nameof(InterruptedIncrementalPreserve_KeepsThePreviousBlobs) + "-backup");
			try
			{
				Library source = Library.Create(sourcePath, BatchFormat.LengthPrefixed);
				Library backup = Library.Create(backupPath, BatchFormat.LengthPrefixed);
				Code photo = new Code("110011");
				Digihouse.AttachPhoto(source, "chair", photo, null);
				PreserveResult first = Preserver.Preserve(source, backup, null, null);
				Assert.True(first.Published);
				Assert.Equal(AppendStatus.Committed, Digihouse.ReleasePhoto(source, "chair", null).Status);
				FaultInjector faults = new FaultInjector();
				faults.ArmedPoint = FaultPoint.BeforeDestinationPointer;
				Assert.Throws<CrashInjectedException>(() => Preserver.Preserve(source, backup, faults, null));
				backup.Dispose();
				source.Dispose();
				using (Library reopened = Library.Open(backupPath))
				{
					Assert.Equal(first.EditionId, reopened.CurrentEditionId);
					Assert.True(File.Exists(reopened.ContentFilePath(Library.ContentIdFor(photo))));
					VerificationReport report = reopened.Verify(first.EditionId, null);
					Assert.Equal(CheckResult.Pass, report.Completeness);
					Assert.Equal(CheckResult.Pass, report.Integrity);
					Assert.True(report.IsPublished);
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(backupPath);
			}
		}

		[Fact]
		public void UnpinnedBackup_FailsWhenCompactionWinsTheRace()
		{
			string sourcePath = LabPaths.Create(nameof(UnpinnedBackup_FailsWhenCompactionWinsTheRace));
			string backupPath = LabPaths.Create(nameof(UnpinnedBackup_FailsWhenCompactionWinsTheRace) + "-backup");
			try
			{
				using (Library source = Library.Create(sourcePath, BatchFormat.LengthPrefixed))
				using (Library backup = Library.Create(backupPath, BatchFormat.LengthPrefixed))
				{
					Digihouse.InitializeRoom(source, "parlor", null);
					Code photo = new Code("1010101");
					Digihouse.AttachPhoto(source, "chair", photo, null);
					Digihouse.Move(source, "parlor", "chair", 1, 1, 1, null);
					Digihouse.Move(source, "parlor", "chair", 2, 2, 2, null);
					PreserveResult first = Preserver.Preserve(source, backup, null, null);
					Assert.True(first.Published);
					string preservedEdition = first.EditionId;
					Digihouse.Move(source, "parlor", "chair", 3, 3, 3, null);
					FaultInjector faults = new FaultInjector();
					faults.Before = delegate(string point)
					{
						if (point == "after-story-copied:digihouse/catalog")
						{
							Digihouse.CompactMoves(source, "parlor", null);
							Digihouse.ReleasePhoto(source, "chair", null);
						}
					};
					PreserveResult result = Preserver.Preserve(source, backup, faults, null);
					Assert.True(result.Failed);
					Assert.False(result.Published);
					Assert.Equal(preservedEdition, backup.CurrentEditionId);
					Assert.True(File.Exists(backup.ContentFilePath(Library.ContentIdFor(photo))));
					VerificationReport report = backup.Verify(preservedEdition, null);
					Assert.Equal(CheckResult.Pass, report.Integrity);
					Assert.Equal(CheckResult.Pass, report.Completeness);
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(backupPath);
			}
		}

		[Fact]
		public void RestoreOlderThanDeletion_BringsSandBack_UnlessCertificatesAreSupplied()
		{
			string sourcePath = LabPaths.Create(nameof(RestoreOlderThanDeletion_BringsSandBack_UnlessCertificatesAreSupplied));
			string oldBackupPath = LabPaths.Create(nameof(RestoreOlderThanDeletion_BringsSandBack_UnlessCertificatesAreSupplied) + "-old");
			string newBackupPath = LabPaths.Create(nameof(RestoreOlderThanDeletion_BringsSandBack_UnlessCertificatesAreSupplied) + "-new");
			string exactPath = LabPaths.Create(nameof(RestoreOlderThanDeletion_BringsSandBack_UnlessCertificatesAreSupplied) + "-exact");
			string appliedPath = LabPaths.Create(nameof(RestoreOlderThanDeletion_BringsSandBack_UnlessCertificatesAreSupplied) + "-applied");
			string refusedPath = LabPaths.Create(nameof(RestoreOlderThanDeletion_BringsSandBack_UnlessCertificatesAreSupplied) + "-refused");
			try
			{
				string oldEdition;
				string newEdition;
				using (Library source = Library.Create(sourcePath, BatchFormat.LengthPrefixed))
				{
					Digihouse.InitializeRoom(source, "parlor", null);
					Digihouse.Purchase(source, "chair", "ada", null);
					Code photo = new Code("1100110");
					Digihouse.AttachPhoto(source, "chair", photo, null);
					Digihouse.Move(source, "parlor", "chair", 1, 1, 1, null);
					Digihouse.Move(source, "parlor", "chair", 2, 2, 2, null);
					Digihouse.Move(source, "parlor", "chair", 3, 3, 3, null);
					oldEdition = source.CurrentEditionId;
					using (Library oldBackup = Library.Create(oldBackupPath, BatchFormat.LengthPrefixed))
						Preserver.Preserve(source, oldBackup, null, null);

					Digihouse.Purchase(source, "lamp", "bea", null);
					Digihouse.CompactMoves(source, "parlor", null);
					Digihouse.ReleasePhoto(source, "chair", null);
					newEdition = source.CurrentEditionId;
					using (Library newBackup = Library.Create(newBackupPath, BatchFormat.LengthPrefixed))
						Preserver.Preserve(source, newBackup, null, null);
				}

				Directory.Delete(sourcePath, true);
				using (Library oldBackup = Library.Open(oldBackupPath))
				using (Library newBackup = Library.Open(newBackupPath))
				using (Library exact = Library.Create(exactPath, BatchFormat.LengthPrefixed))
				using (Library applied = Library.Create(appliedPath, BatchFormat.LengthPrefixed))
				using (Library refused = Library.Create(refusedPath, BatchFormat.LengthPrefixed))
				{
					RestoreResult exactResult = Preserver.Restore(oldBackup, oldEdition, exact, AcceptSand());
					Assert.True(exactResult.Restored);
					Assert.False(exactResult.Refused);
					Assert.Contains("present again", exactResult.Detail);
					Assert.Contains("move|parlor|chair|1|1", Texts(exact, Digihouse.RoomStoryId("parlor")));
					Assert.Contains("move|parlor|chair|3|3", Texts(exact, Digihouse.RoomStoryId("parlor")));
					Assert.False(Digihouse.Read(exact, exact.CurrentEditionId).Owners.ContainsKey("lamp"));
					string photoId = Digihouse.Read(exact, exact.CurrentEditionId).Photos["chair"];
					Assert.True(File.Exists(exact.ContentFilePath(photoId)));

					RestoreOptions refusedOptions = new RestoreOptions();
					refusedOptions.PreventResurrection = true;
					RestoreResult refusedResult = Preserver.Restore(oldBackup, oldEdition, refused, refusedOptions);
					Assert.True(refusedResult.Refused);
					Assert.Equal(ManifestText.EmptyId(), refused.CurrentEditionId);

					RestoreOptions applyOptions = new RestoreOptions();
					applyOptions.PreventResurrection = true;
					applyOptions.NewerEvidence = newBackup;
					applyOptions.NewerEditionId = newEdition;
					RestoreResult appliedResult = Preserver.Restore(oldBackup, oldEdition, applied, applyOptions);
					Assert.True(appliedResult.Restored);
					Assert.True(appliedResult.ErasuresApplied);
					Assert.Contains("not a roll-forward", appliedResult.Detail);
					DigihouseState state = Digihouse.Read(applied, applied.CurrentEditionId);
					Assert.Equal("ada", state.Owners["chair"]);
					Assert.False(state.Owners.ContainsKey("lamp"));
					Assert.StartsWith("erased:", state.Photos["chair"]);
					Assert.Equal("3,3", state.Positions["parlor/chair"]);
					Assert.False(File.Exists(applied.ContentFilePath(photoId)));
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(oldBackupPath);
				LabPaths.Delete(newBackupPath);
				LabPaths.Delete(exactPath);
				LabPaths.Delete(appliedPath);
				LabPaths.Delete(refusedPath);
			}
		}

		[Fact]
		public void SecondRestore_WithNewerEvidence_ErasesSandAlreadyRestored()
		{
			string sourcePath = LabPaths.Create(nameof(SecondRestore_WithNewerEvidence_ErasesSandAlreadyRestored));
			string oldBackupPath = LabPaths.Create(nameof(SecondRestore_WithNewerEvidence_ErasesSandAlreadyRestored) + "-old");
			string newBackupPath = LabPaths.Create(nameof(SecondRestore_WithNewerEvidence_ErasesSandAlreadyRestored) + "-new");
			string destinationPath = LabPaths.Create(nameof(SecondRestore_WithNewerEvidence_ErasesSandAlreadyRestored) + "-dest");
			try
			{
				string oldEdition;
				string newEdition;
				using (Library source = Library.Create(sourcePath, BatchFormat.PerFrame))
				{
					Assert.Equal(AppendStatus.Committed, Digihouse.InitializeRoom(source, "parlor", null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.Purchase(source, "chair", "ada", null).Status);
					Code photo = new Code("1100110");
					Assert.Equal(AppendStatus.Committed, Digihouse.AttachPhoto(source, "chair", photo, null).Status);
					oldEdition = source.CurrentEditionId;
					using (Library oldBackup = Library.Create(oldBackupPath, BatchFormat.PerFrame))
						Assert.True(Preserver.Preserve(source, oldBackup, null, null).Published);

					Assert.Equal(AppendStatus.Committed, Digihouse.ReleasePhoto(source, "chair", null).Status);
					newEdition = source.CurrentEditionId;
					using (Library newBackup = Library.Create(newBackupPath, BatchFormat.PerFrame))
						Assert.True(Preserver.Preserve(source, newBackup, null, null).Published);
				}

				using (Library oldBackup = Library.Open(oldBackupPath))
				using (Library newBackup = Library.Open(newBackupPath))
				using (Library destination = Library.Create(destinationPath, BatchFormat.PerFrame))
				{
					RestoreResult first = Preserver.Restore(oldBackup, oldEdition, destination, AcceptSand());
					Assert.True(first.Restored);
					string photoId = Digihouse.Read(destination, destination.CurrentEditionId).Photos["chair"];
					Assert.True(File.Exists(destination.ContentFilePath(photoId)));
					RestoreOptions again = new RestoreOptions();
					again.PreventResurrection = true;
					again.NewerEvidence = newBackup;
					again.NewerEditionId = newEdition;
					RestoreResult second = Preserver.Restore(oldBackup, oldEdition, destination, again);
					Assert.True(second.Restored);
					Assert.True(second.ErasuresApplied);
					Assert.False(second.Refused);
					Assert.StartsWith("erased:", Digihouse.Read(destination, destination.CurrentEditionId).Photos["chair"]);
					Assert.False(File.Exists(destination.ContentFilePath(photoId)));
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(oldBackupPath);
				LabPaths.Delete(newBackupPath);
				LabPaths.Delete(destinationPath);
			}
		}

		[Fact]
		public void IncrementalPreserve_DoesNotReplaceAValidBlobWithCorruptSourceBytes()
		{
			string sourcePath = LabPaths.Create(nameof(IncrementalPreserve_DoesNotReplaceAValidBlobWithCorruptSourceBytes));
			string backupPath = LabPaths.Create(nameof(IncrementalPreserve_DoesNotReplaceAValidBlobWithCorruptSourceBytes) + "-backup");
			try
			{
				string photoId;
				byte[] original;
				using (Library source = Library.Create(sourcePath, BatchFormat.PerFrame))
				using (Library backup = Library.Create(backupPath, BatchFormat.PerFrame))
				{
					Assert.Equal(AppendStatus.Committed, Digihouse.InitializeRoom(source, "parlor", null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.Purchase(source, "chair", "ada", null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.AttachPhoto(source, "chair", new Code("1100110"), null).Status);
					Assert.True(Preserver.Preserve(source, backup, null, null).Published);
					photoId = Digihouse.Read(backup, backup.CurrentEditionId).Photos["chair"];
					original = File.ReadAllBytes(backup.ContentFilePath(photoId));
					Assert.Equal(AppendStatus.Committed, Digihouse.Purchase(source, "lamp", "bea", null).Status);
					File.WriteAllBytes(source.ContentFilePath(photoId), new byte[] { 9, 9, 9 });
					PreserveResult copied = Preserver.Preserve(source, backup, null, null);
					Assert.True(copied.Failed);
					Assert.False(copied.Published);
					Assert.Equal(original, File.ReadAllBytes(backup.ContentFilePath(photoId)));
					Assert.Equal(CheckResult.Pass, backup.Verify(backup.CurrentEditionId, null).Integrity);
					Assert.False(Digihouse.Read(backup, backup.CurrentEditionId).Owners.ContainsKey("lamp"));
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(backupPath);
			}
		}

		[Fact]
		public void SecondRestore_ResumesErasuresAfterTheFirstCertificate()
		{
			string sourcePath = LabPaths.Create(nameof(SecondRestore_ResumesErasuresAfterTheFirstCertificate));
			string backupPath = LabPaths.Create(nameof(SecondRestore_ResumesErasuresAfterTheFirstCertificate) + "-backup");
			string destinationPath = LabPaths.Create(nameof(SecondRestore_ResumesErasuresAfterTheFirstCertificate) + "-dest");
			try
			{
				string oldEdition;
				string newEdition;
				using (Library source = Library.Create(sourcePath, BatchFormat.PerFrame))
				{
					Assert.Equal(AppendStatus.Committed, Digihouse.InitializeRoom(source, "parlor", null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.Purchase(source, "chair", "ada", null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.Purchase(source, "lamp", "bea", null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.AttachPhoto(source, "chair", new Code("1100110"), null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.AttachPhoto(source, "lamp", new Code("1010101"), null).Status);
					oldEdition = source.CurrentEditionId;
					using (Library backup = Library.Create(backupPath, BatchFormat.PerFrame))
						Assert.True(Preserver.Preserve(source, backup, null, null).Published);

					Assert.Equal(AppendStatus.Committed, Digihouse.ReleasePhoto(source, "chair", null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.ReleasePhoto(source, "lamp", null).Status);
					newEdition = source.CurrentEditionId;
				}

				using (Library backup = Library.Open(backupPath))
				using (Library source = Library.Open(sourcePath))
				using (Library destination = Library.Create(destinationPath, BatchFormat.PerFrame))
				{
					RestoreResult first = Preserver.Restore(backup, oldEdition, destination, AcceptSand());
					Assert.True(first.Restored);
					Assert.Equal(AppendStatus.Committed, Digihouse.ReleasePhoto(destination, "chair", null).Status);
					RestoreOptions again = new RestoreOptions();
					again.PreventResurrection = true;
					again.NewerEvidence = source;
					again.NewerEditionId = newEdition;
					RestoreResult second = Preserver.Restore(backup, oldEdition, destination, again);
					Assert.True(second.Restored);
					Assert.True(second.ErasuresApplied);
					Assert.False(second.Refused);
					DigihouseState state = Digihouse.Read(destination, destination.CurrentEditionId);
					Assert.StartsWith("erased:", state.Photos["chair"]);
					Assert.StartsWith("erased:", state.Photos["lamp"]);
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(backupPath);
				LabPaths.Delete(destinationPath);
			}
		}

		[Fact]
		public void Restore_CompactionCertificate_DropsMovesTheOldEditionHas()
		{
			string sourcePath = LabPaths.Create(nameof(Restore_CompactionCertificate_DropsMovesTheOldEditionHas));
			string backupPath = LabPaths.Create(nameof(Restore_CompactionCertificate_DropsMovesTheOldEditionHas) + "-backup");
			string destinationPath = LabPaths.Create(nameof(Restore_CompactionCertificate_DropsMovesTheOldEditionHas) + "-dest");
			try
			{
				string oldEdition;
				string newEdition;
				using (Library source = Library.Create(sourcePath, BatchFormat.PerFrame))
				using (Library backup = Library.Create(backupPath, BatchFormat.PerFrame))
				using (Library destination = Library.Create(destinationPath, BatchFormat.PerFrame))
				{
					Assert.Equal(AppendStatus.Committed, Digihouse.InitializeRoom(source, "parlor", null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.Move(source, "parlor", "chair", 1, 1, 1, null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.Move(source, "parlor", "chair", 2, 2, 2, null).Status);
					oldEdition = source.CurrentEditionId;
					Assert.True(Preserver.Preserve(source, backup, null, null).Published);
					Assert.Equal(AppendStatus.Committed, Digihouse.Move(source, "parlor", "chair", 3, 3, 3, null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.CompactMoves(source, "parlor", null).Status);
					newEdition = source.CurrentEditionId;
					RestoreOptions options = new RestoreOptions();
					options.PreventResurrection = true;
					options.NewerEvidence = source;
					options.NewerEditionId = newEdition;
					RestoreResult restored = Preserver.Restore(backup, oldEdition, destination, options);
					Assert.True(restored.Restored);
					Assert.True(restored.ErasuresApplied);
					Assert.False(restored.Refused);
					string room = Texts(destination, Digihouse.RoomStoryId("parlor"));
					Assert.DoesNotContain("move|parlor|chair|1|1", room);
					Assert.DoesNotContain("move|parlor|chair|2|2", room);
					Assert.Contains("move-summary|parlor|chair", room);
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(backupPath);
				LabPaths.Delete(destinationPath);
			}
		}

		[Fact]
		public void SharedSandClaim_DoesNotDropBytesAnotherClaimNeeds()
		{
			string path = LabPaths.Create(nameof(SharedSandClaim_DoesNotDropBytesAnotherClaimNeeds));
			try
			{
				using (Library library = Library.Create(path, BatchFormat.LengthPrefixed))
				{
					Code model = new Code("101111");
					string contentId = Library.ContentIdFor(model);
					AppendResult first = AppendClaim(library, "room-a", "model-a", contentId, model);
					Assert.Equal(AppendStatus.Committed, first.Status);
					AppendResult second = AppendClaim(library, "room-b", "model-b", contentId, model);
					Assert.Equal(AppendStatus.Committed, second.Status);
					AppendResult releaseFirst = library.ReleaseClaim("room-a", 0, "drop-a", "drop-a", library.CurrentEditionId, null);
					Assert.Equal(AppendStatus.Committed, releaseFirst.Status);
					Assert.True(File.Exists(library.ContentFilePath(contentId)));
					AppendResult releaseSecond = library.ReleaseClaim("room-b", 0, "drop-b", "drop-b", library.CurrentEditionId, null);
					Assert.Equal(AppendStatus.Committed, releaseSecond.Status);
					Assert.False(File.Exists(library.ContentFilePath(contentId)));
					Assert.Equal(CheckResult.Pass, library.Verify(library.CurrentEditionId, null).RetentionCompliance);
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void Promote_BlocksRelease_AndStoneCompactionIsRejected()
		{
			string path = LabPaths.Create(nameof(Promote_BlocksRelease_AndStoneCompactionIsRejected));
			try
			{
				using (Library library = Library.Create(path, BatchFormat.LengthPrefixed))
				{
					Digihouse.InitializeRoom(library, "parlor", null);
					Digihouse.Move(library, "parlor", "chair", 1, 1, 1, null);
					AppendResult compacted = library.CompactSand(
						Digihouse.RoomStoryId("parlor"),
						0,
						1,
						"nope",
						"nope",
						"bad-compact",
						library.CurrentEditionId,
						null
					);
					Assert.Equal(AppendStatus.Rejected, compacted.Status);
					Assert.Contains("stone", compacted.Detail);
					Code photo = new Code("1000001");
					Digihouse.AttachPhoto(library, "chair", photo, null);
					long photoOffset = library.ReadCommitted(Digihouse.MediaStoryId)[0].Offset;
					Assert.Equal(
						AppendStatus.Committed,
						library.PromoteClaim(Digihouse.MediaStoryId, photoOffset, "keep", "promote-chair", library.CurrentEditionId, null).Status
					);
					AppendResult release = Digihouse.ReleasePhoto(library, "chair", null);
					Assert.Equal(AppendStatus.Rejected, release.Status);
					Assert.True(File.Exists(library.ContentFilePath(Library.ContentIdFor(photo))));
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void GapWithoutCertificate_FailsRetention_WhileAFlippedByteFailsIntegrity()
		{
			string path = LabPaths.Create(nameof(GapWithoutCertificate_FailsRetention_WhileAFlippedByteFailsIntegrity));
			try
			{
				using (Library library = Library.Create(path, BatchFormat.LengthPrefixed))
				{
					Digihouse.InitializeRoom(library, "parlor", null);
					Digihouse.Move(library, "parlor", "chair", 1, 1, 1, null);
					Digihouse.Move(library, "parlor", "chair", 2, 2, 2, null);
					Digihouse.Move(library, "parlor", "chair", 3, 3, 3, null);
					library.DebugDropSandWithoutCertificate(Digihouse.RoomStoryId("parlor"), 3);
					VerificationReport gap = library.Verify(library.CurrentEditionId, null);
					Assert.Equal(CheckResult.Pass, gap.Integrity);
					Assert.Equal(CheckResult.Fail, gap.RetentionCompliance);
					Assert.Contains(gap.Findings, finding => finding.Contains("without a certificate", StringComparison.Ordinal));
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void FlippedStoryByte_FailsIntegrityAndIsNotReportedAsAGap()
		{
			string path = LabPaths.Create(nameof(FlippedStoryByte_FailsIntegrityAndIsNotReportedAsAGap));
			try
			{
				Library library = Library.Create(path, BatchFormat.LengthPrefixed);
				Digihouse.Purchase(library, "chair", "ada", null);
				string storyPath = library.PublishedStoryPath(Digihouse.OwnershipStoryId);
				library.Dispose();
				byte[] bytes = File.ReadAllBytes(storyPath);
				bytes[bytes.Length / 2] ^= 0x01;
				File.WriteAllBytes(storyPath, bytes);
				using (Library reopened = Library.Open(path))
				{
					VerificationReport report = reopened.Verify(reopened.CurrentEditionId, null);
					Assert.Equal(CheckResult.Fail, report.Integrity);
					Assert.DoesNotContain(report.Findings, finding => finding.Contains("without a certificate", StringComparison.Ordinal));
					Assert.Throws<InvalidDataException>(() => reopened.ReadEdition(reopened.CurrentEditionId));
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void InterruptedPreserve_DoesNotBecomeTheCurrentEdition()
		{
			string sourcePath = LabPaths.Create(nameof(InterruptedPreserve_DoesNotBecomeTheCurrentEdition));
			string backupPath = LabPaths.Create(nameof(InterruptedPreserve_DoesNotBecomeTheCurrentEdition) + "-backup");
			try
			{
				Library source = Library.Create(sourcePath, BatchFormat.LengthPrefixed);
				Library backup = Library.Create(backupPath, BatchFormat.LengthPrefixed);
				Digihouse.Purchase(source, "chair", "ada", null);
				string empty = backup.CurrentEditionId;
				FaultInjector faults = new FaultInjector();
				faults.ArmedPoint = FaultPoint.BeforeDestinationPointer;
				Assert.Throws<CrashInjectedException>(() => Preserver.Preserve(source, backup, faults, null));
				backup.Dispose();
				source.Dispose();
				using (Library reopened = Library.Open(backupPath))
				{
					Assert.Equal(empty, reopened.CurrentEditionId);
					VerificationReport report = reopened.Verify(empty, null);
					Assert.True(report.IsPublished);
					Assert.Equal(CheckResult.Pass, report.Integrity);
				}

				using (Library sourceAgain = Library.Open(sourcePath))
				using (Library backupAgain = Library.Open(backupPath))
				{
					PreserveResult second = Preserver.Preserve(sourceAgain, backupAgain, null, null);
					Assert.True(second.Published);
					Assert.Equal(sourceAgain.CurrentEditionId, backupAgain.CurrentEditionId);
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(backupPath);
			}
		}

		[Fact]
		public void LostPreserveAck_SecondCallDoesNotCopyAgain()
		{
			string sourcePath = LabPaths.Create(nameof(LostPreserveAck_SecondCallDoesNotCopyAgain));
			string backupPath = LabPaths.Create(nameof(LostPreserveAck_SecondCallDoesNotCopyAgain) + "-backup");
			try
			{
				Library source = Library.Create(sourcePath, BatchFormat.LengthPrefixed);
				Library backup = Library.Create(backupPath, BatchFormat.LengthPrefixed);
				Digihouse.Purchase(source, "chair", "ada", null);
				FaultInjector faults = new FaultInjector();
				faults.ArmedPoint = FaultPoint.AfterDestinationPointer;
				Assert.Throws<CrashInjectedException>(() => Preserver.Preserve(source, backup, faults, null));
				backup.Dispose();
				using (Library backupAgain = Library.Open(backupPath))
				{
					Assert.Equal(source.CurrentEditionId, backupAgain.CurrentEditionId);
					PreserveResult second = Preserver.Preserve(source, backupAgain, null, null);
					Assert.True(second.AlreadyPreserved);
					Assert.Equal(0, second.BytesCopied);
				}

				source.Dispose();
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(backupPath);
			}
		}

		[Fact]
		public void OccupiedUnrelatedLibrary_IsRefused()
		{
			string sourcePath = LabPaths.Create(nameof(OccupiedUnrelatedLibrary_IsRefused));
			string otherPath = LabPaths.Create(nameof(OccupiedUnrelatedLibrary_IsRefused) + "-other");
			try
			{
				using (Library source = Library.Create(sourcePath, BatchFormat.LengthPrefixed))
				using (Library other = Library.Create(otherPath, BatchFormat.LengthPrefixed))
				{
					Digihouse.Purchase(source, "chair", "ada", null);
					Digihouse.Purchase(other, "lamp", "bea", null);
					string otherEdition = other.CurrentEditionId;
					PreserveResult result = Preserver.Preserve(source, other, null, null);
					Assert.True(result.Failed);
					Assert.Equal(otherEdition, other.CurrentEditionId);
					Assert.Contains("not an ancestor", result.Detail);
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(otherPath);
			}
		}

		[Fact]
		public void EmptyStory_IsPreserved()
		{
			string sourcePath = LabPaths.Create(nameof(EmptyStory_IsPreserved));
			string backupPath = LabPaths.Create(nameof(EmptyStory_IsPreserved) + "-backup");
			try
			{
				using (Library source = Library.Create(sourcePath, BatchFormat.LengthPrefixed))
				using (Library backup = Library.Create(backupPath, BatchFormat.LengthPrefixed))
				{
					RecordDraft declaration = new RecordDraft(RetentionClass.Stone, LabCode.FromText("room|attic"));
					declaration.DeclaresStoryId = Digihouse.RoomStoryId("attic");
					StoryAppend story = new StoryAppend(
						Digihouse.CatalogStoryId,
						0,
						new RecordDraft[] { declaration }
					);
					AppendRequest request = new AppendRequest("declare-attic", source.CurrentEditionId, new StoryAppend[] { story });
					Assert.Equal(AppendStatus.Committed, source.Append(request, null).Status);
					Assert.True(Preserver.Preserve(source, backup, null, null).Published);
					StoryPrefix? attic = null;
					EditionView edition = backup.ReadCurrent();
					for (int i = 0; i < edition.Stories.Count; i++)
					{
						if (edition.Stories[i].StoryId == Digihouse.RoomStoryId("attic"))
							attic = edition.Stories[i];
					}

					Assert.NotNull(attic);
					Assert.Equal(0, attic!.RecordCount);
					Assert.Equal(0, attic.HighWater);
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(backupPath);
			}
		}

		[Fact]
		public void MissingBlob_DoesNotPublishACompleteBackup()
		{
			string sourcePath = LabPaths.Create(nameof(MissingBlob_DoesNotPublishACompleteBackup));
			string backupPath = LabPaths.Create(nameof(MissingBlob_DoesNotPublishACompleteBackup) + "-backup");
			try
			{
				using (Library source = Library.Create(sourcePath, BatchFormat.LengthPrefixed))
				using (Library backup = Library.Create(backupPath, BatchFormat.LengthPrefixed))
				{
					Code photo = new Code("1111");
					Digihouse.AttachPhoto(source, "chair", photo, null);
					File.Delete(source.ContentFilePath(Library.ContentIdFor(photo)));
					PreserveResult result = Preserver.Preserve(source, backup, null, null);
					Assert.True(result.Failed);
					Assert.Equal(ManifestText.EmptyId(), backup.CurrentEditionId);
					VerificationReport report = source.Verify(source.CurrentEditionId, null);
					Assert.Equal(CheckResult.Fail, report.Completeness);
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(backupPath);
			}
		}

		[Fact]
		public void TapeCopy_CanTearAcrossStoriesAndOmitsContent()
		{
			string path = LabPaths.Create(nameof(TapeCopy_CanTearAcrossStoriesAndOmitsContent));
			string tapePath = Path.Combine(path, "tape.txt");
			try
			{
				using (Library library = Library.Create(path, BatchFormat.LengthPrefixed))
				{
					Digihouse.InitializeRoom(library, "parlor", null);
					Code photo = new Code("101");
					Digihouse.AttachPhoto(library, "chair", photo, null);
					FaultInjector faults = new FaultInjector();
					faults.Before = delegate(string point)
					{
						if (point == "before-story:" + Digihouse.RoomStoryId("parlor"))
						{
							RecordDraft catalog = new RecordDraft(RetentionClass.Stone, LabCode.FromText("room|extra"));
							catalog.DeclaresStoryId = Digihouse.RoomStoryId("extra");
							RecordDraft room = new RecordDraft(RetentionClass.Stone, LabCode.FromText("init|extra|seed"));
							AppendRequest request = new AppendRequest(
								"tear",
								library.CurrentEditionId,
								new StoryAppend[]
								{
									new StoryAppend(Digihouse.CatalogStoryId, library.HighWater(Digihouse.CatalogStoryId), new RecordDraft[] { catalog }),
									new StoryAppend(Digihouse.RoomStoryId("parlor"), library.HighWater(Digihouse.RoomStoryId("parlor")), new RecordDraft[] { room })
								}
							);
							library.Append(request, null);
						}
					};
					TapeCopy tape = TapeSeriesBackup.CopyHeads(library, tapePath, faults);
					Assert.False(tape.MatchesStartEdition);
					Assert.Contains("no single edition", tape.Detail);
					string tapeText = File.ReadAllText(tapePath);
					Assert.DoesNotContain("content", tapeText);
					Assert.False(tapeText.Contains(Library.ContentIdFor(photo), StringComparison.Ordinal));
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void FieldNotes_UsesTheSameBackupContract()
		{
			string sourcePath = LabPaths.Create(nameof(FieldNotes_UsesTheSameBackupContract));
			string backupPath = LabPaths.Create(nameof(FieldNotes_UsesTheSameBackupContract) + "-backup");
			string restoredPath = LabPaths.Create(nameof(FieldNotes_UsesTheSameBackupContract) + "-restored");
			try
			{
				string rendered;
				string edition;
				Code photo = LabCode.FromText("specimen-photo");
				using (Library source = Library.Create(sourcePath, BatchFormat.LengthPrefixed))
				{
					Assert.Equal(AppendStatus.Committed, FieldNotes.Accession(source, "fern-1", "ada", null).Status);
					Assert.Equal(AppendStatus.Committed, FieldNotes.AttachPhoto(source, "fern-1", photo, null).Status);
					Assert.Equal(AppendStatus.Committed, FieldNotes.Track(source, "fern-1", "47.6", "-122.3", 1, null).Status);
					Assert.Equal(AppendStatus.Committed, FieldNotes.Track(source, "fern-1", "47.7", "-122.4", 2, null).Status);
					Assert.Equal(AppendStatus.Committed, FieldNotes.CompactTrack(source, "fern-1", null).Status);
					edition = source.CurrentEditionId;
					rendered = FieldNotes.Read(source, edition).Render();
					Assert.Contains("ada", rendered);
					Assert.Contains("2 points", rendered);
					using (Library backup = Library.Create(backupPath, BatchFormat.LengthPrefixed))
						Assert.True(Preserver.Preserve(source, backup, null, null).Published);
				}

				Directory.Delete(sourcePath, true);
				using (Library backup = Library.Open(backupPath))
				using (Library restored = Library.Create(restoredPath, BatchFormat.LengthPrefixed))
				{
					Assert.True(Preserver.Restore(backup, edition, restored, AcceptSand()).Restored);
					Assert.Equal(rendered, FieldNotes.Read(restored, restored.CurrentEditionId).Render());
					Assert.Equal(photo, ContentFiles.Read(restored.ContentFilePath(Library.ContentIdFor(photo))));
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(backupPath);
				LabPaths.Delete(restoredPath);
			}
		}

		#region private functions
		private static RestoreOptions AcceptSand()
		{
			RestoreOptions options = new RestoreOptions();
			options.AcceptResurrection = true;
			return options;
		}

		private static string Texts(Library library, string storyId)
		{
			List<string> lines = new List<string>();
			IReadOnlyList<StoredRecord> records = library.ReadCommitted(storyId);
			for (int i = 0; i < records.Count; i++)
				lines.Add(LabCode.ToText(records[i].Body));

			return string.Join("\n", lines);
		}

		private static string Describe(IReadOnlyList<StoredRecord> records)
		{
			List<string> lines = new List<string>();
			for (int i = 0; i < records.Count; i++)
				lines.Add(records[i].Offset.ToString() + ":" + LabCode.Describe(records[i].Body));

			return string.Join("\n", lines);
		}

		private static AppendResult AppendClaim(Library library, string storyId, string key, string contentId, Code body)
		{
			RecordDraft draft = new RecordDraft(RetentionClass.Sand, LabCode.FromText(key));
			draft.ContentId = contentId;
			StoryAppend story = new StoryAppend(storyId, library.HighWater(storyId), new RecordDraft[] { draft });
			AppendRequest request = new AppendRequest(key, library.CurrentEditionId, new StoryAppend[] { story });
			request.Content = new ContentWrite[] { new ContentWrite(contentId, body) };
			return library.Append(request, null);
		}
		#endregion
	}
}
