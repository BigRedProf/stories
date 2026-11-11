using System.Globalization;
using System.Text;

namespace BigRedProf.Stories.LibrarySemanticsLab
{
	internal static class LibraryLayout
	{
		#region functions
		public static string Pointer(string root)
		{
			return Path.Combine(root, "edition-current.txt");
		}

		public static string Manifest(string root, string id)
		{
			return Path.Combine(root, "editions", id + ".txt");
		}

		public static string StoryDirectory(string root, string storyId)
		{
			return Path.Combine(root, "stories", Hashing.Sha256(storyId));
		}

		public static string Generation(string root, string storyId, int generation)
		{
			return Path.Combine(StoryDirectory(root, storyId), "gen-" + generation.ToString(CultureInfo.InvariantCulture) + ".bin");
		}

		public static string ContentDirectory(string root)
		{
			return Path.Combine(root, "content");
		}

		public static string ContentFile(string root, string contentId)
		{
			return Path.Combine(ContentDirectory(root), contentId + ".bin");
		}

		public static string Description(string root)
		{
			return Path.Combine(root, "library.txt");
		}

		public static string Lock(string root)
		{
			return Path.Combine(root, "writer.lock");
		}

		public static string Pin(string root, string editionId)
		{
			return Path.Combine(root, "pins", editionId);
		}
		#endregion
	}

	/// <summary>
	/// A directory library. One process holds <c>writer.lock</c>. Publication is
	/// the rename of <c>edition-current.txt</c>. Story frames can be durable
	/// before that rename; edition readers do not see them until it lands.
	/// </summary>
	public sealed partial class Library : IDisposable
	{
		#region fields
		private readonly string _root;
		private readonly FileStream? _lock;
		private readonly object _gate;
		private readonly BatchFormat _format;
		private readonly Dictionary<string, StoryPrefix> _forcedPrefixes;
		private int _directorySyncs;
		private bool _disposed;
		private bool _dead;
		#endregion

		#region constructors
		private Library(string root, FileStream? ownerLock, BatchFormat format)
		{
			_root = root;
			_lock = ownerLock;
			_format = format;
			_forcedPrefixes = new Dictionary<string, StoryPrefix>(StringComparer.Ordinal);
			_gate = new object();
		}
		#endregion

		#region properties
		public string Root
		{
			get
			{
				return _root;
			}
		}

		public BatchFormat Format
		{
			get
			{
				return _format;
			}
		}

		public int DirectorySyncCount
		{
			get
			{
				return _directorySyncs;
			}
		}

		public string CurrentEditionId
		{
			get
			{
				lock (_gate)
				{
					RequireAlive();
					return ReadPointer();
				}
			}
		}
		#endregion

		#region functions
		public static string ContentIdFor(BigRedProf.Data.Core.Code code)
		{
			return ContentFiles.IdFor(code);
		}

		public static Library Create(string path, BatchFormat format)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("A library path is required.", nameof(path));

			Directory.CreateDirectory(path);
			Directory.CreateDirectory(Path.Combine(path, "editions"));
			Directory.CreateDirectory(Path.Combine(path, "stories"));
			Directory.CreateDirectory(LibraryLayout.ContentDirectory(path));
			File.WriteAllText(LibraryLayout.Description(path), "format=" + FormatName(format) + "\n");
			string emptyId = ManifestText.EmptyId();
			Directory.CreateDirectory(Path.Combine(path, "editions"));
			File.WriteAllText(LibraryLayout.Manifest(path, emptyId), ManifestText.EmptyText());
			File.WriteAllText(LibraryLayout.Pointer(path), emptyId + "\n");
			return Open(path);
		}

		public static string PeekEditionId(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("A library path is required.", nameof(path));

			return File.ReadAllText(LibraryLayout.Pointer(path)).Trim();
		}

		public static Library Open(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("A library path is required.", nameof(path));

			bool skipLock = Environment.GetEnvironmentVariable("BRP_LAB_SKIP_WRITER_LOCK") == "1";
			FileStream? ownerLock = null;
			if (!skipLock)
			{
				try
				{
					ownerLock = new FileStream(
						LibraryLayout.Lock(path),
						FileMode.OpenOrCreate,
						FileAccess.ReadWrite,
						FileShare.None,
						bufferSize: 1,
						FileOptions.None
					);
				}
				catch (IOException exception)
				{
					throw new IOException(
						"Library '" + path + "' already has a writer.",
						exception
					);
				}
			}

			try
			{
				BatchFormat format = ReadFormat(path);
				Library library = new Library(path, ownerLock, format);
				library.RepairTornTails();
				library.ReclaimErasedContent();
				return library;
			}
			catch
			{
				if (ownerLock != null)
					ownerLock.Dispose();

				throw;
			}
		}

