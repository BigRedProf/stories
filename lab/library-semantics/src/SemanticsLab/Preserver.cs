namespace BigRedProf.Stories.LibrarySemanticsLab
{
	public sealed class PreserveResult
	{
		#region constructors
		public PreserveResult(bool published, bool alreadyPreserved, bool failed, string editionId, long bytesCopied, string detail)
		{
			Published = published;
			AlreadyPreserved = alreadyPreserved;
			Failed = failed;
			EditionId = editionId ?? "";
			BytesCopied = bytesCopied;
			Detail = detail ?? "";
		}
		#endregion

		#region properties
		public bool Published
		{
			get;
		}

		public bool AlreadyPreserved
		{
			get;
		}

		public bool Failed
		{
			get;
		}

		public string EditionId
		{
			get;
		}

		public long BytesCopied
		{
			get;
		}

		public string Detail
		{
			get;
		}
		#endregion
	}

	public sealed class RestoreOptions
	{
		#region properties
		public bool PreventResurrection
		{
			get;
			set;
		}

		/// <summary>
		/// Required when the edition contains sand. Exact restore then materializes
		/// that sand, including sand a later deletion removed.
		/// </summary>
		public bool AcceptResurrection
		{
			get;
			set;
		}

		public Library? NewerEvidence
		{
			get;
			set;
		}

		public string? NewerEditionId
		{
			get;
			set;
		}
		#endregion
	}

	public sealed class RestoreResult
	{
		#region constructors
		public RestoreResult(bool restored, bool refused, bool erasuresApplied, string editionId, string detail)
		{
			Restored = restored;
			Refused = refused;
			ErasuresApplied = erasuresApplied;
			EditionId = editionId ?? "";
			Detail = detail ?? "";
		}
		#endregion

		#region properties
		public bool Restored
		{
			get;
		}

		public bool Refused
		{
			get;
		}

		public bool ErasuresApplied
		{
			get;
		}

		public string EditionId
		{
			get;
		}

		public string Detail
		{
			get;
		}
		#endregion
	}

	public static class Preserver
	{
		#region functions
		public static PreserveResult Preserve(Library source, Library destination, FaultInjector? faults, EditionPin? pin)
		{
			if (source == null)
				throw new ArgumentNullException(nameof(source));

			if (destination == null)
				throw new ArgumentNullException(nameof(destination));

			if (source.Format != destination.Format)
			{
				return new PreserveResult(false, false, true, "", 0, "Refused. The libraries use different story framings.");
			}

			string editionId = pin == null ? source.CurrentEditionId : pin.EditionId;
			if (destination.CurrentEditionId == editionId)
			{
				return new PreserveResult(
					true,
					true,
					false,
					editionId,
					0,
					"The destination already has this edition. A lost acknowledgement must not copy it again."
				);
			}

			string emptyId = ManifestText.EmptyId();
			if (destination.CurrentEditionId != emptyId && !SourceHasAncestor(source, editionId, destination.CurrentEditionId))
			{
				return new PreserveResult(
					false,
					false,
					true,
					editionId,
					0,
					"Refused. The destination is at " + destination.CurrentEditionId + ", which is not an ancestor of " + editionId + "."
				);
			}

			if (pin == null)
			{
				string stamp = source.ByteStamp(editionId);
				if (faults != null)
					faults.Hit(FaultPoint.AfterManifestCaptured);

				if (source.ByteStamp(editionId) != stamp)
				{
					return new PreserveResult(
						false,
						false,
						true,
						editionId,
						0,
						"Failed. The source edition changed before the copy. Nothing was published."
					);
				}
			}
			else if (faults != null)
			{
				faults.Hit(FaultPoint.AfterManifestCaptured);
			}

			string origin = pin == null ? source.Root : pin.Directory;
			string manifestPath = Path.Combine(origin, "editions", editionId + ".txt");
			if (!File.Exists(manifestPath))
			{
				return new PreserveResult(false, false, true, editionId, 0, "Failed. The edition manifest is not in the source.");
			}

			ParsedManifest manifest = ManifestText.Parse(File.ReadAllText(manifestPath));
			string? mismatch = SourceMatchesManifest(origin, manifest);
			if (mismatch != null)
				return new PreserveResult(false, false, true, editionId, 0, mismatch);

			long bytes = 0;
			try
			{
				for (int i = 0; i < manifest.Stories.Count; i++)
				{
					StoryPrefix story = manifest.Stories[i];
					string from = LibraryLayout.Generation(origin, story.StoryId, story.Generation);
					string to = LibraryLayout.Generation(destination.Root, story.StoryId, story.Generation);
					bytes += CopyStory(from, to, story.FileBytes);
					if (faults != null)
						faults.Hit("after-story-copied:" + story.StoryId);
				}

				for (int i = 0; i < manifest.Content.Count; i++)
				{
					ContentEntry entry = manifest.Content[i];
					string to = LibraryLayout.ContentFile(destination.Root, entry.ContentId);
					if (entry.Disposition != "required")
						continue;

					string from = LibraryLayout.ContentFile(origin, entry.ContentId);
					if (!File.Exists(from))
					{
						return new PreserveResult(
							false,
							false,
							true,
							editionId,
							bytes,
							"Failed. Required content " + entry.ContentId + " is missing. The backup was not published."
						);
					}

					bytes += CopyContent(from, to, entry.Hash, faults);
				}

				destination.PublishImported(manifest.Text, faults);
			}
			catch (CrashInjectedException)
			{
				throw;
			}
			catch (Exception exception)
			{
				return new PreserveResult(
					false,
					false,
					true,
					editionId,
					bytes,
					"Failed. " + exception.Message + " The backup was not published."
				);
			}

			return new PreserveResult(true, false, false, editionId, bytes, "Published edition " + editionId + ".");
		}

		private static PreserveResult CopyEdition(Library source, EditionView edition, Library destination)
		{
			if (source.Format != destination.Format)
			{
				return new PreserveResult(false, false, true, edition.Id, 0, "Refused. The libraries use different story framings.");
			}

			string manifestPath = LibraryLayout.Manifest(source.Root, edition.Id);
			if (!File.Exists(manifestPath))
			{
				return new PreserveResult(false, false, true, edition.Id, 0, "Failed. The requested edition is not in the backup.");
			}

			ParsedManifest manifest = ManifestText.Parse(File.ReadAllText(manifestPath));
			if (manifest.Id != edition.Id)
			{
				return new PreserveResult(false, false, true, edition.Id, 0, "Failed. The edition file does not hash to the requested id.");
			}

			if (destination.CurrentEditionId == manifest.Id)
			{
				VerificationReport report = destination.Verify(manifest.Id, null);
				if (report.Integrity == CheckResult.Pass && report.Completeness == CheckResult.Pass)
				{
					return new PreserveResult(
						true,
						true,
						false,
						manifest.Id,
						0,
						"The destination already has this edition. The retry did not copy."
					);
				}
			}

			string? mismatch = SourceMatchesManifest(source.Root, manifest);
			if (mismatch != null)
			{
				return new PreserveResult(false, false, true, edition.Id, 0, mismatch);
			}

			long bytes = 0;
			try
			{
				for (int i = 0; i < manifest.Stories.Count; i++)
				{
					StoryPrefix story = manifest.Stories[i];
					string from = LibraryLayout.Generation(source.Root, story.StoryId, story.Generation);
					string to = LibraryLayout.Generation(destination.Root, story.StoryId, story.Generation);
					bytes += CopyStory(from, to, story.FileBytes);
				}

				for (int i = 0; i < manifest.Content.Count; i++)
				{
					ContentEntry entry = manifest.Content[i];
					if (entry.Disposition != "required")
						continue;

					string from = LibraryLayout.ContentFile(source.Root, entry.ContentId);
					string to = LibraryLayout.ContentFile(destination.Root, entry.ContentId);
					bytes += CopyContent(from, to, entry.Hash, null);
				}

				destination.PublishImported(manifest.Text, null);
			}
			catch (Exception exception)
			{
				return new PreserveResult(
					false,
					false,
					true,
					edition.Id,
					bytes,
					"Failed. " + exception.Message + " The restore was not published."
				);
			}

			return new PreserveResult(true, false, false, edition.Id, bytes, "Published edition " + edition.Id + ".");
		}

		public static RestoreResult Restore(Library backup, string editionId, Library destination, RestoreOptions options)
		{
			if (backup == null)
				throw new ArgumentNullException(nameof(backup));

			if (destination == null)
				throw new ArgumentNullException(nameof(destination));

			if (options == null)
				throw new ArgumentNullException(nameof(options));

			if (string.IsNullOrWhiteSpace(editionId))
				throw new ArgumentException("An edition id is required.", nameof(editionId));

			if (options.PreventResurrection && (options.NewerEvidence == null || string.IsNullOrWhiteSpace(options.NewerEditionId)))
			{
				return new RestoreResult(
					false,
					true,
					false,
					"",
					"Refused. Preventing resurrection needs a newer edition's certificates. None were supplied. A missing blob is not a deletion."
				);
			}

			string emptyId = ManifestText.EmptyId();
			bool resumeErasures = false;
			if (destination.CurrentEditionId != emptyId && destination.CurrentEditionId != editionId)
			{
				if (options.PreventResurrection && ErasureChainReaches(destination, editionId, options.NewerEvidence!, options.NewerEditionId!))
					resumeErasures = true;
				else
				{
					return new RestoreResult(
						false,
						true,
						false,
						"",
						"Refused. The destination is at " + destination.CurrentEditionId + ", which is not empty."
					);
				}
			}

			if (resumeErasures)
				return FinishErasures(editionId, destination, options);

			EditionView edition = backup.ReadEdition(editionId);
			bool hasSand = false;
			for (int i = 0; i < edition.Records.Count; i++)
			{
				if (edition.Records[i].Retention == RetentionClass.Sand)
					hasSand = true;
			}

			if (hasSand && !options.AcceptResurrection && !options.PreventResurrection)
			{
				return new RestoreResult(
					false,
					true,
					false,
					"",
					"Refused. This edition contains sand. Exact restore would materialize it, including sand deleted after this edition. Pass AcceptResurrection to proceed."
				);
			}

			PreserveResult copied = CopyEdition(backup, edition, destination);
			if (copied.AlreadyPreserved)
			{
				if (options.PreventResurrection)
					return FinishErasures(editionId, destination, options);

				return new RestoreResult(true, false, false, editionId, copied.Detail);
			}

			if (copied.Failed || !copied.Published)
				return new RestoreResult(false, true, false, editionId, copied.Detail);

			if (destination.CurrentEditionId != editionId)
			{
				return new RestoreResult(
					false,
					true,
					false,
					destination.CurrentEditionId,
					"Refused. Restore published " + destination.CurrentEditionId + " instead of the requested edition " + editionId + "."
				);
			}

			string detail = "Restored edition " + editionId + ". This edition has no sand records.";
			if (options.AcceptResurrection)
			{
				detail = "Restored edition " + editionId
					+ ". Caller accepted resurrection. Sand that this edition contains is present again. Newer removal evidence was not supplied, so this restore cannot keep later deletions deleted.";
			}

			bool applied = false;
			if (options.PreventResurrection)
				return FinishErasures(editionId, destination, options);

			return new RestoreResult(true, false, applied, destination.CurrentEditionId, detail);
		}
		#endregion

		#region private functions
		private static string? SourceMatchesManifest(string origin, ParsedManifest manifest)
		{
			for (int i = 0; i < manifest.Stories.Count; i++)
			{
				StoryPrefix story = manifest.Stories[i];
				string from = LibraryLayout.Generation(origin, story.StoryId, story.Generation);
				if (!File.Exists(from))
					return "Failed. Story '" + story.StoryId + "' is missing from the backup. The destination was not modified.";

				string hash = Hashing.FilePrefix(from, story.FileBytes);
				if (hash != story.FileHash)
					return "Failed. Story '" + story.StoryId + "' does not match the edition. The destination was not modified.";
			}

			for (int i = 0; i < manifest.Content.Count; i++)
			{
				ContentEntry entry = manifest.Content[i];
				if (entry.Disposition != "required")
					continue;

				string from = LibraryLayout.ContentFile(origin, entry.ContentId);
				if (!File.Exists(from))
					return "Failed. Required content " + entry.ContentId + " is missing. The destination was not modified.";

				string hash = Hashing.Sha256(File.ReadAllBytes(from));
				if (hash != entry.Hash)
					return "Failed. Content " + entry.ContentId + " does not match the edition. The destination was not modified.";
			}

			return null;
		}

		private static bool SourceHasAncestor(Library source, string editionId, string candidate)
		{
			string cursor = editionId;
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			while (cursor != "-" && cursor.Length > 0 && seen.Add(cursor))
			{
				if (cursor == candidate)
					return true;

				string path = LibraryLayout.Manifest(source.Root, cursor);
				if (!File.Exists(path))
					return false;

				ParsedManifest manifest = ManifestText.Parse(File.ReadAllText(path));
				cursor = manifest.ParentId;
			}

			return false;
		}

		private static long CopyStory(string from, string to, long count)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(to)!);
			byte[] sourceBytes = ReadPrefix(from, count);
			if (!File.Exists(to))
			{
				WriteAll(to, sourceBytes);
				return sourceBytes.Length;
			}

			byte[] existing = File.ReadAllBytes(to);
			if (existing.Length == sourceBytes.Length && BytesEqual(existing, sourceBytes, existing.Length))
				return 0;

			if (existing.Length < sourceBytes.Length && BytesEqual(existing, sourceBytes, existing.Length))
			{
				using (FileStream stream = new FileStream(to, FileMode.Append, FileAccess.Write, FileShare.None))
				{
					stream.Write(sourceBytes, existing.Length, sourceBytes.Length - existing.Length);
					stream.Flush(true);
				}

				return sourceBytes.Length - existing.Length;
			}

			WriteAll(to, sourceBytes);
			return sourceBytes.Length;
		}

		private static byte[] ReadPrefix(string path, long count)
		{
			using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				if (stream.Length < count)
					throw new InvalidDataException("'" + path + "' is shorter than the edition.");

				byte[] data = new byte[count];
				int offset = 0;
				while (offset < data.Length)
				{
					int read = stream.Read(data, offset, data.Length - offset);
					if (read == 0)
						throw new EndOfStreamException("The story ended inside the edition prefix.");

					offset += read;
				}

				return data;
			}
		}

		private static void WriteAll(string path, byte[] data)
		{
			using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				stream.Write(data, 0, data.Length);
				stream.Flush(true);
			}
		}

		private static long CopyContent(string from, string to, string expectedHash, FaultInjector? faults)
		{
			byte[] sourceBytes = File.ReadAllBytes(from);
			if (Hashing.Sha256(sourceBytes) != expectedHash)
				throw new InvalidDataException("Content " + expectedHash + " does not match the edition.");

			if (File.Exists(to))
			{
				byte[] existing = File.ReadAllBytes(to);
				if (existing.Length == sourceBytes.Length && BytesEqual(existing, sourceBytes, existing.Length))
					return 0;
			}

			Directory.CreateDirectory(Path.GetDirectoryName(to)!);
			string temporary = to + ".new";
			using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				if (faults != null && faults.ArmedPoint == FaultPoint.DuringContentCopy)
				{
					int partial = Math.Max(1, sourceBytes.Length / 2);
					stream.Write(sourceBytes, 0, partial);
					stream.Flush(true);
					faults.Hit(FaultPoint.DuringContentCopy);
				}

				stream.Write(sourceBytes, 0, sourceBytes.Length);
				stream.Flush(true);
			}

			File.Move(temporary, to, true);
			return sourceBytes.Length;
		}

		private static string? SummaryForPresentMoves(Library destination, string storyId, long start, long end)
		{
			IReadOnlyList<StoredRecord> records = destination.ReadCommitted(storyId);
			StoredRecord? last = null;
			int count = 0;
			for (int i = 0; i < records.Count; i++)
			{
				if (records[i].Offset < start || records[i].Offset >= end)
					continue;

				string text = LabCode.ToText(records[i].Body);
				if (!text.StartsWith("move|", StringComparison.Ordinal))
					continue;

				count++;
				if (last == null || records[i].Offset > last.Offset)
					last = records[i];
			}

			if (last == null)
				return null;

			string[] parts = LabCode.ToText(last.Body).Split('|');
			if (parts.Length < 5)
				return null;

			return "move-summary|" + parts[1] + "|" + parts[2] + "|" + count.ToString(System.Globalization.CultureInfo.InvariantCulture)
				+ "|" + parts[3] + "|" + parts[4];
		}

		private static bool BytesEqual(byte[] left, byte[] right, int count)
		{
			for (int i = 0; i < count; i++)
			{
				if (left[i] != right[i])
					return false;
			}

			return true;
		}

		private static RestoreResult FinishErasures(string editionId, Library destination, RestoreOptions options)
		{
			ErasureReplay replay = ApplyErasures(options.NewerEvidence!, options.NewerEditionId!, destination);
			if (replay.Failure != null)
			{
				return new RestoreResult(
					false,
					true,
					false,
					destination.CurrentEditionId,
					"Refused. Resurrection prevention did not finish. " + replay.Failure
				);
			}

			return new RestoreResult(
				true,
				false,
				true,
				destination.CurrentEditionId,
				"Restored edition " + editionId + ", then applied " + replay.Applied.ToString()
					+ " later certificate batches. Business events that exist only in the newer edition were not copied. This is not a roll-forward."
			);
		}

		private static bool ErasureChainReaches(Library destination, string editionId, Library newer, string newerEditionId)
		{
			HashSet<string> keys = ErasureKeys(newer, newerEditionId);
			string cursor = destination.CurrentEditionId;
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			while (cursor != editionId)
			{
				if (!seen.Add(cursor) || cursor == "-" || cursor.Length == 0)
					return false;

				string manifestPath = LibraryLayout.Manifest(destination.Root, cursor);
				if (!File.Exists(manifestPath))
					return false;

				ParsedManifest manifest = ManifestText.Parse(File.ReadAllText(manifestPath));
				if (!keys.Contains(manifest.CommitKey))
					return false;

				cursor = manifest.ParentId;
			}

			return true;
		}

		private static HashSet<string> ErasureKeys(Library newer, string newerEditionId)
		{
			HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
			EditionView evidence = newer.ReadEdition(newerEditionId);
			for (int i = 0; i < evidence.Records.Count; i++)
			{
				EffectKind effect = evidence.Records[i].Effect;
				if (effect == EffectKind.ReleaseClaim || effect == EffectKind.PromoteClaim || effect == EffectKind.CompactSand)
					keys.Add(evidence.Records[i].IdempotencyKey);
			}

			return keys;
		}

		private static ErasureReplay ApplyErasures(Library newer, string newerEditionId, Library destination)
		{
			EditionView evidence = newer.ReadEdition(newerEditionId);
			ErasureReplay replay = new ErasureReplay();
			List<StoredRecord> ordered = evidence.Records.OrderBy(item => item.StoryId, StringComparer.Ordinal).ThenBy(item => item.Offset).ToList();
			HashSet<string> done = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < ordered.Count; i++)
			{
				StoredRecord record = ordered[i];
				if (record.Effect == EffectKind.ReleaseClaim)
				{
					string[] parts = record.EffectArgument.Split('|');
					AppendResult result = destination.ReleaseClaim(
						parts[0],
						long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
						LabCode.ToText(record.Body),
						record.IdempotencyKey,
						destination.CurrentEditionId,
						null
					);
					if (!NoteErasure(replay, result))
						return replay;
				}

				if (record.Effect == EffectKind.PromoteClaim)
				{
					string[] parts = record.EffectArgument.Split('|');
					AppendResult result = destination.PromoteClaim(
						parts[0],
						long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
						LabCode.ToText(record.Body),
						record.IdempotencyKey,
						destination.CurrentEditionId,
						null
					);
					if (!NoteErasure(replay, result))
						return replay;
				}

				if (record.Effect == EffectKind.CompactSand && done.Add(record.IdempotencyKey))
				{
					StoredRecord? summary = null;
					for (int s = 0; s < ordered.Count; s++)
					{
						if (ordered[s].IdempotencyKey == record.IdempotencyKey && ordered[s].Effect == EffectKind.MovementSummary)
							summary = ordered[s];
					}

					if (summary == null)
					{
						replay.Failure = "The compaction certificate has no summary.";
						return replay;
					}

					string[] parts = record.EffectArgument.Split('|');
					long start = long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
					long end = long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
					long originalEnd = end;
					long high = destination.HighWater(record.StoryId);
					if (end > high)
						end = high;

					if (end <= start)
						continue;

					string summaryText = LabCode.ToText(summary.Body);
					if (originalEnd != end)
					{
						string? rebuilt = SummaryForPresentMoves(destination, record.StoryId, start, end);
						if (rebuilt == null)
						{
							replay.Failure = "The compaction summary covers moves this edition does not have.";
							return replay;
						}

						summaryText = rebuilt;
					}

					AppendResult result = destination.CompactSand(
						record.StoryId,
						start,
						end,
						summaryText,
						LabCode.ToText(record.Body),
						record.IdempotencyKey,
						destination.CurrentEditionId,
						null
					);
					if (!NoteErasure(replay, result))
						return replay;
				}
			}

			return replay;
		}

		private static bool NoteErasure(ErasureReplay replay, AppendResult result)
		{
			if (result.Status == AppendStatus.Committed)
			{
				replay.Applied++;
				return true;
			}

			if (result.Status == AppendStatus.AlreadyCommitted)
				return true;

			replay.Failure = result.Detail;
			return false;
		}
		private sealed class ErasureReplay
		{
			public int Applied
			{
				get;
				set;
			}

			public string? Failure
			{
				get;
				set;
			}
		}
		#endregion
	}
}
