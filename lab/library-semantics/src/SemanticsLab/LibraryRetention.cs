using System.Globalization;
using BigRedProf.Data.Core;

namespace BigRedProf.Stories.LibrarySemanticsLab
{
	public sealed partial class Library
	{
		#region methods
		public AppendResult ReleaseClaim(
			string storyId,
			long offset,
			string reason,
			string idempotencyKey,
			string expectedEditionId,
			FaultInjector? faults
		)
		{
			return AppendClaimCertificate(
				storyId,
				offset,
				reason,
				idempotencyKey,
				expectedEditionId,
				faults,
				EffectKind.ReleaseClaim
			);
		}

		public AppendResult PromoteClaim(
			string storyId,
			long offset,
			string reason,
			string idempotencyKey,
			string expectedEditionId,
			FaultInjector? faults
		)
		{
			return AppendClaimCertificate(
				storyId,
				offset,
				reason,
				idempotencyKey,
				expectedEditionId,
				faults,
				EffectKind.PromoteClaim
			);
		}

		public AppendResult CompactSand(
			string storyId,
			long start,
			long end,
			string summaryText,
			string certificateText,
			string idempotencyKey,
			string expectedEditionId,
			FaultInjector? faults
		)
		{
			if (summaryText == null)
				throw new ArgumentNullException(nameof(summaryText));

			if (certificateText == null)
				throw new ArgumentNullException(nameof(certificateText));

			lock (_gate)
			{
				try
				{
					RequireAlive();
					string currentId = ReadPointer();
					if (expectedEditionId != currentId)
					{
						return new AppendResult(
							AppendStatus.PositionConflict,
							currentId,
							"Expected edition " + expectedEditionId + " but the library is at " + currentId + ".",
							0
						);
					}

					if (end <= start)
						return new AppendResult(AppendStatus.Rejected, currentId, "The compaction range is empty.", 0);

					List<StoredRecord> published = ReadCommittedFrom(storyId, GenerationOf(storyId), CountOf(storyId));
					string? problem = ValidateCompactionRange(published, start, end);
					if (problem != null)
						return new AppendResult(AppendStatus.Rejected, currentId, problem, 0);

					long high = HighWaterOf(published);
					List<KeyHash> superseded = new List<KeyHash>();
					List<StoredRecord> kept = new List<StoredRecord>();
					for (int i = 0; i < published.Count; i++)
					{
						StoredRecord record = published[i];
						if (record.Offset >= start && record.Offset < end)
						{
							superseded.Add(new KeyHash(record.IdempotencyKey, record.BodyHash));
							continue;
						}

						kept.Add(CloneRecord(record));
					}

					RecordDraft summaryDraft = new RecordDraft(RetentionClass.Sand, LabCode.FromText(summaryText));
					summaryDraft.Effect = EffectKind.MovementSummary;
					RecordDraft certificateDraft = new RecordDraft(RetentionClass.Stone, LabCode.FromText(certificateText));
					certificateDraft.Effect = EffectKind.CompactSand;
					certificateDraft.EffectArgument = start.ToString(CultureInfo.InvariantCulture) + "|" + end.ToString(CultureInfo.InvariantCulture);
					certificateDraft.Supersedes = superseded;
					StoryAppend append = new StoryAppend(storyId, high, new RecordDraft[] { summaryDraft, certificateDraft });
					AppendRequest request = new AppendRequest(idempotencyKey, expectedEditionId, new StoryAppend[] { append });
					string bodyHash = RecordCodec.HashDrafts(request.Stories);
					KeyDisposition existing = Classify(ReadAllPublished(), request, bodyHash);
					if (existing == KeyDisposition.Same)
					{
						return new AppendResult(
							AppendStatus.AlreadyCommitted,
							currentId,
							"Compaction already published.",
							0
						);
					}

					if (existing == KeyDisposition.Different)
					{
						return new AppendResult(
							AppendStatus.IdempotencyConflict,
							currentId,
							"The compaction key was used for a different body.",
							0
						);
					}

					StoredRecord summary = ToStored(summaryDraft, storyId, high, idempotencyKey, bodyHash);
					StoredRecord certificate = ToStored(certificateDraft, storyId, high + 1, idempotencyKey, bodyHash);
					List<StoredRecord> planned = new List<StoredRecord>();
					planned.AddRange(kept);
					planned.Add(summary);
					planned.Add(certificate);
					planned.Sort(delegate(StoredRecord left, StoredRecord right)
					{
						return left.Offset.CompareTo(right.Offset);
					});

					int nextGeneration = GenerationOf(storyId) + 1;
					string nextPath = LibraryLayout.Generation(_root, storyId, nextGeneration);
					if (File.Exists(nextPath) && !GenerationMatches(nextPath, planned))
						File.Delete(nextPath);

					if (!File.Exists(nextPath))
						StoryLog.Rewrite(nextPath, _format, planned);

					Dictionary<string, int> overrides = new Dictionary<string, int>(StringComparer.Ordinal);
					overrides[storyId] = nextGeneration;
					AppendResult result = PublishExisting(
						idempotencyKey,
						bodyHash,
						Array.Empty<ContentWrite>(),
						faults,
						1,
						new string[] { storyId },
						overrides
					);
					if (result.Status == AppendStatus.Committed)
						DeleteStrayGenerations();

					return result;
				}
				catch (CrashInjectedException)
				{
					_dead = true;
					throw;
				}
			}
		}