		public static string FormatName(BatchFormat format)
		{
			if (format == BatchFormat.PerFrame)
				return "per-frame";

			if (format == BatchFormat.CommitMarker)
				return "commit-marker";

			return "length-prefixed";
		}

		public static BatchFormat ParseFormat(string name)
		{
			if (name == "per-frame")
				return BatchFormat.PerFrame;

			if (name == "commit-marker")
				return BatchFormat.CommitMarker;

			if (name == "length-prefixed")
				return BatchFormat.LengthPrefixed;

			throw new InvalidDataException("Unknown lab library format '" + name + "'.");
		}
		#endregion

		#region methods
		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;
			if (_lock != null)
				_lock.Dispose();
		}

		public long HighWater(string storyId)
		{
			lock (_gate)
			{
				RequireAlive();
				return HighWaterUnlocked(storyId);
			}
		}

		public string ByteStamp(string editionId)
		{
			lock (_gate)
			{
				RequireAlive();
				string current = ReadPointer();
				if (current != editionId)
					return "moved:" + current;

				ParsedManifest manifest = ReadManifest(editionId);
				StringBuilder builder = new StringBuilder();
				builder.Append(editionId);
				for (int i = 0; i < manifest.Stories.Count; i++)
				{
					StoryPrefix story = manifest.Stories[i];
					string path = LibraryLayout.Generation(_root, story.StoryId, story.Generation);
					string hash = File.Exists(path) ? Hashing.FilePrefix(path, story.FileBytes) : "missing";
					builder.Append('\n');
					builder.Append(story.StoryId);
					builder.Append('=');
					builder.Append(hash);
				}

				for (int i = 0; i < manifest.Content.Count; i++)
				{
					ContentEntry entry = manifest.Content[i];
					string path = LibraryLayout.ContentFile(_root, entry.ContentId);
					string hash = "absent";
					if (File.Exists(path))
						hash = Hashing.Sha256(File.ReadAllBytes(path));

					builder.Append('\n');
					builder.Append(entry.ContentId);
					builder.Append('=');
					builder.Append(hash);
				}

				return builder.ToString();
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

		public EditionView ReadCurrent()
		{
			lock (_gate)
			{
				RequireAlive();
				return ReadEditionUnlocked(ReadPointer(), requireHash: true);
			}
		}

		public EditionView ReadEdition(string editionId)
		{
			if (string.IsNullOrWhiteSpace(editionId))
				throw new ArgumentException("An edition id is required.", nameof(editionId));

			lock (_gate)
			{
				RequireAlive();
				return ReadEditionUnlocked(editionId, requireHash: true);
			}
		}

		public IReadOnlyList<StoredRecord> ReadCommitted(string storyId)
		{
			lock (_gate)
			{
				RequireAlive();
				return ReadCommittedUnlocked(storyId);
			}
		}

		public IReadOnlyList<StoredRecord> ReadPhysical(string storyId)
		{
			lock (_gate)
			{
				RequireAlive();
				string? path = StoryFile(storyId);
				if (path == null || !File.Exists(path))
					return Array.Empty<StoredRecord>();

				LogImage image = StoryLog.Read(path);
				List<StoredRecord> records = new List<StoredRecord>();
				for (int i = 0; i < image.Physical.Count; i++)
				{
					image.Physical[i].StoryId = storyId;
					records.Add(image.Physical[i]);
				}

				return records;
			}
		}

		public void DiscardUnpublished()
		{
			lock (_gate)
			{
				RequireAlive();
				TruncateQuarantine();
				DeleteStrayGenerations();
			}
		}

		public string PublishedStoryPath(string storyId)
		{
			lock (_gate)
			{
				RequireAlive();
				StoryPrefix? prefix = FindPrefix(ReadManifest(ReadPointer()), storyId);
				if (prefix == null)
					throw new InvalidOperationException("Story '" + storyId + "' is not in the current edition.");

				return LibraryLayout.Generation(_root, storyId, prefix.Generation);
			}
		}

		public string ContentFilePath(string contentId)
		{
			return LibraryLayout.ContentFile(_root, contentId);
		}

		public AppendResult AdoptShippedFile(
			string storyId,
			byte[] fileBytes,
			int recordCount,
			long highWater,
			string idempotencyKey,
			string expectedEditionId
		)
		{
			if (fileBytes == null)
				throw new ArgumentNullException(nameof(fileBytes));

			if (recordCount < 0 || highWater < recordCount)
				throw new ArgumentOutOfRangeException(nameof(recordCount));

			lock (_gate)
			{
				RequireAlive();
				string currentId = ReadPointer();
				ParsedManifest current = ReadManifest(currentId);
				string fileHash = Hashing.Sha256(fileBytes);
				if (current.CommitKey == idempotencyKey)
				{
					if (current.BodyHash == fileHash)
					{
						return new AppendResult(
							AppendStatus.AlreadyCommitted,
							currentId,
							"The shipped file is already the published edition.",
							0
						);
					}

					return new AppendResult(
						AppendStatus.IdempotencyConflict,
						currentId,
						"This key adopted different bytes.",
						0
					);
				}

				if (expectedEditionId != currentId)
				{
					return new AppendResult(
						AppendStatus.PositionConflict,
						currentId,
						"Expected edition " + expectedEditionId + " but the library is at " + currentId + ".",
						0
					);
				}

				int generation = GenerationOf(storyId);
				string path = LibraryLayout.Generation(_root, storyId, generation);
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
				{
					stream.Write(fileBytes, 0, fileBytes.Length);
					stream.Flush(true);
				}

				StoryPrefix prefix = new StoryPrefix();
				prefix.StoryId = storyId;
				prefix.Generation = generation;
				prefix.RecordCount = recordCount;
				prefix.HighWater = highWater;
				prefix.FileBytes = fileBytes.Length;
				prefix.FileHash = fileHash;
				prefix.Encoding = "shipped-v1";
				_forcedPrefixes[storyId] = prefix;
				try
				{
					return PublishExisting(
						idempotencyKey,
						fileHash,
						Array.Empty<ContentWrite>(),
						null,
						0,
						new string[] { storyId },
						null
					);
				}
				finally
				{
					_forcedPrefixes.Remove(storyId);
				}
			}
		}

		public void PublishImported(string manifestText, FaultInjector? faults)
		{
			if (manifestText == null)
				throw new ArgumentNullException(nameof(manifestText));

			lock (_gate)
			{
				try
				{
					RequireAlive();
					ParsedManifest manifest = ManifestText.Parse(manifestText);
					for (int i = 0; i < manifest.Stories.Count; i++)
					{
						StoryPrefix story = manifest.Stories[i];
						string path = LibraryLayout.Generation(_root, story.StoryId, story.Generation);
						if (!File.Exists(path))
							throw new InvalidDataException("Imported story '" + story.StoryId + "' is missing.");

						string hash = Hashing.FilePrefix(path, story.FileBytes);
						if (hash != story.FileHash)
							throw new InvalidDataException("Imported story '" + story.StoryId + "' hash does not match.");
					}

					for (int i = 0; i < manifest.Content.Count; i++)
					{
						ContentEntry entry = manifest.Content[i];
						string path = LibraryLayout.ContentFile(_root, entry.ContentId);
						if (entry.Disposition == "required")
						{
							if (!File.Exists(path))
								throw new InvalidDataException("Imported content " + entry.ContentId + " is missing.");

							string hash = Hashing.Sha256(File.ReadAllBytes(path));
							if (hash != entry.Hash)
								throw new InvalidDataException("Imported content " + entry.ContentId + " hash does not match.");
						}
					}

					WriteManifest(manifest);
					if (faults != null)
						faults.Hit(FaultPoint.BeforeDestinationPointer);

					WritePointer(manifest.Id);
					if (faults != null)
						faults.Hit(FaultPoint.AfterDirectoryFsync);

					ReclaimErasedContent();
					if (faults != null)
						faults.Hit(FaultPoint.AfterDestinationPointer);
				}
				catch (CrashInjectedException)
				{
					_dead = true;
					throw;
				}
			}
		}
		#endregion

		#region private methods
		private AppendResult AppendUnlocked(AppendRequest request, FaultInjector? faults)
		{
			RequireToken(request.IdempotencyKey, nameof(request.IdempotencyKey));
			string bodyHash = RecordCodec.HashDrafts(request.Stories);
			string currentId = ReadPointer();
			List<StoredRecord> published = ReadAllPublished();
			KeyDisposition publishedKey = Classify(published, request, bodyHash);
			if (publishedKey == KeyDisposition.Same)
			{
				return new AppendResult(
					AppendStatus.AlreadyCommitted,
					FindEditionForKey(request.IdempotencyKey, bodyHash, currentId),
					"The key is already in a published edition. The retry did not append.",
					0
				);
			}

			if (publishedKey == KeyDisposition.Different)
			{
				return new AppendResult(
					AppendStatus.IdempotencyConflict,
					currentId,
					"The key was already committed with a different body.",
					0
				);
			}

			List<StoredRecord> quarantine = ReadQuarantine(StoryIds(request));
			KeyDisposition quarantineKey = Classify(quarantine, request, bodyHash);
			if (quarantineKey == KeyDisposition.Same)
			{
				return PublishExisting(
					request.IdempotencyKey,
					bodyHash,
					request.Content,
					faults,
					0,
					StoryIds(request),
					null
				);
			}

			if (quarantineKey == KeyDisposition.Different)
			{
				return new AppendResult(
					AppendStatus.IdempotencyConflict,
					currentId,
					"Unpublished frames for this key do not match the retry.",
					0
				);
			}

			if (quarantine.Count > 0)
			{
				TruncateQuarantine();
				TruncateUnpublished(StoryIds(request));
				DeleteStrayGenerations();
			}

			if (!request.SkipExpectedEditionCheck && request.ExpectedEditionId != currentId)
			{
				return new AppendResult(
					AppendStatus.PositionConflict,
					currentId,
					"Expected edition " + request.ExpectedEditionId + " but the library is at " + currentId + ".",
					0
				);
			}

			string? rejection = ValidateDrafts(request, published);
			if (rejection != null)
			{
				return new AppendResult(AppendStatus.Rejected, currentId, rejection, 0);
			}

			List<StoryAppend> ordered = request.Stories.OrderBy(item => item.StoryId, StringComparer.Ordinal).ToList();
			for (int s = 0; s < ordered.Count; s++)
			{
				StoryAppend story = ordered[s];
				long high = HighWaterUnlocked(story.StoryId);
				long expected = story.ExpectedOffset;
				if (request.SkipExpectedOffsetCheck)
					expected = high;

				if (expected != high)
				{
					return new AppendResult(
						AppendStatus.PositionConflict,
						currentId,
						"Story '" + story.StoryId + "' is at offset " + high.ToString(CultureInfo.InvariantCulture) + ".",
						0
					);
				}
			}

			string? pauseDirectory = Environment.GetEnvironmentVariable("BRP_LAB_PAUSE_BEFORE_WRITE");
			if (!string.IsNullOrWhiteSpace(pauseDirectory))
			{
				Directory.CreateDirectory(pauseDirectory);
				File.WriteAllText(Path.Combine(pauseDirectory, Environment.ProcessId.ToString() + ".ready"), "ready");
				DateTime deadline = DateTime.UtcNow.AddSeconds(15);
				while (!File.Exists(Path.Combine(pauseDirectory, "go")))
				{
					if (DateTime.UtcNow > deadline)
						throw new TimeoutException("The pause-before-write gate was not opened.");

					Thread.Sleep(20);
				}
			}

			WriteContent(request.Content);
			int frames = 0;
			for (int s = 0; s < ordered.Count; s++)
			{
				StoryAppend story = ordered[s];
				if (story.Records.Count == 0)
					continue;

				List<StoredRecord> stored = new List<StoredRecord>();
				long offset = HighWaterUnlocked(story.StoryId);
				if (!request.SkipExpectedOffsetCheck)
					offset = story.ExpectedOffset;
				for (int i = 0; i < story.Records.Count; i++)
				{
					stored.Add(ToStored(story.Records[i], story.StoryId, offset, request.IdempotencyKey, bodyHash));
					offset++;
				}

				int generation = GenerationOf(story.StoryId);
				string path = LibraryLayout.Generation(_root, story.StoryId, generation);
				if (!File.Exists(path))
					StoryLog.CreateEmpty(path, _format);

				frames += StoryLog.AppendBatch(path, _format, request.IdempotencyKey, bodyHash, stored, faults);
				if (faults != null)
					faults.Hit("after-story:" + story.StoryId);
			}

			return PublishExisting(
				request.IdempotencyKey,
				bodyHash,
				request.Content,
				faults,
				frames,
				StoryIds(request),
				null
			);
		}

		private AppendResult PublishExisting(
			string key,
			string bodyHash,
			IReadOnlyList<ContentWrite> content,
			FaultInjector? faults,
			int frames,
			IReadOnlyList<string> extraStoryIds,
			Dictionary<string, int>? generationOverrides
		)
		{
			if (faults != null)
				faults.Hit(FaultPoint.AfterFrameBeforePublish);

			WriteContent(content);
			ParsedManifest manifest = DescribeUnlocked(ReadPointer(), key, bodyHash, extraStoryIds, generationOverrides);
			WriteManifest(manifest);
			if (faults != null)
				faults.Hit(FaultPoint.AfterManifestBeforePointer);

			WritePointer(manifest.Id);
			if (faults != null)
				faults.Hit(FaultPoint.AfterDirectoryFsync);

			ReclaimErasedContent();
			if (faults != null)
				faults.Hit(FaultPoint.AfterPublishBeforeAck);

			return new AppendResult(AppendStatus.Committed, manifest.Id, "Published.", frames);
		}

		private ParsedManifest DescribeUnlocked(
			string parentId,
			string key,
			string bodyHash,
			IReadOnlyList<string> extraStoryIds,
			Dictionary<string, int>? generationOverrides
		)
		{
			ParsedManifest previous = ReadManifest(parentId);
			HashSet<string> storyIds = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < previous.Stories.Count; i++)
				storyIds.Add(previous.Stories[i].StoryId);

			for (int i = 0; i < extraStoryIds.Count; i++)
				storyIds.Add(extraStoryIds[i]);

			if (generationOverrides != null)
			{
				foreach (KeyValuePair<string, int> pair in generationOverrides)
					storyIds.Add(pair.Key);
			}

			List<StoryPrefix> prefixes = new List<StoryPrefix>();
			List<StoredRecord> all = new List<StoredRecord>();
			foreach (string storyId in storyIds.OrderBy(item => item, StringComparer.Ordinal))
			{
				int generation = GenerationFor(storyId, previous, generationOverrides);
				StoryPrefix prefix = DescribeStory(storyId, generation);
				prefixes.Add(prefix);
				if (prefix.Encoding != "shipped-v1")
					all.AddRange(ReadCommittedFrom(storyId, generation, prefix.RecordCount));
			}

			for (int i = 0; i < all.Count; i++)
			{
				string? declared = all[i].DeclaresStoryId;
				if (string.IsNullOrWhiteSpace(declared))
					continue;

				if (storyIds.Contains(declared))
					continue;

				storyIds.Add(declared);
				int generation = GenerationFor(declared, previous, generationOverrides);
				StoryPrefix prefix = DescribeStory(declared, generation);
				prefixes.Add(prefix);
			}

			List<ContentEntry> content = DescribeContent(all, out _);
			return ManifestText.Build(parentId, key, bodyHash, prefixes, content);
		}

		private static List<string> StoryIds(AppendRequest request)
		{
			List<string> ids = new List<string>();
			for (int i = 0; i < request.Stories.Count; i++)
				ids.Add(request.Stories[i].StoryId);

			return ids;
		}
		#endregion

		#region private functions
		private static BatchFormat ReadFormat(string path)
		{
			string text = File.ReadAllText(LibraryLayout.Description(path)).Trim();
			if (!text.StartsWith("format=", StringComparison.Ordinal))
				throw new InvalidDataException("library.txt does not name a format.");

			return ParseFormat(text.Substring("format=".Length));
		}

		private static void RequireToken(string value, string name)
		{
			if (string.IsNullOrWhiteSpace(value))
				throw new ArgumentException("A value is required.", name);

			if (value.IndexOf('\t') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
				throw new ArgumentException("The value cannot contain tabs or newlines.", name);
		}
		#endregion
	}
}
