using System.Globalization;
using System.Text;
using BigRedProf.Data.Core;

namespace BigRedProf.Stories.LibrarySemanticsLab
{
	public sealed partial class Library
	{
		#region private enums
		private enum KeyDisposition
		{
			None = 0,
			Same = 1,
			Different = 2,
			Prefix = 3
		}
		#endregion

		#region private methods
		private void RequireAlive()
		{
			if (_disposed || _dead)
				throw new ObjectDisposedException(nameof(Library));
		}

		private string FindEditionForKey(string key, string bodyHash, string fallback)
		{
			string directory = Path.Combine(_root, "editions");
			if (!Directory.Exists(directory))
				return fallback;

			string[] files = Directory.GetFiles(directory, "*.txt");
			for (int i = 0; i < files.Length; i++)
			{
				ParsedManifest manifest = ManifestText.Parse(File.ReadAllText(files[i]));
				if (manifest.CommitKey == key && manifest.BodyHash == bodyHash)
					return manifest.Id;
			}

			return fallback;
		}

		private string ReadPointer()
		{
			string text = File.ReadAllText(LibraryLayout.Pointer(_root)).Trim();
			if (text.Length == 0)
				throw new InvalidDataException("edition-current.txt is empty.");

			return text;
		}

		private void WritePointer(string editionId)
		{
			string finalPath = LibraryLayout.Pointer(_root);
			string tempPath = finalPath + ".new";
			byte[] data = Encoding.UTF8.GetBytes(editionId + "\n");
			using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				stream.Write(data, 0, data.Length);
				stream.Flush(true);
			}

			File.Move(tempPath, finalPath, true);
			PosixSync.FsyncDirectory(_root);
			_directorySyncs++;
		}

		private void WriteManifest(ParsedManifest manifest)
		{
			string path = LibraryLayout.Manifest(_root, manifest.Id);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			if (File.Exists(path))
			{
				string existing = File.ReadAllText(path);
				if (existing != manifest.Text)
					throw new InvalidDataException("Edition id " + manifest.Id + " already exists with different bytes.");

				return;
			}

			string tempPath = path + ".new";
			using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				byte[] data = Encoding.UTF8.GetBytes(manifest.Text);
				stream.Write(data, 0, data.Length);
				stream.Flush(true);
			}