		public EditionPin Pin()
		{
			lock (_gate)
			{
				RequireAlive();
				string editionId = ReadPointer();
				string directory = LibraryLayout.Pin(_root, editionId);
				if (Directory.Exists(directory))
					Directory.Delete(directory, true);

				Directory.CreateDirectory(directory);
				ParsedManifest manifest = ReadManifest(editionId);
				Directory.CreateDirectory(Path.Combine(directory, "editions"));
				File.WriteAllText(Path.Combine(directory, "editions", editionId + ".txt"), manifest.Text);
				File.WriteAllText(Path.Combine(directory, "edition-id.txt"), editionId);
				for (int i = 0; i < manifest.Stories.Count; i++)
				{
					StoryPrefix story = manifest.Stories[i];
					string source = LibraryLayout.Generation(_root, story.StoryId, story.Generation);
					string destination = Path.Combine(
						directory,
						"stories",
						Hashing.Sha256(story.StoryId),
						"gen-" + story.Generation.ToString(CultureInfo.InvariantCulture) + ".bin"
					);
					Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
					CopyPrefix(source, destination, story.FileBytes);
				}

				for (int i = 0; i < manifest.Content.Count; i++)
				{
					ContentEntry entry = manifest.Content[i];
					if (entry.Disposition != "required")
						continue;

					string source = LibraryLayout.ContentFile(_root, entry.ContentId);
					string destination = Path.Combine(directory, "content", entry.ContentId + ".bin");
					Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
					File.Copy(source, destination, true);
				}

				return new EditionPin(editionId, directory);
			}
		}

		public VerificationReport Verify(string editionId, Func<EditionView, bool>? application)
		{
			if (string.IsNullOrWhiteSpace(editionId))
				throw new ArgumentException("An edition id is required.", nameof(editionId));

			lock (_gate)
			{
				RequireAlive();
				VerificationReport report = new VerificationReport();
				report.Integrity = CheckResult.Pass;
				report.Completeness = CheckResult.Pass;
				report.RetentionCompliance = CheckResult.Pass;
				report.ApplicationReconstructed = application == null ? CheckResult.NotChecked : CheckResult.Pass;
				report.IsPublished = ReadPointer() == editionId;
				ParsedManifest manifest;
				try
				{
					manifest = ReadManifest(editionId);
				}
				catch (InvalidDataException exception)
				{
					report.Integrity = CheckResult.Fail;
					report.Findings.Add(exception.Message);
					return report;
				}

				EditionView? view = null;
				try
				{
					view = ReadEditionUnlocked(editionId, requireHash: true);
				}
				catch (InvalidDataException exception)
				{
					report.Integrity = CheckResult.Fail;
					report.Findings.Add(exception.Message);
				}

				for (int i = 0; i < manifest.Stories.Count; i++)
					CheckStoryRetention(manifest.Stories[i], report);

				for (int i = 0; i < manifest.Content.Count; i++)
					CheckContent(manifest.Content[i], report);

				if (application != null && view != null && report.Integrity == CheckResult.Pass)
				{
					bool reconstructed = false;
					try
					{
						reconstructed = application(view);
					}
					catch (Exception exception)
					{
						report.Findings.Add("Application fold failed: " + exception.Message);
					}

					if (!reconstructed)
						report.ApplicationReconstructed = CheckResult.Fail;
				}

				if (!report.IsPublished)
					report.Findings.Add("This edition is not the published current edition.");

				return report;
			}
		}

