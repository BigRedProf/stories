using System.Globalization;
using System.Text;

namespace BigRedProf.Stories.LibrarySemanticsLab
{
	/// <summary>
	/// A publish path whose durable write is one journal frame for the stories
	/// that changed, plus the edition pointer. The edition id is the hash of the
	/// parent id and that delta. Replaying the journal to an id is one point in
	/// time. A checkpoint is a cached map, not a second identity.
	/// </summary>
	public sealed class JournalLibrary : IDisposable
	{
		#region constructors
		private JournalLibrary(string root, FileStream ownerLock, int checkpointEvery, View view)
		{
			_root = root;
			_lock = ownerLock;
			_checkpointEvery = checkpointEvery;
			_view = view;
			_gate = new object();
		}
		#endregion

		#region fields
		private readonly string _root;
		private readonly FileStream _lock;
		private readonly int _checkpointEvery;
		private readonly object _gate;
		private View _view;
		private int _storyExaminations;
		private bool _disposed;
		private bool _dead;
		#endregion

		#region properties
		public string Root
		{
			get
			{
				return _root;
			}
		}

		public string CurrentEditionId
		{
			get
			{
				lock (_gate)
				{
					RequireAlive();
					return _view.Id;
				}
			}
		}

		/// <summary>
		/// Story files examined while appending. One movement examines the
		/// stories in that movement, not the whole library.
		/// </summary>
		public int StoryExaminationCount
		{
			get
			{
				return _storyExaminations;
			}
		}
		#endregion

		#region functions
		public static JournalLibrary Create(string path, int checkpointEvery)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("A library path is required.", nameof(path));

			if (checkpointEvery < 1)
				throw new ArgumentOutOfRangeException(nameof(checkpointEvery));

			Directory.CreateDirectory(path);
			Directory.CreateDirectory(Path.Combine(path, "stories"));
			Directory.CreateDirectory(Path.Combine(path, "checkpoints"));
			File.WriteAllText(
				LibraryLayout.Description(path),
				"format=per-frame\npublish=journal\ncheckpoint=" + checkpointEvery.ToString(CultureInfo.InvariantCulture) + "\n"
			);
			using (FileStream journal = new FileStream(JournalPath(path), FileMode.Create, FileAccess.Write, FileShare.None))
				journal.Flush(true);

			File.WriteAllText(LibraryLayout.Pointer(path), "-\n");
			PosixSync.FsyncDirectory(path);
			return Open(path);
		}

		public static JournalLibrary Open(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("A library path is required.", nameof(path));

			int checkpointEvery = ReadCheckpointEvery(path);
			FileStream ownerLock = new FileStream(
				LibraryLayout.Lock(path),
				FileMode.OpenOrCreate,
				FileAccess.ReadWrite,
				FileShare.None,
				bufferSize: 1,
				FileOptions.None
			);
			try
			{
				JournalLibrary library = new JournalLibrary(path, ownerLock, checkpointEvery, View.Empty());
				library._view = library.LoadPublished();
				return library;
			}
			catch
			{
				ownerLock.Dispose();
				throw;
			}
		}
		#endregion

