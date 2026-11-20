using BigRedProf.Data.Core;

namespace BigRedProf.Stories.LibrarySemanticsLab.Tests
{
	public class ExplicitFaultTests
	{
		[Fact]
		public void NamedFaults_HitTheOperationTheyAreArmedOn()
		{
			string sourcePath = LabPaths.Create(nameof(NamedFaults_HitTheOperationTheyAreArmedOn));
			string backupPath = LabPaths.Create(nameof(NamedFaults_HitTheOperationTheyAreArmedOn) + "-backup");
			try
			{
				Library library = Library.Create(sourcePath, BatchFormat.LengthPrefixed);
				Library backup = Library.Create(backupPath, BatchFormat.LengthPrefixed);
				try
				{
					Assert.Equal(AppendStatus.Committed, Digihouse.InitializeRoom(library, "parlor", null).Status);
					List<string> stone = new List<string>();
					Remember(library, stone);

					AssertThrew(delegate()
					{
						FaultInjector faults = Torn();
						Digihouse.Purchase(library, "chair", "ada", faults);
					});
					library.Dispose();
					library = Library.Open(sourcePath);
					Assert.Equal(AppendStatus.Committed, Digihouse.Purchase(library, "chair", "ada", null).Status);
					Remember(library, stone);

					string beforeMove = library.CurrentEditionId;
					AssertThrew(delegate()
					{
						FaultInjector faults = new FaultInjector();
						faults.ArmedPoint = FaultPoint.AfterManifestBeforePointer;
						Digihouse.Move(library, "parlor", "chair", 1, 0, 1, faults);
					});
					library.Dispose();
					library = Library.Open(sourcePath);
					Assert.Equal(beforeMove, library.CurrentEditionId);
					Assert.Equal(AppendStatus.Committed, Digihouse.Move(library, "parlor", "chair", 1, 0, 1, null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.Move(library, "parlor", "chair", 2, 0, 2, null).Status);
					Assert.Equal(AppendStatus.Committed, Digihouse.Move(library, "parlor", "chair", 3, 0, 3, null).Status);

					AssertThrew(delegate()
					{
						FaultInjector faults = new FaultInjector();
						faults.ArmedPoint = FaultPoint.AfterPublishBeforeAck;
						Digihouse.CompactMoves(library, "parlor", faults);
					});
					library.Dispose();
					library = Library.Open(sourcePath);
					Assert.Equal("3,0", Digihouse.Read(library, library.CurrentEditionId).Positions["parlor/chair"]);
					Assert.Equal(AppendStatus.AlreadyCommitted, Digihouse.Move(library, "parlor", "chair", 1, 0, 1, null).Status);

					Code photo = new Code("10110011");
					AssertThrew(delegate()
					{
						FaultInjector faults = new FaultInjector();
						faults.ArmedPoint = FaultPoint.AfterDirectoryFsync;
						Digihouse.AttachPhoto(library, "chair", photo, faults);
					});
					library.Dispose();
					library = Library.Open(sourcePath);
					Assert.Equal(AppendStatus.AlreadyCommitted, Digihouse.AttachPhoto(library, "chair", photo, null).Status);
					Assert.True(File.Exists(library.ContentFilePath(Library.ContentIdFor(photo))));

					Remember(library, stone);
					Check(library, stone, backup);
				}
				finally
				{
					library.Dispose();
					backup.Dispose();
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(backupPath);
			}
		}

		#region private functions
		private static FaultInjector Torn()
		{
			FaultInjector faults = new FaultInjector();
			faults.ArmedPoint = FaultPoint.DuringFrameWrite;
			faults.TearWrite = true;
			return faults;
		}

		private static void AssertThrew(Action action)
		{
			Assert.Throws<CrashInjectedException>(action);
		}

		private static void Remember(Library library, List<string> stone)
		{
			EditionView edition = library.ReadCurrent();
			for (int i = 0; i < edition.Records.Count; i++)
			{
				StoredRecord record = edition.Records[i];
				if (record.Retention != RetentionClass.Stone)
					continue;

				string identity = record.StoryId + ":" + record.Offset.ToString() + ":" + LabCode.Describe(record.Body);
				if (!stone.Contains(identity))
					stone.Add(identity);
			}
		}

		private static void Check(Library library, List<string> stone, Library backup)
		{
			VerificationReport report = library.Verify(library.CurrentEditionId, null);
			Assert.Equal(CheckResult.Pass, report.Integrity);
			Assert.Equal(CheckResult.Pass, report.Completeness);
			Assert.Equal(CheckResult.Pass, report.RetentionCompliance);
			EditionView edition = library.ReadCurrent();
			for (int i = 0; i < stone.Count; i++)
			{
				bool found = false;
				for (int r = 0; r < edition.Records.Count; r++)
				{
					StoredRecord record = edition.Records[r];
					string identity = record.StoryId + ":" + record.Offset.ToString() + ":" + LabCode.Describe(record.Body);
					if (identity == stone[i])
						found = true;
				}

				Assert.True(found, "Missing stone record " + stone[i]);
			}

			PreserveResult preserved = Preserver.Preserve(library, backup, null, null);
			Assert.True(preserved.Published);
		}
		#endregion
	}
}