		internal void DebugDropSandWithoutCertificate(string storyId, long offset)
		{
			lock (_gate)
			{
				RequireAlive();
				int generation = GenerationOf(storyId);
				List<StoredRecord> records = ReadCommittedFrom(storyId, generation, CountOf(storyId));
				List<StoredRecord> kept = new List<StoredRecord>();
				bool removed = false;
				for (int i = 0; i < records.Count; i++)
				{
					if (records[i].Offset == offset)
					{
						if (records[i].Retention != RetentionClass.Sand)
							throw new InvalidOperationException("Refusing to drop a stone record.");

						removed = true;
						continue;
					}

					kept.Add(records[i]);
				}

				if (!removed)
					throw new InvalidOperationException("Offset " + offset.ToString(CultureInfo.InvariantCulture) + " is not in the story.");

				string path = LibraryLayout.Generation(_root, storyId, generation);
				StoryLog.Rewrite(path, _format, kept);
				PublishExisting(
					"debug-drop-" + storyId + "-" + offset.ToString(CultureInfo.InvariantCulture),
					"debug-drop",
					Array.Empty<ContentWrite>(),
					null,
					0,
					new string[] { storyId },
					null
				);
			}
		}
		#endregion

		#region private methods
		private AppendResult AppendClaimCertificate(
			string storyId,
			long offset,
			string reason,
			string idempotencyKey,
			string expectedEditionId,
			FaultInjector? faults,
			EffectKind effect
		)
		{
			if (reason == null)
				throw new ArgumentNullException(nameof(reason));

			lock (_gate)
			{
				try
				{
					RequireAlive();
					List<StoredRecord> published = ReadAllPublished();
					StoredRecord? claim = null;
					for (int i = 0; i < published.Count; i++)
					{
						if (published[i].StoryId == storyId && published[i].Offset == offset)
							claim = published[i];
					}

					if (claim == null || string.IsNullOrWhiteSpace(claim.ContentId))
					{
						return new AppendResult(
							AppendStatus.Rejected,
							ReadPointer(),
							"There is no content claim at that offset.",
							0
						);
					}

					RecordDraft draft = new RecordDraft(RetentionClass.Stone, LabCode.FromText(reason));
					draft.Effect = effect;
					draft.EffectArgument = storyId
						+ "|"
						+ offset.ToString(CultureInfo.InvariantCulture)
						+ "|"
						+ claim.ContentId;
					StoryAppend story = new StoryAppend(storyId, HighWaterUnlocked(storyId), new RecordDraft[] { draft });
					AppendRequest request = new AppendRequest(
						idempotencyKey,
						expectedEditionId,
						new StoryAppend[] { story }
					);
					return AppendUnlocked(request, faults);
				}
				catch (CrashInjectedException)
				{
					_dead = true;
					throw;
				}
			}
		}

		private int CountOf(string storyId)
		{
			StoryPrefix? prefix = FindPrefix(ReadManifest(ReadPointer()), storyId);
			if (prefix == null)
				return 0;

			return prefix.RecordCount;
		}

		private static string? ValidateCompactionRange(List<StoredRecord> records, long start, long end)
		{
			for (long offset = start; offset < end; offset++)
			{
				StoredRecord? found = null;
				for (int i = 0; i < records.Count; i++)
				{
					if (records[i].Offset == offset)
						found = records[i];
				}

				if (found == null)
					return "The compaction range is already missing offset " + offset.ToString(CultureInfo.InvariantCulture) + ".";

				if (found.Retention != RetentionClass.Sand)
					return "Refusing to compact stone at offset " + offset.ToString(CultureInfo.InvariantCulture) + ".";

				if (!string.IsNullOrWhiteSpace(found.ContentId))
					return "Release content claims explicitly before compacting them.";
			}

			return null;
		}

		private static StoredRecord CloneRecord(StoredRecord record)
		{
			StoredRecord clone = new StoredRecord();
			clone.StoryId = record.StoryId;
			clone.Offset = record.Offset;
			clone.Retention = record.Retention;
			clone.Body = record.Body;
			clone.IdempotencyKey = record.IdempotencyKey;
			clone.BodyHash = record.BodyHash;
			clone.DeclaresStoryId = record.DeclaresStoryId;
			clone.ContentId = record.ContentId;
			clone.Effect = record.Effect;
			clone.EffectArgument = record.EffectArgument;
			clone.Supersedes = record.Supersedes;
			return clone;
		}

