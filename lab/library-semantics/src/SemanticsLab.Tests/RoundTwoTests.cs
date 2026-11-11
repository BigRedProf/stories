using System.Diagnostics;
using BigRedProf.Data.Core;
using BigRedProf.Stories;
using BigRedProf.Stories.Data;
using BigRedProf.Stories.Disk;

namespace BigRedProf.Stories.LibrarySemanticsLab.Tests
{
	public class RoundTwoTests
	{
		[Fact]
		public void Publish_FsyncsTheDirectoryAfterThePointerRename()
		{
			string path = LabPaths.Create(nameof(Publish_FsyncsTheDirectoryAfterThePointerRename));
			try
			{
				using (Library library = Library.Create(path, BatchFormat.PerFrame))
				{
					int before = library.DirectorySyncCount;
					Assert.Equal(AppendStatus.Committed, Digihouse.Purchase(library, "chair", "ada", null).Status);
					Assert.True(library.DirectorySyncCount > before);
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void ExactRestoreOfSand_RequiresAnExplicitAccept()
		{
			string sourcePath = LabPaths.Create(nameof(ExactRestoreOfSand_RequiresAnExplicitAccept));
			string backupPath = LabPaths.Create(nameof(ExactRestoreOfSand_RequiresAnExplicitAccept) + "-backup");
			string refusedPath = LabPaths.Create(nameof(ExactRestoreOfSand_RequiresAnExplicitAccept) + "-refused");
			string acceptedPath = LabPaths.Create(nameof(ExactRestoreOfSand_RequiresAnExplicitAccept) + "-accepted");
			try
			{
				string edition;
				using (Library source = Library.Create(sourcePath, BatchFormat.PerFrame))
				{
					Digihouse.Purchase(source, "chair", "ada", null);
					Digihouse.Move(source, "parlor", "chair", 1, 2, 1, null);
					edition = source.CurrentEditionId;
					using (Library backup = Library.Create(backupPath, BatchFormat.PerFrame))
						Assert.True(Preserver.Preserve(source, backup, null, null).Published);
				}

				using (Library backup = Library.Open(backupPath))
				using (Library refused = Library.Create(refusedPath, BatchFormat.PerFrame))
				using (Library accepted = Library.Create(acceptedPath, BatchFormat.PerFrame))
				{
					RestoreResult without = Preserver.Restore(backup, edition, refused, new RestoreOptions());
					Assert.True(without.Refused);
					Assert.Contains("AcceptResurrection", without.Detail);
					Assert.Equal(ManifestText.EmptyId(), refused.CurrentEditionId);

					RestoreOptions options = new RestoreOptions();
					options.AcceptResurrection = true;
					RestoreResult with = Preserver.Restore(backup, edition, accepted, options);
					Assert.True(with.Restored);
					Assert.Contains("accepted resurrection", with.Detail);
					Assert.Equal("1,2", Digihouse.Read(accepted, accepted.CurrentEditionId).Positions["parlor/chair"]);
				}
			}
			finally
			{
				LabPaths.Delete(sourcePath);
				LabPaths.Delete(backupPath);
				LabPaths.Delete(refusedPath);
				LabPaths.Delete(acceptedPath);
			}
		}

		[Fact]
		public void PerFrame_EditionHidesThePrefix_AndTheNextWriterUsesThePublishedOffset()
		{
			string path = LabPaths.Create(nameof(PerFrame_EditionHidesThePrefix_AndTheNextWriterUsesThePublishedOffset));
			try
			{
				Library library = Library.Create(path, BatchFormat.PerFrame);
				string before = library.CurrentEditionId;
				FaultInjector faults = new FaultInjector();
				faults.ArmedPoint = FaultPoint.AfterRecord(0);
				RecordDraft[] drafts = new RecordDraft[]
				{
					new RecordDraft(RetentionClass.Stone, LabCode.FromText("seed")),
					new RecordDraft(RetentionClass.Stone, LabCode.FromText("ready"))
				};
				StoryAppend story = new StoryAppend("room", 0, drafts);
				AppendRequest request = new AppendRequest("init", before, new StoryAppend[] { story });
				Assert.Throws<CrashInjectedException>(() => library.Append(request, faults));
				library.Dispose();

				using (Library reopened = Library.Open(path))
				{
					Assert.Equal(before, reopened.CurrentEditionId);
					Assert.Equal("seed", LabCode.ToText(reopened.ReadCommitted("room")[0].Body));
					RecordDraft other = new RecordDraft(RetentionClass.Stone, LabCode.FromText("other"));
					StoryAppend otherStory = new StoryAppend("room", 0, new RecordDraft[] { other });
					AppendRequest otherRequest = new AppendRequest("other", before, new StoryAppend[] { otherStory });
					AppendResult result = reopened.Append(otherRequest, null);
					Assert.Equal(AppendStatus.Committed, result.Status);
					IReadOnlyList<StoredRecord> records = reopened.ReadCommitted("room");
					Assert.Single(records);
					Assert.Equal("other", LabCode.ToText(records[0].Body));
					AppendResult retry = reopened.Append(request, null);
					Assert.Equal(AppendStatus.PositionConflict, retry.Status);
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void ProcessKill_BeforeFlush_DropsTheUnflushedFrame()
		{
			string path = LabPaths.Create(nameof(ProcessKill_BeforeFlush_DropsTheUnflushedFrame));
			try
			{
				CrashRun run = CrashClient.Run(path, "length-prefixed", FaultPoint.DuringFrameWrite, 1, tear: true);
				Assert.NotEqual(0, run.ExitCode);
				using (Library library = Library.Open(path))
				{
					Assert.Equal(ManifestText.EmptyId(), library.CurrentEditionId);
					Assert.Empty(library.ReadCommitted("durability"));
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void ProcessKill_AfterDirectoryFsync_KeepsTheEdition()
		{
			string path = LabPaths.Create(nameof(ProcessKill_AfterDirectoryFsync_KeepsTheEdition));
			try
			{
				CrashRun run = CrashClient.Run(path, "per-frame", FaultPoint.AfterDirectoryFsync, 1, tear: false);
				Assert.NotEqual(0, run.ExitCode);
				using (Library library = Library.Open(path))
				{
					Assert.NotEqual(ManifestText.EmptyId(), library.CurrentEditionId);
					Assert.Equal("record-0", LabCode.ToText(library.ReadCommitted("durability")[0].Body));
					VerificationReport report = library.Verify(library.CurrentEditionId, null);
					Assert.Equal(CheckResult.Pass, report.Integrity);
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void ProcessKill_AfterFirstFlushedRecord_EditionStaysPut()
		{
			string path = LabPaths.Create(nameof(ProcessKill_AfterFirstFlushedRecord_EditionStaysPut));
			try
			{
				CrashRun run = CrashClient.Run(path, "per-frame", FaultPoint.AfterRecord(0), 2, tear: false);
				Assert.NotEqual(0, run.ExitCode);
				using (Library library = Library.Open(path))
				{
					Assert.Equal(ManifestText.EmptyId(), library.CurrentEditionId);
					IReadOnlyList<StoredRecord> head = library.ReadCommitted("durability");
					Assert.Single(head);
					Assert.Equal("record-0", LabCode.ToText(head[0].Body));
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void KilledHolder_ReleasesTheFileLock_APidFileWouldNot()
		{
			string path = LabPaths.Create(nameof(KilledHolder_ReleasesTheFileLock_APidFileWouldNot));
			string ready = Path.Combine(path, "ready");
			string pidFile = Path.Combine(path, "pid.lock");
			try
			{
				using (Library created = Library.Create(path, BatchFormat.PerFrame))
				{
				}

				Process holder = CrashClient.Start("hold-lock", path, ready);
				WaitForFile(ready);
				holder.Kill(entireProcessTree: true);
				holder.WaitForExit();
				using (Library reopened = Library.Open(path))
				{
					Assert.Equal(ManifestText.EmptyId(), reopened.CurrentEditionId);
				}

				File.Delete(ready);
				Process pid = CrashClient.Start("pidfile", pidFile, ready);
				WaitForFile(ready);
				pid.Kill(entireProcessTree: true);
				pid.WaitForExit();
				Assert.True(File.Exists(pidFile));
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void TwoProcesses_SameExpectedEdition_OneCommits()
		{
			string path = LabPaths.Create(nameof(TwoProcesses_SameExpectedEdition_OneCommits));
			string go = Path.Combine(path, "go");
			string leftResult = Path.Combine(path, "left.txt");
			string rightResult = Path.Combine(path, "right.txt");
			try
			{
				string edition;
				using (Library library = Library.Create(path, BatchFormat.PerFrame))
					edition = library.CurrentEditionId;

				Process left = CrashClient.Start("race-append", path, edition, "left", "left-body", leftResult, go);
				Process right = CrashClient.Start("race-append", path, edition, "right", "right-body", rightResult, go);
				Thread.Sleep(200);
				File.WriteAllText(go, "go");
				left.WaitForExit();
				right.WaitForExit();
				string leftStatus = File.ReadAllText(leftResult);
				string rightStatus = File.ReadAllText(rightResult);
				string winner = leftStatus == "Committed" ? "left" : "right";
				string loserStatus = winner == "left" ? rightStatus : leftStatus;
				Assert.Equal("Committed", winner == "left" ? leftStatus : rightStatus);
				Assert.Contains("already has a writer", loserStatus);

				string loserKey = winner == "left" ? "right" : "left";
				string loserBody = winner == "left" ? "right-body" : "left-body";
				string retryResult = Path.Combine(path, "retry.txt");
				File.Delete(go);
				Process retry = CrashClient.Start("race-append", path, edition, loserKey, loserBody, retryResult, go);
				Thread.Sleep(100);
				File.WriteAllText(go, "go");
				retry.WaitForExit();
				Assert.Equal("PositionConflict", File.ReadAllText(retryResult));
				using (Library library = Library.Open(path))
					Assert.Single(library.ReadCommitted("race"));
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void TwoProcesses_WithoutTheLock_DoNotGetSingleWinner()
		{
			string path = LabPaths.Create(nameof(TwoProcesses_WithoutTheLock_DoNotGetSingleWinner));
			string pause = Path.Combine(path, "pause");
			string go = Path.Combine(pause, "go");
			string leftResult = Path.Combine(path, "left.txt");
			string rightResult = Path.Combine(path, "right.txt");
			try
			{
				string edition;
				using (Library library = Library.Create(path, BatchFormat.PerFrame))
					edition = library.CurrentEditionId;

			string startGo = Path.Combine(path, "start");
			File.WriteAllText(startGo, "go");
			Dictionary<string, string> env = new Dictionary<string, string>();
			env["BRP_LAB_SKIP_WRITER_LOCK"] = "1";
			env["BRP_LAB_PAUSE_BEFORE_WRITE"] = pause;
			Process left = CrashClient.Start(env, "race-append", path, edition, "left", "left-body", leftResult, startGo);
			Process right = CrashClient.Start(env, "race-append", path, edition, "right", "right-body", rightResult, startGo);
				WaitForReadyCount(pause, 2);
				File.WriteAllText(go, "go");
				left.WaitForExit();
				right.WaitForExit();
				string leftStatus = File.Exists(leftResult) ? File.ReadAllText(leftResult) : "";
				string rightStatus = File.Exists(rightResult) ? File.ReadAllText(rightResult) : "";
				string both = leftStatus + "\n" + rightStatus;
				bool bothAcked = leftStatus == "Committed" && rightStatus == "Committed";
				bool sharingViolation = both.Contains("cannot access", StringComparison.Ordinal);
				Assert.DoesNotContain("PositionConflict", both);
				Assert.True(bothAcked || sharingViolation, both);
				try
				{
					using (Library library = Library.Open(path))
					{
						int count = library.ReadCommitted("race").Count;
						Assert.True(count >= 1);
					}
				}
				catch (InvalidDataException)
				{
					Assert.True(bothAcked || sharingViolation);
				}
			}
			finally
			{
				LabPaths.Delete(path);
			}
		}

		[Fact]
		public void ShippedStoryFile_IsAdoptedWithoutRewritingBytes()
		{
			string shipped = LabPaths.Create(nameof(ShippedStoryFile_IsAdoptedWithoutRewritingBytes) + "-shipped");
			string libraryPath = LabPaths.Create(nameof(ShippedStoryFile_IsAdoptedWithoutRewritingBytes) + "-library");
			string backupPath = LabPaths.Create(nameof(ShippedStoryFile_IsAdoptedWithoutRewritingBytes) + "-backup");
			string replay = LabPaths.Create(nameof(ShippedStoryFile_IsAdoptedWithoutRewritingBytes) + "-replay");
			try
			{
				TextTrail storyId = new TextTrail("catalog", "room");
				Code first = new Code("10110011");
				Code second = new Code("10101");
				IPiedPiper piedPiper = CreatePiedPiper();
				using (FileScribe scribe = new FileScribe(shipped, storyId, piedPiper))
					scribe.RecordSomething(first, second);

				string shippedFile = Directory.GetFiles(shipped, "*.story")[0];
				byte[] original = File.ReadAllBytes(shippedFile);
				using (Library library = Library.Create(libraryPath, BatchFormat.PerFrame))
				using (Library backup = Library.Create(backupPath, BatchFormat.PerFrame))
				{
					AppendResult adopted = library.AdoptShippedFile(
						"catalog/room",
						original,
						2,
						2,
						"adopt-room",
						library.CurrentEditionId
					);
					Assert.Equal(AppendStatus.Committed, adopted.Status);
					byte[] stored = File.ReadAllBytes(library.PublishedStoryPath("catalog/room"));
					Assert.Equal(original, stored);
					File.WriteAllBytes(Path.Combine(replay, Path.GetFileName(shippedFile)), stored);
					EditionView edition = library.ReadCurrent();
					StoryPrefix? prefix = null;
					for (int i = 0; i < edition.Stories.Count; i++)
					{
						if (edition.Stories[i].StoryId == "catalog/room")
							prefix = edition.Stories[i];
					}

					Assert.NotNull(prefix);
					Assert.Equal("shipped-v1", prefix!.Encoding);
					Assert.Equal(Hashing.Sha256(original), prefix.FileHash);
					Assert.Equal(AppendStatus.AlreadyCommitted, library.AdoptShippedFile(
						"catalog/room",
						original,
						2,
						2,
						"adopt-room",
						library.CurrentEditionId
					).Status);
					Assert.True(Preserver.Preserve(library, backup, null, null).Published);
					Assert.Equal(original, File.ReadAllBytes(backup.PublishedStoryPath("catalog/room")));
				}

				FileStoryteller storyteller = new FileStoryteller(replay, storyId, piedPiper);
				Assert.Equal(first, storyteller.TellMeSomething().Thing);
				Assert.Equal(second, storyteller.TellMeSomething().Thing);
				Assert.Equal(5, second.Length);
			}
			finally
			{
				LabPaths.Delete(shipped);
				LabPaths.Delete(libraryPath);
				LabPaths.Delete(backupPath);
				LabPaths.Delete(replay);
			}
		}

		[Fact]
		public void CodeBytes_MatchData_0_11_0_rc_1()
		{
			string project = Path.GetFullPath(Path.Combine(
				AppContext.BaseDirectory,
				"..", "..", "..", "..",
				"BitLayout11",
				"BitLayout11.csproj"
			));
			ProcessStartInfo info = new ProcessStartInfo();
			info.FileName = CrashClient.FindDotnet();
			info.ArgumentList.Add("run");
			info.ArgumentList.Add("--project");
			info.ArgumentList.Add(project);
			info.ArgumentList.Add("--nologo");
			info.RedirectStandardOutput = true;
			info.RedirectStandardError = true;
			info.UseShellExecute = false;
			using (Process process = Process.Start(info)!)
			{
				string output = process.StandardOutput.ReadToEnd();
				string error = process.StandardError.ReadToEnd();
				process.WaitForExit();
				Assert.True(process.ExitCode == 0, error);
				Dictionary<string, string> from11 = new Dictionary<string, string>(StringComparer.Ordinal);
				string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
				for (int i = 0; i < lines.Length; i++)
				{
					string line = lines[i].Trim('\r');
					string[] parts = line.Split('\t');
					if (parts.Length < 2 || parts[0].Length == 0)
						continue;

					string hex = parts.Length >= 3 ? parts[2] : "";
					from11[parts[0]] = parts[1] + "\t" + hex;
				}

				AssertLayout(from11, "aligned", new Code("10110011"));
				AssertLayout(from11, "mid", new Code("10101"));
				AssertLayout(from11, "empty", new Code(0));
				AssertLayout(from11, "trailing", new Code("1"));
			}
		}

		#region private functions
		private static void AssertLayout(Dictionary<string, string> from11, string name, Code code)
		{
			byte[] bytes = code.ToByteArray();
			string hex = "";
			for (int i = 0; i < bytes.Length; i++)
				hex += bytes[i].ToString("x2");

			Assert.Equal(code.Length.ToString() + "\t" + hex, from11[name]);
			int packed = 0;
			int limit = Math.Min(8, code.Length);
			for (int i = 0; i < limit; i++)
			{
				int bit = code[i];
				if (bit == 1)
					packed |= 1 << i;
			}

			if (bytes.Length > 0)
				Assert.Equal((byte)packed, bytes[0]);
		}

		private static IPiedPiper CreatePiedPiper()
		{
			IPiedPiper piedPiper = new PiedPiper();
			piedPiper.RegisterCorePackRats();
			piedPiper.RegisterPackRats(typeof(StoryThing).Assembly);
			return piedPiper;
		}

		private static void WaitForFile(string path)
		{
			DateTime deadline = DateTime.UtcNow.AddSeconds(15);
			while (!File.Exists(path))
			{
				if (DateTime.UtcNow > deadline)
					throw new TimeoutException("Timed out waiting for " + path);

				Thread.Sleep(20);
			}
		}

		private static void WaitForReadyCount(string directory, int count)
		{
			DateTime deadline = DateTime.UtcNow.AddSeconds(15);
			while (true)
			{
				int ready = 0;
				if (Directory.Exists(directory))
					ready = Directory.GetFiles(directory, "*.ready").Length;

				if (ready >= count)
					return;

				if (DateTime.UtcNow > deadline)
					throw new TimeoutException("Timed out waiting for " + count.ToString() + " ready writers.");

				Thread.Sleep(20);
			}
		}
		#endregion
	}

	internal sealed class CrashRun
	{
		public int ExitCode
		{
			get;
			set;
		}

		public string Error
		{
			get;
			set;
		} = "";
	}

	internal static class CrashClient
	{
		public static CrashRun Run(string root, string format, string point, int records, bool tear)
		{
			List<string> args = new List<string>();
			args.Add("kill-append");
			args.Add(root);
			args.Add(format);
			args.Add(point);
			args.Add(records.ToString());
			if (tear)
				args.Add("--tear");

			using (Process process = Start(null, args.ToArray()))
			{
				string error = process.StandardError.ReadToEnd();
				process.WaitForExit();
				CrashRun run = new CrashRun();
				run.ExitCode = process.ExitCode;
				run.Error = error;
				return run;
			}
		}

		public static Process Start(params string[] args)
		{
			return Start(null, args);
		}

		public static Process Start(IDictionary<string, string>? environment, params string[] args)
		{
			ProcessStartInfo info = new ProcessStartInfo();
			info.FileName = FindDotnet();
			info.ArgumentList.Add("exec");
			info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "CrashHost.dll"));
			for (int i = 0; i < args.Length; i++)
				info.ArgumentList.Add(args[i]);

			info.UseShellExecute = false;
			info.RedirectStandardOutput = false;
			info.RedirectStandardError = true;
			if (environment != null)
			{
				foreach (KeyValuePair<string, string> pair in environment)
					info.Environment[pair.Key] = pair.Value;
			}

			Process? process = Process.Start(info);
			if (process == null)
				throw new InvalidOperationException("Could not start CrashHost.");

			return process;
		}

		public static string FindDotnet()
		{
			string? path = Environment.GetEnvironmentVariable("PATH");
			if (path != null)
			{
				string[] dirs = path.Split(':');
				for (int i = 0; i < dirs.Length; i++)
				{
					string candidate = Path.Combine(dirs[i], "dotnet");
					if (File.Exists(candidate))
						return candidate;
				}
			}

			throw new InvalidOperationException("dotnet is not on PATH.");
		}
	}
}
