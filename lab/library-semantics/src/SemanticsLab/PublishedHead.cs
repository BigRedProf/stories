using System.Globalization;

namespace BigRedProf.Stories.LibrarySemanticsLab
{
	/// <summary>
	/// One record a published-head listener actually yielded. A flushed frame
	/// past the published length never appears here.
	/// </summary>
	public sealed class HeardRecord
	{
		#region properties
		public string StoryId
		{
			get;
			set;
		} = "";

		public long Offset
		{
			get;
			set;
		}

		public string Text
		{
			get;
			set;
		} = "";

		public double SeenAtMilliseconds
		{
			get;
			set;
		}
		#endregion
	}

	/// <summary>
	/// The lab stand-in for <c>FileStoryteller</c> and a live listener. It reads
	/// the story file, and it stops at the length named by the current edition.
	/// </summary>
	public static class PublishedHead
	{
		#region functions
		public static IReadOnlyList<StoredRecord> ReadStory(string root, string storyId)
		{
			if (string.IsNullOrWhiteSpace(root))
				throw new ArgumentException("A library path is required.", nameof(root));

			if (string.IsNullOrWhiteSpace(storyId))
				throw new ArgumentException("A story id is required.", nameof(storyId));

			string pointerPath = LibraryLayout.Pointer(root);
			if (!File.Exists(pointerPath))
				return Array.Empty<StoredRecord>();

			string editionId = File.ReadAllText(pointerPath).Trim();
			string manifestPath = LibraryLayout.Manifest(root, editionId);
			if (!File.Exists(manifestPath))
				return Array.Empty<StoredRecord>();

			ParsedManifest manifest = ManifestText.Parse(File.ReadAllText(manifestPath));
			StoryPrefix? prefix = null;
			for (int i = 0; i < manifest.Stories.Count; i++)
			{
				if (manifest.Stories[i].StoryId == storyId)
				{
					prefix = manifest.Stories[i];
					break;
				}
			}

			if (prefix == null || prefix.RecordCount == 0)
				return Array.Empty<StoredRecord>();

			if (prefix.Encoding == "shipped-v1")
				throw new InvalidDataException("'" + storyId + "' is a shipped story file. This head reader is the lab frame.");

			string path = LibraryLayout.Generation(root, storyId, prefix.Generation);
			LogImage image = StoryLog.ReadThrough(path, prefix.FileBytes);
			if (image.TornTail || image.ValidLength != prefix.FileBytes)
				throw new InvalidDataException("The published prefix of '" + storyId + "' is not a clean frame sequence.");

			if (image.Committed.Count != prefix.RecordCount)
				throw new InvalidDataException("The published prefix of '" + storyId + "' does not have the published record count.");

			for (int i = 0; i < image.Committed.Count; i++)
				image.Committed[i].StoryId = storyId;

			return image.Committed;
		}
		#endregion
	}

	/// <summary>
	/// Polls <see cref="PublishedHead"/> and records only records that are inside
	/// the published length. Replacing an unpublished tail does not show up here.
	/// </summary>
	public sealed class PublishedListener : IDisposable
	{
		#region constructors
		public PublishedListener(string root, int pollMilliseconds)
		{
			if (string.IsNullOrWhiteSpace(root))
				throw new ArgumentException("A library path is required.", nameof(root));

			if (pollMilliseconds < 1)
				throw new ArgumentOutOfRangeException(nameof(pollMilliseconds));

			_root = root;
			_pollMilliseconds = pollMilliseconds;
			_gate = new object();
			_heard = new List<HeardRecord>();
			_seen = new HashSet<string>(StringComparer.Ordinal);
			_watch = System.Diagnostics.Stopwatch.StartNew();
			_thread = new Thread(Loop);
			_thread.IsBackground = true;
			_thread.Name = "published-head";
			_thread.Start();
		}
		#endregion

		#region fields
		private readonly string _root;
		private readonly int _pollMilliseconds;
		private readonly System.Diagnostics.Stopwatch _watch;
		private readonly Thread _thread;
		private readonly object _gate;
		private readonly List<HeardRecord> _heard;
		private readonly HashSet<string> _seen;
		private bool _stop;
		#endregion

		#region methods
		public IReadOnlyList<HeardRecord> Snapshot()
		{
			lock (_gate)
			{
				HeardRecord[] copy = new HeardRecord[_heard.Count];
				_heard.CopyTo(copy);
				return copy;
			}
		}

		public bool WaitForText(string text, int timeoutMilliseconds)
		{
			if (text == null)
				throw new ArgumentNullException(nameof(text));

			DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
			while (DateTime.UtcNow < deadline)
			{
				if (Contains(text))
					return true;

				Thread.Sleep(_pollMilliseconds);
			}

			return Contains(text);
		}

		public void Dispose()
		{
			_stop = true;
			_thread.Join(TimeSpan.FromSeconds(2));
		}
		#endregion

		#region private methods
		private bool Contains(string text)
		{
			lock (_gate)
			{
				for (int i = 0; i < _heard.Count; i++)
				{
					if (_heard[i].Text == text)
						return true;
				}

				return false;
			}
		}

		private void Loop()
		{
			while (!_stop)
			{
				try
				{
					Poll();
				}
				catch (IOException)
				{
				}
				catch (InvalidDataException)
				{
				}
				catch (ArgumentException)
				{
				}

				Thread.Sleep(_pollMilliseconds);
			}
		}

		private void Poll()
		{
			string pointerPath = LibraryLayout.Pointer(_root);
			if (!File.Exists(pointerPath))
				return;

			string editionId = File.ReadAllText(pointerPath).Trim();
			string manifestPath = LibraryLayout.Manifest(_root, editionId);
			if (!File.Exists(manifestPath))
				return;

			ParsedManifest manifest = ManifestText.Parse(File.ReadAllText(manifestPath));
			for (int i = 0; i < manifest.Stories.Count; i++)
			{
				StoryPrefix story = manifest.Stories[i];
				if (story.Encoding == "shipped-v1" || story.RecordCount == 0)
					continue;

				IReadOnlyList<StoredRecord> records = PublishedHead.ReadStory(_root, story.StoryId);
				for (int r = 0; r < records.Count; r++)
				{
					StoredRecord record = records[r];
					string text = LabCode.ToText(record.Body);
					string identity = story.StoryId + "\t" + record.Offset.ToString(CultureInfo.InvariantCulture) + "\t" + text;
					lock (_gate)
					{
						if (!_seen.Add(identity))
							continue;

						HeardRecord heard = new HeardRecord();
						heard.StoryId = story.StoryId;
						heard.Offset = record.Offset;
						heard.Text = text;
						heard.SeenAtMilliseconds = _watch.Elapsed.TotalMilliseconds;
						_heard.Add(heard);
					}
				}
			}
		}
		#endregion
	}
}