		private static bool GenerationMatches(string path, List<StoredRecord> planned)
		{
			LogImage image;
			try
			{
				image = StoryLog.Read(path);
			}
			catch (InvalidDataException)
			{
				return false;
			}

			if (image.Committed.Count != planned.Count)
				return false;

			for (int i = 0; i < planned.Count; i++)
			{
				StoredRecord expected = planned[i];
				StoredRecord actual = image.Committed[i];
				if (expected.Offset != actual.Offset)
					return false;

				if (expected.IdempotencyKey != actual.IdempotencyKey)
					return false;

				if (expected.Effect != actual.Effect)
					return false;

				if (!expected.Body.Equals(actual.Body))
					return false;
			}

			return true;
		}

		private static void CopyPrefix(string source, string destination, long count)
		{
			using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				byte[] buffer = new byte[8192];
				long remaining = count;
				while (remaining > 0)
				{
					int ask = (int)Math.Min(buffer.Length, remaining);
					int read = input.Read(buffer, 0, ask);
					if (read == 0)
						throw new EndOfStreamException("The pinned story ended early.");

					output.Write(buffer, 0, read);
					remaining -= read;
				}

				output.Flush(true);
			}
		}

		private void CheckStoryRetention(StoryPrefix story, VerificationReport report)
		{
			if (story.Encoding == "shipped-v1")
			{
				report.Findings.Add("Story '" + story.StoryId + "' is shipped bytes. Retention was not interpreted.");
				return;
			}

			string path = LibraryLayout.Generation(_root, story.StoryId, story.Generation);
			if (!File.Exists(path))
			{
				report.Completeness = CheckResult.Fail;
				report.Findings.Add("Missing story file for '" + story.StoryId + "'.");
				return;
			}

			List<StoredRecord> records;
			try
			{
				records = ReadCommittedFrom(story.StoryId, story.Generation, story.RecordCount);
			}
			catch (InvalidDataException exception)
			{
				report.Integrity = CheckResult.Fail;
				report.Findings.Add(exception.Message);
				return;
			}

			HashSet<long> present = new HashSet<long>();
			List<(long start, long end)> ranges = new List<(long start, long end)>();
			for (int i = 0; i < records.Count; i++)
			{
				present.Add(records[i].Offset);
				if (records[i].Effect != EffectKind.CompactSand)
					continue;

				string[] parts = (records[i].EffectArgument ?? "").Split('|');
				long rangeStart;
				long rangeEnd;
				if (parts.Length >= 2
					&& long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out rangeStart)
					&& long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out rangeEnd))
				{
					ranges.Add((rangeStart, rangeEnd));
				}
			}

			for (long offset = 0; offset < story.HighWater; offset++)
			{
				bool covered = false;
				for (int r = 0; r < ranges.Count; r++)
				{
					if (offset >= ranges[r].start && offset < ranges[r].end)
						covered = true;
				}

				if (present.Contains(offset))
				{
					if (covered)
					{
						report.RetentionCompliance = CheckResult.Fail;
						report.Findings.Add(
							"Story '" + story.StoryId + "' still stores offset "
							+ offset.ToString(CultureInfo.InvariantCulture)
							+ " inside a compacted range."
						);
					}
				}
				else if (!covered)
				{
					report.RetentionCompliance = CheckResult.Fail;
					report.Findings.Add(
						"Story '" + story.StoryId + "' has a gap at offset "
						+ offset.ToString(CultureInfo.InvariantCulture)
						+ " without a certificate."
					);
				}
			}
		}

		private void CheckContent(ContentEntry entry, VerificationReport report)
		{
			string path = LibraryLayout.ContentFile(_root, entry.ContentId);
			bool exists = File.Exists(path);
			if (entry.Disposition == "required")
			{
				if (!exists)
				{
					report.Completeness = CheckResult.Fail;
					report.Findings.Add("Required content " + entry.ContentId + " is missing.");
					return;
				}

				string hash = Hashing.Sha256(File.ReadAllBytes(path));
				if (hash != entry.Hash)
				{
					report.Integrity = CheckResult.Fail;
					report.Findings.Add("Required content " + entry.ContentId + " hash does not match.");
				}

				return;
			}

			if (entry.Disposition == "erased" && exists)
			{
				report.RetentionCompliance = CheckResult.Fail;
				report.Findings.Add("Erased content " + entry.ContentId + " is still stored.");
			}
		}
		#endregion
	}
}