		#region methods
		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;
			_lock.Dispose();
		}

		public long HighWater(string storyId)
		{
			lock (_gate)
			{
				RequireAlive();
				return HighWaterOf(storyId);
			}
		}

		public AppendResult Append(AppendRequest request, FaultInjector? faults)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));

			lock (_gate)
			{
				try
				{
					RequireAlive();
					return AppendUnlocked(request, faults);
				}
				catch (CrashInjectedException)
				{
					_dead = true;
					throw;
				}
			}
		}

		public EditionView ReadEdition(string editionId)
		{
			if (string.IsNullOrWhiteSpace(editionId))
				throw new ArgumentException("An edition id is required.", nameof(editionId));

			lock (_gate)
			{
				RequireAlive();
				View view = ReplayTo(editionId);
				return ToEdition(view, true);
			}
		}

		/// <summary>
		/// Story records at the current edition's published length. Flushed bytes
		/// past that length are not returned.
		/// </summary>
		public IReadOnlyList<StoredRecord> ReadHead(string storyId)
		{
			if (string.IsNullOrWhiteSpace(storyId))
				throw new ArgumentException("A story id is required.", nameof(storyId));

			lock (_gate)
			{
				RequireAlive();
				if (!_view.Stories.TryGetValue(storyId, out StoryPrefix? prefix) || prefix.RecordCount == 0)
					return Array.Empty<StoredRecord>();

				return ReadPrefix(storyId, prefix);
			}
		}
		#endregion

		#region private methods
		private AppendResult AppendUnlocked(AppendRequest request, FaultInjector? faults)
		{
			if (request.Content.Count > 0)
				throw new InvalidOperationException("This journal prototype does not store content blobs.");

			RequireToken(request.IdempotencyKey);
			string bodyHash = RecordCodec.HashDrafts(request.Stories);
			if (_view.Keys.TryGetValue(request.IdempotencyKey, out KeyRecord? existing))
			{
				if (existing.BodyHash == bodyHash)
				{
					return new AppendResult(
						AppendStatus.AlreadyCommitted,
						existing.EditionId,
						"The key is already in a published edition. The retry did not append.",
						0
					);
				}

				return new AppendResult(
					AppendStatus.IdempotencyConflict,
					_view.Id,
					"The key was already committed with a different body.",
					0
				);
			}

			if (!request.SkipExpectedEditionCheck && request.ExpectedEditionId != _view.Id)
			{
				return new AppendResult(
					AppendStatus.PositionConflict,
					_view.Id,
					"Expected edition " + request.ExpectedEditionId + " but the library is at " + _view.Id + ".",
					0
				);
			}

			List<StoryAppend> ordered = request.Stories.OrderBy(item => item.StoryId, StringComparer.Ordinal).ToList();
			for (int i = 0; i < ordered.Count; i++)
			{
				StoryAppend story = ordered[i];
				RequireToken(story.StoryId);
				long high = HighWaterOf(story.StoryId);
				long expected = request.SkipExpectedOffsetCheck ? high : story.ExpectedOffset;
				if (expected != high)
				{
					return new AppendResult(
						AppendStatus.PositionConflict,
						_view.Id,
						"Story '" + story.StoryId + "' is at offset " + high.ToString(CultureInfo.InvariantCulture) + ".",
						0
					);
				}
			}

			TailKind[] tails = new TailKind[ordered.Count];
			for (int i = 0; i < ordered.Count; i++)
				tails[i] = InspectTail(ordered[i], request.IdempotencyKey, bodyHash);

			for (int i = 0; i < tails.Length; i++)
			{
				if (tails[i] == TailKind.Mismatch)
				{
					return new AppendResult(
						AppendStatus.IdempotencyConflict,
						_view.Id,
						"Unpublished frames for this key do not match the retry.",
						0
					);
				}
			}

			bool rewrite = false;
			for (int i = 0; i < tails.Length; i++)
			{
				if (tails[i] == TailKind.Other)
					rewrite = true;
			}

			int frames = 0;
			List<StoryPrefix> changed = new List<StoryPrefix>();
			for (int i = 0; i < ordered.Count; i++)
			{
				StoryAppend story = ordered[i];
				bool write = rewrite || tails[i] != TailKind.Match;
				if (write)
				{
					CutToPublished(story.StoryId);
					frames += WriteStory(story, request.IdempotencyKey, bodyHash, faults);
				}

				changed.Add(Describe(story.StoryId));
			}

			if (faults != null)
				faults.Hit(FaultPoint.AfterFrameBeforePublish);

			string canonical = Canonical(_view.Id, request.IdempotencyKey, bodyHash, changed);
			string editionId = Hashing.Sha256(canonical);
			byte[] payload = Encoding.UTF8.GetBytes(canonical + "id\t" + editionId + "\n");
			long end = JournalLog.Append(_root, payload);
			if (faults != null)
				faults.Hit(FaultPoint.AfterJournalBeforePointer);

			WritePointer(editionId);
			if (faults != null)
				faults.Hit(FaultPoint.AfterDirectoryFsync);

			JournalFrame frame = JournalLog.Parse(payload, end);
			Apply(_view, frame);
			if (_view.CommitCount % _checkpointEvery == 0)
				WriteCheckpoint(_view, faults);

			return new AppendResult(AppendStatus.Committed, editionId, "Published.", frames);
		}

		private TailKind InspectTail(StoryAppend story, string key, string bodyHash)
		{
			_storyExaminations++;
			string path = StoryPath(story.StoryId);
			if (!File.Exists(path))
				return TailKind.Empty;

			long publishedBytes = PublishedBytes(story.StoryId);
			long length = new FileInfo(path).Length;
			if (length < publishedBytes)
				throw new InvalidDataException("Story '" + story.StoryId + "' lost published bytes.");

			if (length == publishedBytes)
				return TailKind.Empty;

			LogImage image = StoryLog.Read(path);
			int publishedCount = PublishedCount(story.StoryId);
			if (image.Committed.Count < publishedCount)
				throw new InvalidDataException("Story '" + story.StoryId + "' lost published records.");

			int extra = image.Committed.Count - publishedCount;
			if (extra == 0)
				return TailKind.Other;

			bool sameKey = true;
			for (int i = publishedCount; i < image.Committed.Count; i++)
			{
				if (image.Committed[i].IdempotencyKey != key)
					sameKey = false;
			}

			if (!sameKey)
				return TailKind.Other;

			if (extra != story.Records.Count)
				return TailKind.Mismatch;

			for (int i = 0; i < story.Records.Count; i++)
			{
				StoredRecord stored = image.Committed[publishedCount + i];
				if (stored.BodyHash != bodyHash || !RecordCodec.SameDraft(story.Records[i], stored))
					return TailKind.Mismatch;
			}

			return image.TornTail ? TailKind.Other : TailKind.Match;
		}

		private void CutToPublished(string storyId)
		{
			string path = StoryPath(storyId);
			if (!File.Exists(path))
				return;

			long cut = _view.Stories.ContainsKey(storyId) ? PublishedBytes(storyId) : StoryLog.HeaderLength;
			long length = new FileInfo(path).Length;
			if (length < cut)
				throw new InvalidDataException("Story '" + storyId + "' lost published bytes.");

			if (length > cut)
				StoryLog.Truncate(path, cut);
		}

		private int WriteStory(StoryAppend story, string key, string bodyHash, FaultInjector? faults)
		{
			if (story.Records.Count == 0)
				return 0;

			string path = StoryPath(story.StoryId);
			if (!File.Exists(path))
				StoryLog.CreateEmpty(path, BatchFormat.PerFrame);

			long offset = HighWaterOf(story.StoryId);
			List<StoredRecord> stored = new List<StoredRecord>();
			for (int i = 0; i < story.Records.Count; i++)
			{
				stored.Add(ToStored(story.Records[i], story.StoryId, offset, key, bodyHash));
				offset++;
			}

			return StoryLog.AppendBatch(path, BatchFormat.PerFrame, key, bodyHash, stored, faults);
		}

		private StoryPrefix Describe(string storyId)
		{
			_storyExaminations++;
			string path = StoryPath(storyId);
			LogImage image = StoryLog.Trim(path, BatchFormat.PerFrame);
			StoryPrefix prefix = new StoryPrefix();
			prefix.StoryId = storyId;
			prefix.Generation = 0;
			prefix.RecordCount = image.Committed.Count;
			prefix.HighWater = 0;
			for (int i = 0; i < image.Committed.Count; i++)
			{
				long next = image.Committed[i].Offset + 1;
				if (next > prefix.HighWater)
					prefix.HighWater = next;
			}

			prefix.FileBytes = image.ValidLength;
			prefix.FileHash = Hashing.FilePrefix(path, image.ValidLength);
			prefix.Encoding = "lab";
			return prefix;
		}

		private View LoadPublished()
		{
			string pointer = File.ReadAllText(LibraryLayout.Pointer(_root)).Trim();
			long fileLength = new FileInfo(JournalPath(_root)).Length;
			if (pointer == "-")
			{
				if (fileLength != 0)
					TruncateJournal(0);

				return View.Empty();
			}

			View? checkpoint = LatestCheckpoint(fileLength);
			View view = checkpoint ?? View.Empty();
			long resume = view.JournalEnd;
			if (!Reach(view, resume, fileLength, pointer, out long publishedEnd))
			{
				view = View.Empty();
				if (!Reach(view, 0, fileLength, pointer, out publishedEnd))
					throw new InvalidDataException("Edition '" + pointer + "' is not in the journal.");
			}

			if (fileLength != publishedEnd)
				TruncateJournal(publishedEnd);

			return view;
		}

		private bool Reach(View view, long resume, long fileLength, string pointer, out long publishedEnd)
		{
			publishedEnd = view.Id == pointer ? view.JournalEnd : -1;
			if (publishedEnd >= 0)
				return true;

			List<JournalFrame> frames = JournalLog.ReadFrom(_root, resume, fileLength);
			for (int i = 0; i < frames.Count; i++)
			{
				Apply(view, frames[i]);
				if (view.Id == pointer)
				{
					publishedEnd = frames[i].EndOffset;
					return true;
				}
			}

			return false;
		}

		private View ReplayTo(string editionId)
		{
			if (editionId == "-")
				return View.Empty();

			List<CheckpointRef> checkpoints = ReadCheckpointIndex();
			for (int i = checkpoints.Count - 1; i >= 0; i--)
			{
				View view = LoadSnapshot(checkpoints[i].Id);
				if (view.Id == editionId)
					return view;

				List<JournalFrame> frames = JournalLog.ReadFrom(_root, view.JournalEnd, new FileInfo(JournalPath(_root)).Length);
				bool found = false;
				for (int f = 0; f < frames.Count; f++)
				{
					if (frames[f].ParentId != view.Id)
						break;

					Apply(view, frames[f]);
					if (view.Id == editionId)
					{
						found = true;
						break;
					}
				}

				if (found)
					return view;
			}

			View full = View.Empty();
			List<JournalFrame> all = JournalLog.ReadFrom(_root, 0, new FileInfo(JournalPath(_root)).Length);
			for (int i = 0; i < all.Count; i++)
			{
				Apply(full, all[i]);
				if (full.Id == editionId)
					return full;
			}

			throw new InvalidDataException("Edition '" + editionId + "' is not in the journal.");
		}

		private void WriteCheckpoint(View view, FaultInjector? faults)
		{
			string directory = Path.Combine(_root, "checkpoints");
			Directory.CreateDirectory(directory);
			string finalPath = Path.Combine(directory, view.Id + ".txt");
			string text = SnapshotText(view);
			if (!File.Exists(finalPath))
			{
				string tempPath = finalPath + ".new";
				using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
				{
					byte[] data = Encoding.UTF8.GetBytes(text);
					stream.Write(data, 0, data.Length);
					stream.Flush(true);
				}

				File.Move(tempPath, finalPath, true);
				PosixSync.FsyncDirectory(directory);
			}

			string index = CheckpointIndexPath(_root);
			string line = view.JournalEnd.ToString(CultureInfo.InvariantCulture) + "\t" + view.Id + "\n";
			if (faults != null && faults.ArmedPoint == FaultPoint.DuringCheckpointIndex)
			{
				using (FileStream stream = new FileStream(index, FileMode.Append, FileAccess.Write, FileShare.Read))
				{
					byte[] torn = Encoding.UTF8.GetBytes(line);
					int partial = Math.Max(1, torn.Length / 2);
					stream.Write(torn, 0, partial);
					stream.Flush(true);
				}

				faults.Hit(FaultPoint.DuringCheckpointIndex);
			}

			List<CheckpointRef> existing = ReadCheckpointIndex();
			StringBuilder rebuilt = new StringBuilder();
			for (int i = 0; i < existing.Count; i++)
			{
				rebuilt.Append(existing[i].End.ToString(CultureInfo.InvariantCulture));
				rebuilt.Append('\t');
				rebuilt.Append(existing[i].Id);
				rebuilt.Append('\n');
			}

			rebuilt.Append(line);
			string tempIndex = index + ".new";
			using (FileStream stream = new FileStream(tempIndex, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				byte[] data = Encoding.UTF8.GetBytes(rebuilt.ToString());
				stream.Write(data, 0, data.Length);
				stream.Flush(true);
			}

			File.Move(tempIndex, index, true);
			PosixSync.FsyncDirectory(directory);
		}

		private View? LatestCheckpoint(long fileLength)
		{
			List<CheckpointRef> checkpoints = ReadCheckpointIndex();
			for (int i = checkpoints.Count - 1; i >= 0; i--)
			{
				if (checkpoints[i].End > fileLength)
					continue;

				View view = LoadSnapshot(checkpoints[i].Id);
				if (view.JournalEnd == checkpoints[i].End)
					return view;
			}

			return null;
		}

		private List<CheckpointRef> ReadCheckpointIndex()
		{
			List<CheckpointRef> refs = new List<CheckpointRef>();
			string index = CheckpointIndexPath(_root);
			if (!File.Exists(index))
				return refs;

			string[] lines = File.ReadAllLines(index);
			for (int i = 0; i < lines.Length; i++)
			{
				if (lines[i].Length == 0)
					continue;

				bool last = i == lines.Length - 1;
				if (!TryParseCheckpointLine(lines[i], out CheckpointRef reference))
				{
					if (last)
						continue;

					throw new InvalidDataException("Checkpoint index is corrupt.");
				}

				string snapshot = Path.Combine(_root, "checkpoints", reference.Id + ".txt");
				if (!File.Exists(snapshot))
				{
					if (last)
						continue;

					throw new InvalidDataException("Checkpoint '" + reference.Id + "' is missing.");
				}

				refs.Add(reference);
			}

			return refs;
		}

		private View LoadSnapshot(string editionId)
		{
			string path = Path.Combine(_root, "checkpoints", editionId + ".txt");
			if (!File.Exists(path))
				throw new InvalidDataException("Checkpoint '" + editionId + "' is missing.");

			string[] lines = File.ReadAllLines(path);
			View view = View.Empty();
			view.Stories.Clear();
			for (int i = 0; i < lines.Length; i++)
			{
				string[] parts = lines[i].Split('\t');
				if (parts.Length < 2)
					throw new InvalidDataException("Checkpoint '" + editionId + "' is corrupt.");

				if (parts[0] == "id")
					view.Id = parts[1];
				else if (parts[0] == "parent")
					view.ParentId = parts[1];
				else if (parts[0] == "key")
					view.CommitKey = parts[1];
				else if (parts[0] == "body")
					view.BodyHash = parts[1];
				else if (parts[0] == "end")
					view.JournalEnd = long.Parse(parts[1], CultureInfo.InvariantCulture);
				else if (parts[0] == "count")
					view.CommitCount = int.Parse(parts[1], CultureInfo.InvariantCulture);
				else if (parts[0] == "story")
				{
					StoryPrefix prefix = ParseStory(parts);
					view.Stories[prefix.StoryId] = prefix;
				}
				else if (parts[0] == "commit")
				{
					if (parts.Length != 4)
						throw new InvalidDataException("Checkpoint '" + editionId + "' has a bad commit line.");

					KeyRecord record = new KeyRecord();
					record.BodyHash = parts[2];
					record.EditionId = parts[3];
					view.Keys[parts[1]] = record;
				}
				else
				{
					throw new InvalidDataException("Checkpoint '" + editionId + "' has an unknown line.");
				}
			}

			if (view.Id != editionId)
				throw new InvalidDataException("Checkpoint file does not match '" + editionId + "'.");

			return view;
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
		}

		private void TruncateJournal(long length)
		{
			using (FileStream stream = new FileStream(JournalPath(_root), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
			{
				stream.SetLength(length);
				stream.Flush(true);
			}
		}

		private EditionView ToEdition(View view, bool loadRecords)
		{
			EditionView edition = new EditionView();
			edition.Id = view.Id;
			edition.ParentId = view.ParentId;
			edition.CommitKey = view.CommitKey;
			edition.BodyHash = view.BodyHash;
			List<StoryPrefix> stories = view.Stories.Values.OrderBy(item => item.StoryId, StringComparer.Ordinal).ToList();
			edition.Stories = stories;
			if (!loadRecords || view.Id == "-")
			{
				edition.Records = Array.Empty<StoredRecord>();
				return edition;
			}

			List<StoredRecord> records = new List<StoredRecord>();
			for (int i = 0; i < stories.Count; i++)
				records.AddRange(ReadPrefix(stories[i].StoryId, stories[i]));

			edition.Records = records;
			return edition;
		}

		private IReadOnlyList<StoredRecord> ReadPrefix(string storyId, StoryPrefix prefix)
		{
			string path = StoryPath(storyId);
			LogImage image = StoryLog.ReadThrough(path, prefix.FileBytes);
			if (image.TornTail || image.ValidLength != prefix.FileBytes || image.Committed.Count != prefix.RecordCount)
				throw new InvalidDataException("The published prefix of '" + storyId + "' does not match the edition.");

			string hash = Hashing.FilePrefix(path, prefix.FileBytes);
			if (hash != prefix.FileHash)
				throw new InvalidDataException("The published prefix of '" + storyId + "' does not match the edition hash.");

			for (int i = 0; i < image.Committed.Count; i++)
				image.Committed[i].StoryId = storyId;

			return image.Committed;
		}

		private long HighWaterOf(string storyId)
		{
			if (!_view.Stories.TryGetValue(storyId, out StoryPrefix? prefix))
				return 0;

			return prefix.HighWater;
		}

		private long PublishedBytes(string storyId)
		{
			if (!_view.Stories.TryGetValue(storyId, out StoryPrefix? prefix))
				return 0;

			return prefix.FileBytes;
		}

		private int PublishedCount(string storyId)
		{
			if (!_view.Stories.TryGetValue(storyId, out StoryPrefix? prefix))
				return 0;

			return prefix.RecordCount;
		}

		private string StoryPath(string storyId)
		{
			return LibraryLayout.Generation(_root, storyId, 0);
		}

		private void RequireAlive()
		{
			if (_disposed || _dead)
				throw new ObjectDisposedException(nameof(JournalLibrary));
		}
		#endregion

		#region private functions
		private static bool TryParseCheckpointLine(string line, out CheckpointRef reference)
		{
			reference = new CheckpointRef();
			string[] parts = line.Split('\t');
			if (parts.Length != 2)
				return false;

			if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long end))
				return false;

			if (parts[1].Length != 64)
				return false;

			for (int i = 0; i < parts[1].Length; i++)
			{
				char character = parts[1][i];
				bool hex = (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f');
				if (!hex)
					return false;
			}

			reference.End = end;
			reference.Id = parts[1];
			return true;
		}

		private static int ReadCheckpointEvery(string path)
		{
			string text = File.ReadAllText(LibraryLayout.Description(path));
			bool journal = false;
			int every = 1000;
			string[] lines = text.Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				if (lines[i].StartsWith("publish=", StringComparison.Ordinal))
					journal = lines[i] == "publish=journal";

				if (lines[i].StartsWith("checkpoint=", StringComparison.Ordinal))
					every = int.Parse(lines[i].Substring("checkpoint=".Length), CultureInfo.InvariantCulture);
			}

			if (!journal)
				throw new InvalidDataException("'" + path + "' is not a journal library.");

			return every;
		}

		private static void Apply(View view, JournalFrame frame)
		{
			if (frame.ParentId != view.Id)
				throw new InvalidDataException("Journal frame parent does not match the previous edition.");

			string canonical = Canonical(frame.ParentId, frame.Key, frame.BodyHash, frame.Stories);
			if (Hashing.Sha256(canonical) != frame.Id)
				throw new InvalidDataException("Journal frame does not hash to its edition id.");

			view.ParentId = frame.ParentId;
			view.CommitKey = frame.Key;
			view.BodyHash = frame.BodyHash;
			view.Id = frame.Id;
			view.JournalEnd = frame.EndOffset;
			view.CommitCount++;
			KeyRecord key = new KeyRecord();
			key.BodyHash = frame.BodyHash;
			key.EditionId = frame.Id;
			view.Keys[frame.Key] = key;
			for (int i = 0; i < frame.Stories.Count; i++)
				view.Stories[frame.Stories[i].StoryId] = frame.Stories[i];
		}

		private static string Canonical(string parentId, string key, string bodyHash, IReadOnlyList<StoryPrefix> stories)
		{
			StringBuilder builder = new StringBuilder();
			builder.Append("parent\t");
			builder.Append(parentId);
			builder.Append('\n');
			builder.Append("key\t");
			builder.Append(key);
			builder.Append('\n');
			builder.Append("body\t");
			builder.Append(bodyHash);
			builder.Append('\n');
			List<StoryPrefix> ordered = stories.OrderBy(item => item.StoryId, StringComparer.Ordinal).ToList();
			for (int i = 0; i < ordered.Count; i++)
				AppendStory(builder, ordered[i]);

			return builder.ToString();
		}

		private static string SnapshotText(View view)
		{
			StringBuilder builder = new StringBuilder();
			builder.Append("id\t");
			builder.Append(view.Id);
			builder.Append('\n');
			builder.Append("parent\t");
			builder.Append(view.ParentId);
			builder.Append('\n');
			builder.Append("key\t");
			builder.Append(view.CommitKey);
			builder.Append('\n');
			builder.Append("body\t");
			builder.Append(view.BodyHash);
			builder.Append('\n');
			builder.Append("end\t");
			builder.Append(view.JournalEnd.ToString(CultureInfo.InvariantCulture));
			builder.Append('\n');
			builder.Append("count\t");
			builder.Append(view.CommitCount.ToString(CultureInfo.InvariantCulture));
			builder.Append('\n');
			List<StoryPrefix> stories = view.Stories.Values.OrderBy(item => item.StoryId, StringComparer.Ordinal).ToList();
			for (int i = 0; i < stories.Count; i++)
				AppendStory(builder, stories[i]);

			List<string> keys = view.Keys.Keys.OrderBy(item => item, StringComparer.Ordinal).ToList();
			for (int i = 0; i < keys.Count; i++)
			{
				KeyRecord record = view.Keys[keys[i]];
				builder.Append("commit\t");
				builder.Append(keys[i]);
				builder.Append('\t');
				builder.Append(record.BodyHash);
				builder.Append('\t');
				builder.Append(record.EditionId);
				builder.Append('\n');
			}

			return builder.ToString();
		}

		private static void AppendStory(StringBuilder builder, StoryPrefix story)
		{
			builder.Append("story\t");
			builder.Append(story.StoryId);
			builder.Append('\t');
			builder.Append(story.Generation.ToString(CultureInfo.InvariantCulture));
			builder.Append('\t');
			builder.Append(story.RecordCount.ToString(CultureInfo.InvariantCulture));
			builder.Append('\t');
			builder.Append(story.HighWater.ToString(CultureInfo.InvariantCulture));
			builder.Append('\t');
			builder.Append(story.FileBytes.ToString(CultureInfo.InvariantCulture));
			builder.Append('\t');
			builder.Append(story.FileHash);
			builder.Append('\n');
		}

		private static StoryPrefix ParseStory(string[] parts)
		{
			if (parts.Length != 7)
				throw new InvalidDataException("A story line is missing fields.");

			StoryPrefix prefix = new StoryPrefix();
			prefix.StoryId = parts[1];
			prefix.Generation = int.Parse(parts[2], CultureInfo.InvariantCulture);
			prefix.RecordCount = int.Parse(parts[3], CultureInfo.InvariantCulture);
			prefix.HighWater = long.Parse(parts[4], CultureInfo.InvariantCulture);
			prefix.FileBytes = long.Parse(parts[5], CultureInfo.InvariantCulture);
			prefix.FileHash = parts[6];
			prefix.Encoding = "lab";
			return prefix;
		}

		private static StoredRecord ToStored(RecordDraft draft, string storyId, long offset, string key, string bodyHash)
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

		private static void RequireToken(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				throw new ArgumentException("A value is required.");

			if (value.IndexOf('\t') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
				throw new ArgumentException("The value cannot contain tabs or newlines.");
		}

		private static string JournalPath(string root)
		{
			return Path.Combine(root, "journal.bin");
		}

		private static string CheckpointIndexPath(string root)
		{
			return Path.Combine(root, "checkpoints", "index.txt");
		}
		#endregion

		#region private types
		private enum TailKind
		{
			Empty = 0,
			Match = 1,
			Mismatch = 2,
			Other = 3
		}

		private sealed class KeyRecord
		{
			public string BodyHash
			{
				get;
				set;
			} = "";

			public string EditionId
			{
				get;
				set;
			} = "";
		}

		private struct CheckpointRef
		{
			public long End;
			public string Id;
		}

		private sealed class View
		{
			public string Id = "-";
			public string ParentId = "";
			public string CommitKey = "";
			public string BodyHash = "";
			public long JournalEnd;
			public int CommitCount;
			public Dictionary<string, StoryPrefix> Stories = new Dictionary<string, StoryPrefix>(StringComparer.Ordinal);
			public Dictionary<string, KeyRecord> Keys = new Dictionary<string, KeyRecord>(StringComparer.Ordinal);

			public static View Empty()
			{
				return new View();
			}
		}
		#endregion
	}

	internal static class JournalLog
	{
		#region functions
		public static long Append(string root, byte[] payload)
		{
			string path = Path.Combine(root, "journal.bin");
			using (FileStream stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
			{
				stream.Position = stream.Length;
				WriteFrame(stream, payload);
				return stream.Length;
			}
		}

		public static List<JournalFrame> ReadFrom(string root, long start, long limit)
		{
			List<JournalFrame> frames = new List<JournalFrame>();
			string path = Path.Combine(root, "journal.bin");
			if (!File.Exists(path))
				return frames;

			using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				if (limit > stream.Length)
					limit = stream.Length;

				if (start < 0 || start > limit)
					throw new ArgumentOutOfRangeException(nameof(start));

				stream.Position = start;
				while (stream.Position < limit)
				{
					long at = stream.Position;
					byte[]? payload = TryReadFrame(stream, limit, at);
					if (payload == null)
						break;

					frames.Add(Parse(payload, stream.Position));
				}
			}

			return frames;
		}

		public static JournalFrame Parse(byte[] payload, long endOffset)
		{
			string text = Encoding.UTF8.GetString(payload);
			int idAt = text.LastIndexOf("\nid\t", StringComparison.Ordinal);
			if (idAt < 0)
				throw new InvalidDataException("A journal frame has no edition id.");

			string canonical = text.Substring(0, idAt + 1);
			string id = text.Substring(idAt + 4).Trim();
			if (Hashing.Sha256(canonical) != id)
				throw new InvalidDataException("A journal frame does not hash to its edition id.");

			JournalFrame frame = new JournalFrame();
			frame.Id = id;
			frame.EndOffset = endOffset;
			string[] lines = canonical.Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				if (lines[i].Length == 0)
					continue;

				string[] parts = lines[i].Split('\t');
				if (parts[0] == "parent" && parts.Length == 2)
					frame.ParentId = parts[1];
				else if (parts[0] == "key" && parts.Length == 2)
					frame.Key = parts[1];
				else if (parts[0] == "body" && parts.Length == 2)
					frame.BodyHash = parts[1];
				else if (parts[0] == "story")
					frame.Stories.Add(ParseStoryLine(parts));
				else
					throw new InvalidDataException("A journal frame has an unknown line.");
			}

			return frame;
		}
		#endregion

		#region private functions
		private static StoryPrefix ParseStoryLine(string[] parts)
		{
			if (parts.Length != 7)
				throw new InvalidDataException("A journal story line is missing fields.");

			StoryPrefix prefix = new StoryPrefix();
			prefix.StoryId = parts[1];
			prefix.Generation = int.Parse(parts[2], CultureInfo.InvariantCulture);
			prefix.RecordCount = int.Parse(parts[3], CultureInfo.InvariantCulture);
			prefix.HighWater = long.Parse(parts[4], CultureInfo.InvariantCulture);
			prefix.FileBytes = long.Parse(parts[5], CultureInfo.InvariantCulture);
			prefix.FileHash = parts[6];
			prefix.Encoding = "lab";
			return prefix;
		}

		private static void WriteFrame(FileStream stream, byte[] payload)
		{
			byte[] frame = new byte[4 + payload.Length + 4];
			frame[0] = (byte)payload.Length;
			frame[1] = (byte)(payload.Length >> 8);
			frame[2] = (byte)(payload.Length >> 16);
			frame[3] = (byte)(payload.Length >> 24);
			Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
			uint crc = Crc32.Compute(payload);
			int crcAt = 4 + payload.Length;
			frame[crcAt] = (byte)crc;
			frame[crcAt + 1] = (byte)(crc >> 8);
			frame[crcAt + 2] = (byte)(crc >> 16);
			frame[crcAt + 3] = (byte)(crc >> 24);
			stream.Write(frame, 0, frame.Length);
			stream.Flush(true);
		}

		private static byte[]? TryReadFrame(Stream stream, long limit, long start)
		{
			if (limit - start < 4)
				return null;

			byte[] lengthBytes = new byte[4];
			if (!TryRead(stream, lengthBytes))
				return null;

			uint payloadLength = (uint)(lengthBytes[0] | (lengthBytes[1] << 8) | (lengthBytes[2] << 16) | (lengthBytes[3] << 24));
			if (payloadLength == 0 || payloadLength > 64 * 1024 * 1024)
			{
				if (start + 4L + payloadLength >= limit)
					return null;

				throw new InvalidDataException("The journal is corrupt at offset " + start.ToString(CultureInfo.InvariantCulture) + ".");
			}

			long frameLength = 4L + payloadLength + 4L;
			if (start + frameLength > limit)
				return null;

			byte[] payload = new byte[payloadLength];
			if (!TryRead(stream, payload))
				return null;

			byte[] crcBytes = new byte[4];
			if (!TryRead(stream, crcBytes))
				return null;

			uint actual = Crc32.Compute(payload);
			uint expected = (uint)(crcBytes[0] | (crcBytes[1] << 8) | (crcBytes[2] << 16) | (crcBytes[3] << 24));
			if (actual != expected)
			{
				if (start + frameLength >= limit)
					return null;

				throw new InvalidDataException("The journal checksum failed at offset " + start.ToString(CultureInfo.InvariantCulture) + ".");
			}

			return payload;
		}

		private static bool TryRead(Stream stream, byte[] buffer)
		{
			int read = 0;
			while (read < buffer.Length)
			{
				int got = stream.Read(buffer, read, buffer.Length - read);
				if (got == 0)
					return false;

				read += got;
			}

			return true;
		}
		#endregion
	}

	internal sealed class JournalFrame
	{
		#region properties
		public string ParentId
		{
			get;
			set;
		} = "";

		public string Key
		{
			get;
			set;
		} = "";

		public string BodyHash
		{
			get;
			set;
		} = "";

		public string Id
		{
			get;
			set;
		} = "";

		public long EndOffset
		{
			get;
			set;
		}

		public List<StoryPrefix> Stories
		{
			get;
			set;
		} = new List<StoryPrefix>();
		#endregion
	}
}
