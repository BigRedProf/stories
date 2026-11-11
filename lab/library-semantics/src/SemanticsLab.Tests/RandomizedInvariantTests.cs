using Xunit.Abstractions;

namespace BigRedProf.Stories.LibrarySemanticsLab.Tests
{
	public class RandomizedInvariantTests
	{
		public const int Seed = 20261110;

		public RandomizedInvariantTests(ITestOutputHelper output)
		{
			_output = output;
		}

		private readonly ITestOutputHelper _output;

		[Fact]
		public void FixedSeed_KeepsStoneAndEditionInvariants()
		{
			_output.WriteLine("seed=" + Seed.ToString());
			string sourcePath = LabPaths.Create(nameof(FixedSeed_KeepsStoneAndEditionInvariants));
			string backupPath = LabPaths.Create(nameof(FixedSeed_KeepsStoneAndEditionInvariants) + "-backup");
			try
			{
				Random random = new Random(Seed);
				Library library = Library.Create(sourcePath, BatchFormat.LengthPrefixed);
				Library backup = Library.Create(backupPath, BatchFormat.LengthPrefixed);
				try
				{
					Assert.Equal(AppendStatus.Committed, Digihouse.InitializeRoom(library, "parlor", null).Status);
					List<string> stone = new List<string>();
					RememberStone(library, stone);
					int moves = 0;
					bool photo = false;
					for (int step = 0; step < 24; step++)
					{
						int roll = random.Next(6);
						string action = "step " + step.ToString() + " roll " + roll.ToString();
						FaultInjector? faults = null;
						if (random.Next(5) == 0)
						{
							faults = new FaultInjector();
							int which = random.Next(3);
							if (which == 0)
								faults.ArmedPoint = FaultPoint.AfterManifestBeforePointer;
							else if (which == 1)
								faults.ArmedPoint = FaultPoint.AfterPublishBeforeAck;
							else
							{
								faults.ArmedPoint = FaultPoint.DuringFrameWrite;
								faults.TearWrite = true;
							}

							action += " crash " + faults.ArmedPoint;
						}

						_output.WriteLine(action);
						int sequence = moves;
						if (roll == 2)
						{
							moves++;
							sequence = moves;
						}

						try
						{
							Run(library, roll, step, sequence, ref photo, faults);
						}
						catch (CrashInjectedException)
						{
							library.Dispose();
							library = Library.Open(sourcePath);
							Run(library, roll, step, sequence, ref photo, null);
						}

						Check(library, stone, backup);
					}

					_output.WriteLine("final edition " + library.CurrentEditionId);
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

		#region private methods
		private static void Run(Library library, int roll, int step, int sequence, ref bool photo, FaultInjector? faults)
		{
			if (roll == 0)
				Digihouse.Purchase(library, "good-" + step.ToString(), "ada", faults);
			else if (roll == 1)
				Digihouse.Transfer(library, "chair", "ada", "bea", faults);
			else if (roll == 2)
				Digihouse.Move(library, "parlor", "chair", step, 0, sequence, faults);
			else if (roll == 3 && sequence >= 3)
				Digihouse.CompactMoves(library, "parlor", faults);
			else if (roll == 4 && !photo)
			{
				Digihouse.AttachPhoto(library, "chair", new BigRedProf.Data.Core.Code("10110011"), faults);
				photo = true;
			}
			else if (roll == 5 && photo)
			{
				Digihouse.ReleasePhoto(library, "chair", faults);
				photo = false;
			}
		}

		private static void RememberStone(Library library, List<string> stone)
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
			RememberStone(library, stone);
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

			if (backup.CurrentEditionId == library.CurrentEditionId)
				return;

			PreserveResult preserved = Preserver.Preserve(library, backup, null, null);
			Assert.True(preserved.Published);
			Assert.Equal(library.CurrentEditionId, backup.CurrentEditionId);
		}
		#endregion
	}
}