			File.Move(tempPath, path, true);
			PosixSync.FsyncDirectory(Path.GetDirectoryName(path)!);
			_directorySyncs++;
		}

		private ParsedManifest ReadManifest(string editionId)
		{
			string path = LibraryLayout.Manifest(_root, editionId);
			if (!File.Exists(path))
				throw new InvalidDataException("Edition '" + editionId + "' is not in this library.");

			return ManifestText.Parse(File.ReadAllText(path));
		}

		private void RepairTornTails()
		{
			ParsedManifest manifest = ReadManifest(ReadPointer());
			for (int i = 0; i < manifest.Stories.Count; i++)
			{
				StoryPrefix story = manifest.Stories[i];
				if (story.Encoding == "shipped-v1")
					continue;

				string path = LibraryLayout.Generation(_root, story.StoryId, story.Generation);
				if (!File.Exists(path))
					continue;

				LogImage image = StoryLog.Read(path);
				if (!image.TornTail)
					continue;

				if (image.ValidLength < story.FileBytes)
					continue;

				StoryLog.Truncate(path, image.ValidLength);
			}
		}

		private void ReclaimErasedContent()
		{
			ParsedManifest manifest = ReadManifest(ReadPointer());
			for (int i = 0; i < manifest.Content.Count; i++)
			{
				ContentEntry entry = manifest.Content[i];
				if (entry.Disposition != "erased")
					continue;

				string path = LibraryLayout.ContentFile(_root, entry.ContentId);
				if (File.Exists(path))
					File.Delete(path);
			}
		}

		private long HighWaterUnlocked(string storyId)
		{
			StoryPrefix? prefix = FindPrefix(ReadManifest(ReadPointer()), storyId);
			if (prefix == null)
				return 0;

			return prefix.HighWater;
		}

		private int GenerationOf(string storyId)
		{
			return GenerationFor(storyId, ReadManifest(ReadPointer()), null);
		}

		private static int GenerationFor(
			string storyId,
			ParsedManifest previous,
			Dictionary<string, int>? generationOverrides
		)
		{
			if (generationOverrides != null && generationOverrides.ContainsKey(storyId))
				return generationOverrides[storyId];

			StoryPrefix? prefix = FindPrefix(previous, storyId);
			if (prefix == null)
				return 0;

			return prefix.Generation;
		}

		private static StoryPrefix? FindPrefix(ParsedManifest manifest, string storyId)
		{
			for (int i = 0; i < manifest.Stories.Count; i++)
			{
				if (manifest.Stories[i].StoryId == storyId)
					return manifest.Stories[i];
			}

			return null;
		}

		private string? StoryFile(string storyId)
		{
			StoryPrefix? prefix = FindPrefix(ReadManifest(ReadPointer()), storyId);
			if (prefix != null)
				return LibraryLayout.Generation(_root, storyId, prefix.Generation);

			string directory = LibraryLayout.StoryDirectory(_root, storyId);
			if (!Directory.Exists(directory))
				return null;

			string[] files = Directory.GetFiles(directory, "gen-*.bin");
			if (files.Length == 0)
				return null;

			Array.Sort(files, StringComparer.Ordinal);
			return files[files.Length - 1];
		}

		private List<StoredRecord> ReadQuarantine(IReadOnlyList<string> storyIds)
		{
			List<StoredRecord> records = new List<StoredRecord>();
			ParsedManifest manifest = ReadManifest(ReadPointer());
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < manifest.Stories.Count; i++)
			{
				StoryPrefix story = manifest.Stories[i];
				seen.Add(story.StoryId);
				if (story.Encoding == "shipped-v1")
					continue;

				_storyExaminations++;
				string path = LibraryLayout.Generation(_root, story.StoryId, story.Generation);
				if (!File.Exists(path))
					continue;

				if (new FileInfo(path).Length == story.FileBytes)
					continue;

				LogImage image = StoryLog.Read(path);
				if (image.Committed.Count < story.RecordCount)
					throw new InvalidDataException("Story '" + story.StoryId + "' lost published records.");

				for (int r = story.RecordCount; r < image.Committed.Count; r++)
				{
					image.Committed[r].StoryId = story.StoryId;
					records.Add(image.Committed[r]);
				}
			}

			for (int i = 0; i < storyIds.Count; i++)
			{
				if (seen.Contains(storyIds[i]))
					continue;

				string? path = StoryFile(storyIds[i]);
				if (path == null || !File.Exists(path))
					continue;

				LogImage image = StoryLog.Read(path);
				for (int r = 0; r < image.Committed.Count; r++)
				{
					image.Committed[r].StoryId = storyIds[i];
					records.Add(image.Committed[r]);
				}
			}

			return records;
		}

		private void TruncateUnpublished(IReadOnlyList<string> storyIds)
		{
			ParsedManifest manifest = ReadManifest(ReadPointer());
			for (int i = 0; i < storyIds.Count; i++)
			{
				if (FindPrefix(manifest, storyIds[i]) != null)
					continue;

				string? path = StoryFile(storyIds[i]);
				if (path == null || !File.Exists(path))
					continue;

				StoryLog.Truncate(path, StoryLog.HeaderLength);
			}
		}

		private EditionView ReadEditionUnlocked(string editionId, bool requireHash)
		{
			ParsedManifest manifest = ReadManifest(editionId);
			List<StoredRecord> records = new List<StoredRecord>();
			for (int i = 0; i < manifest.Stories.Count; i++)
			{
				StoryPrefix story = manifest.Stories[i];
				string path = LibraryLayout.Generation(_root, story.StoryId, story.Generation);
				if (story.RecordCount == 0 && story.FileBytes == StoryLog.HeaderLength && !File.Exists(path))
					continue;

				if (!File.Exists(path))
					throw new InvalidDataException("Story '" + story.StoryId + "' is missing from edition " + editionId + ".");

				if (requireHash)
				{
					string hash = Hashing.FilePrefix(path, story.FileBytes);
					if (hash != story.FileHash)
						throw new InvalidDataException("Story '" + story.StoryId + "' does not match edition " + editionId + ".");
				}

				if (story.Encoding == "shipped-v1")
					continue;

				records.AddRange(ReadCommittedFrom(story.StoryId, story.Generation, story.RecordCount));
			}

			EditionView view = new EditionView();
			view.Id = manifest.Id;
			view.ParentId = manifest.ParentId;
			view.CommitKey = manifest.CommitKey;
			view.BodyHash = manifest.BodyHash;
			view.Stories = manifest.Stories;
			view.Content = manifest.Content;
			view.Records = records;
			return view;
		}

		private List<StoredRecord> ReadAllPublished()
		{
			EditionView view = ReadEditionUnlocked(ReadPointer(), requireHash: false);
			return view.Records.ToList();
		}

		private List<StoredRecord> ReadCommittedUnlocked(string storyId)
		{
			string? path = StoryFile(storyId);
			if (path == null || !File.Exists(path))
				return new List<StoredRecord>();

			LogImage image = StoryLog.Read(path);
			for (int i = 0; i < image.Committed.Count; i++)
				image.Committed[i].StoryId = storyId;

			return image.Committed;
		}

		private List<StoredRecord> ReadCommittedFrom(string storyId, int generation, int count)
		{
			string path = LibraryLayout.Generation(_root, storyId, generation);
			List<StoredRecord> records = new List<StoredRecord>();
			if (!File.Exists(path) || count == 0)
				return records;

			LogImage image = StoryLog.Read(path);
			if (image.Committed.Count < count)
				throw new InvalidDataException("Story '" + storyId + "' lost published records.");

			for (int i = 0; i < count; i++)
			{
				image.Committed[i].StoryId = storyId;
				records.Add(image.Committed[i]);
			}

			return records;
		}

		private void TruncateQuarantine()
		{
			ParsedManifest manifest = ReadManifest(ReadPointer());
			for (int i = 0; i < manifest.Stories.Count; i++)
			{
				StoryPrefix story = manifest.Stories[i];
				if (story.Encoding == "shipped-v1")
					continue;

				string path = LibraryLayout.Generation(_root, story.StoryId, story.Generation);
				if (!File.Exists(path))
					continue;

				if (new FileInfo(path).Length > story.FileBytes)
					StoryLog.Truncate(path, story.FileBytes);
			}
		}

		private void DeleteStrayGenerations()
		{
			ParsedManifest manifest = ReadManifest(ReadPointer());
			for (int i = 0; i < manifest.Stories.Count; i++)
			{
				StoryPrefix story = manifest.Stories[i];
				string directory = LibraryLayout.StoryDirectory(_root, story.StoryId);
				if (!Directory.Exists(directory))
					continue;

				string[] files = Directory.GetFiles(directory, "gen-*.bin");
				string keep = LibraryLayout.Generation(_root, story.StoryId, story.Generation);
				for (int f = 0; f < files.Length; f++)
				{
					if (!string.Equals(files[f], keep, StringComparison.Ordinal))
						File.Delete(files[f]);
				}
			}
		}

		private StoryPrefix DescribeStory(string storyId, int generation, ParsedManifest previous)
		{
			if (_forcedPrefixes.ContainsKey(storyId))
				return _forcedPrefixes[storyId];

			string path = LibraryLayout.Generation(_root, storyId, generation);
			StoryPrefix? published = FindPrefix(previous, storyId);
			if (published != null && published.Encoding == "shipped-v1")
				return published;

			_storyExaminations++;
			if (published != null
				&& published.Generation == generation
				&& File.Exists(path)
				&& new FileInfo(path).Length == published.FileBytes)
			{
				return published;
			}

			if (!File.Exists(path))
				StoryLog.CreateEmpty(path, _format);

			LogImage image = StoryLog.Trim(path, _format);
			StoryPrefix prefix = new StoryPrefix();
			prefix.StoryId = storyId;
			prefix.Generation = generation;
			prefix.RecordCount = image.Committed.Count;
			prefix.HighWater = HighWaterOf(image.Committed);
			prefix.FileBytes = image.ValidLength;
			prefix.FileHash = Hashing.FilePrefix(path, image.ValidLength);
			return prefix;
		}

		private static long HighWaterOf(IReadOnlyList<StoredRecord> records)
		{
			long high = 0;
			for (int i = 0; i < records.Count; i++)
			{
				long next = records[i].Offset + 1;
				if (next > high)
					high = next;
			}

			return high;
		}

		private static StoredRecord ToStored(
			RecordDraft draft,
			string storyId,
			long offset,
			string key,
			string bodyHash
		)
		{
			StoredRecord stored = new StoredRecord();
			stored.StoryId = storyId;
			stored.Offset = offset;
			stored.Retention = draft.Retention;
			stored.Body = draft.Body;
			stored.IdempotencyKey = key;
			stored.BodyHash = bodyHash;
			stored.DeclaresStoryId = draft.DeclaresStoryId;
			stored.ContentId = draft.ContentId;
			stored.Effect = draft.Effect;
			stored.EffectArgument = draft.EffectArgument ?? "";
			stored.Supersedes = draft.Supersedes ?? Array.Empty<KeyHash>();
			return stored;
		}

		private void WriteContent(IReadOnlyList<ContentWrite> content)
		{
			if (content == null)
				return;

			for (int i = 0; i < content.Count; i++)
			{
				string id = ContentFiles.IdFor(content[i].Body);
				if (id != content[i].ContentId)
					throw new ArgumentException("Content id does not match the code bits.");

				ContentFiles.Write(LibraryLayout.ContentDirectory(_root), content[i].Body);
			}
		}

		private KeyDisposition Classify(List<StoredRecord> records, AppendRequest request, string bodyHash)
		{
			bool found = false;
			List<StoredRecord> keyed = new List<StoredRecord>();
			for (int i = 0; i < records.Count; i++)
			{
				StoredRecord record = records[i];
				if (record.IdempotencyKey == request.IdempotencyKey)
				{
					found = true;
					keyed.Add(record);
					if (record.BodyHash != bodyHash)
						return KeyDisposition.Different;
				}

				IReadOnlyList<KeyHash> superseded = record.Supersedes ?? Array.Empty<KeyHash>();
				for (int s = 0; s < superseded.Count; s++)
				{
					if (superseded[s].Key != request.IdempotencyKey)
						continue;

					found = true;
					if (superseded[s].BodyHash != bodyHash)
						return KeyDisposition.Different;

					return KeyDisposition.Same;
				}
			}

			if (!found)
				return KeyDisposition.None;

			if (PayloadEquals(keyed, request.Stories))
				return KeyDisposition.Same;

			if (IsProperPrefix(keyed, request.Stories))
				return KeyDisposition.Prefix;

			return KeyDisposition.Different;
		}

		private static bool IsProperPrefix(List<StoredRecord> stored, IReadOnlyList<StoryAppend> stories)
		{
			if (stored.Count == 0)
				return false;

			int compared = 0;
			bool strict = false;
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < stories.Count; i++)
			{
				StoryAppend story = stories[i];
				List<StoredRecord> mine = stored
					.Where(item => item.StoryId == story.StoryId)
					.OrderBy(item => item.Offset)
					.ToList();
				if (mine.Count > story.Records.Count)
					return false;

				for (int r = 0; r < mine.Count; r++)
				{
					if (!RecordCodec.SameDraft(story.Records[r], mine[r]))
						return false;
				}

				if (mine.Count < story.Records.Count)
					strict = true;

				compared += mine.Count;
				if (mine.Count > 0)
					seen.Add(story.StoryId);
			}

			if (compared != stored.Count)
				return false;

			for (int i = 0; i < stored.Count; i++)
			{
				if (!seen.Contains(stored[i].StoryId))
					return false;
			}

			return strict;
		}

		private static bool PayloadEquals(List<StoredRecord> stored, IReadOnlyList<StoryAppend> stories)
		{
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < stories.Count; i++)
			{
				StoryAppend story = stories[i];
				List<StoredRecord> mine = stored
					.Where(item => item.StoryId == story.StoryId)
					.OrderBy(item => item.Offset)
					.ToList();
				if (mine.Count != story.Records.Count)
					return false;

				for (int r = 0; r < mine.Count; r++)
				{
					if (!RecordCodec.SameDraft(story.Records[r], mine[r]))
						return false;
				}

				if (mine.Count > 0)
					seen.Add(story.StoryId);
			}

			for (int i = 0; i < stored.Count; i++)
			{
				if (!seen.Contains(stored[i].StoryId))
					return false;
			}

			return true;
		}

		private string? ValidateDrafts(AppendRequest request, List<StoredRecord> published)
		{
			if (request.Stories.Count == 0)
				return "An append needs at least one story.";

			bool anyRecord = false;
			for (int s = 0; s < request.Stories.Count; s++)
			{
				StoryAppend story = request.Stories[s];
				RequireToken(story.StoryId, nameof(story.StoryId));
				for (int i = 0; i < story.Records.Count; i++)
				{
					anyRecord = true;
					RecordDraft draft = story.Records[i];
					if (draft.Effect == EffectKind.CompactSand || draft.Effect == EffectKind.MovementSummary)
						return "Compaction goes through CompactSand so the generation rewrite stays explicit.";

					if (draft.DeclaresStoryId != null)
						RequireToken(draft.DeclaresStoryId, nameof(draft.DeclaresStoryId));

					if (draft.Effect == EffectKind.ReleaseClaim || draft.Effect == EffectKind.PromoteClaim)
					{
						string? problem = ValidateClaimEffect(draft, published);
						if (problem != null)
							return problem;
					}
				}
			}

			if (!anyRecord)
				return "An append needs at least one record.";

			return null;
		}

		private static string? ValidateClaimEffect(RecordDraft draft, List<StoredRecord> published)
		{
			string[] parts = (draft.EffectArgument ?? "").Split('|');
			if (parts.Length != 3)
				return "A claim effect argument is story|offset|content.";

			long offset;
			if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out offset))
				return "A claim offset is not an integer.";

			StoredRecord? claim = null;
			for (int i = 0; i < published.Count; i++)
			{
				if (published[i].StoryId == parts[0] && published[i].Offset == offset)
					claim = published[i];
			}

			if (claim == null)
				return "The claim record is not in the published edition.";

			if (claim.Retention != RetentionClass.Sand || claim.ContentId != parts[2])
				return "Only a sand content claim can be released or promoted.";

			for (int i = 0; i < published.Count; i++)
			{
				if (published[i].Effect == EffectKind.PromoteClaim && published[i].EffectArgument == draft.EffectArgument)
					return "A stone promise already covers this claim. Release is rejected.";

				if (published[i].Effect == EffectKind.ReleaseClaim
					&& ClaimKey(published[i].EffectArgument ?? "") == ClaimKey(draft.EffectArgument ?? ""))
					return "This claim was already released.";
			}

			if (draft.Retention != RetentionClass.Stone)
				return "Claim certificates are stone.";

			return null;
		}

		private List<ContentEntry> DescribeContent(List<StoredRecord> records, out bool anyErased)
		{
			anyErased = false;
			Dictionary<string, List<string>> claims = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			HashSet<string> released = new HashSet<string>(StringComparer.Ordinal);
			HashSet<string> promoted = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < records.Count; i++)
			{
				StoredRecord record = records[i];
				if (record.Effect == EffectKind.None && !string.IsNullOrWhiteSpace(record.ContentId))
				{
					string claimKey = record.StoryId + "|" + record.Offset.ToString(CultureInfo.InvariantCulture);
					if (!claims.ContainsKey(record.ContentId!))
						claims[record.ContentId!] = new List<string>();

					claims[record.ContentId!].Add(claimKey);
				}

				if (record.Effect == EffectKind.ReleaseClaim)
					released.Add(ClaimKey(record.EffectArgument));

				if (record.Effect == EffectKind.PromoteClaim)
					promoted.Add(ClaimKey(record.EffectArgument));
			}

			List<ContentEntry> entries = new List<ContentEntry>();
			foreach (KeyValuePair<string, List<string>> pair in claims)
			{
				bool live = false;
				for (int i = 0; i < pair.Value.Count; i++)
				{
					if (promoted.Contains(pair.Value[i]) || !released.Contains(pair.Value[i]))
						live = true;
				}

				ContentEntry entry = new ContentEntry();
				entry.ContentId = pair.Key;
				string path = LibraryLayout.ContentFile(_root, pair.Key);
				if (live)
				{
					entry.Disposition = "required";
					if (File.Exists(path))
					{
						byte[] bytes = File.ReadAllBytes(path);
						entry.ByteLength = bytes.Length;
						entry.Hash = Hashing.Sha256(bytes);
					}
					else
					{
						entry.ByteLength = -1;
						entry.Hash = "missing";
					}
				}
				else
				{
					entry.Disposition = "erased";
					anyErased = true;
					if (File.Exists(path))
					{
						byte[] bytes = File.ReadAllBytes(path);
						entry.ByteLength = bytes.Length;
						entry.Hash = Hashing.Sha256(bytes);
					}
					else
					{
						entry.ByteLength = 0;
						entry.Hash = "-";
					}
				}

				entries.Add(entry);
			}

			return entries;
		}

		private static string ClaimKey(string argument)
		{
			string[] parts = (argument ?? "").Split('|');
			if (parts.Length < 2)
				return argument ?? "";

			return parts[0] + "|" + parts[1];
		}
		#endregion
	}
}
