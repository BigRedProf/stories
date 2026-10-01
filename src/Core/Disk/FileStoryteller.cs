using BigRedProf.Data.Core;
using BigRedProf.Stories.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace BigRedProf.Stories.Disk
{
	/// <summary>
	/// An <see cref="IStoryteller"/> that replays a story file written by
	/// <see cref="FileScribe"/>. Each instance keeps its own bookmark. Records
	/// appended after this storyteller is created are visible on the next read,
	/// including from a new instance in a later process.
	/// </summary>
	public class FileStoryteller : IStoryteller
	{
		#region events
#pragma warning disable CS0067 // The event 'FileStoryteller.GotSomethingForYou' is never used
		public event EventHandler? GotSomethingForYou;
#pragma warning restore CS0067 // The event 'FileStoryteller.GotSomethingForYou' is never used
		#endregion

		#region fields
		private readonly string _filePath;
		private readonly IPiedPiper _piedPiper;
		private readonly object _readLock;
		private long _bookmark;
		private List<StoryThing>? _cachedThings;
		private long _cachedLength;
		private uint _cachedTailChecksum;
		#endregion

		#region constructors
		public FileStoryteller(string directoryPath, TextTrail storyId, IPiedPiper piedPiper)
		{
			if (string.IsNullOrWhiteSpace(directoryPath))
				throw new ArgumentException("Value cannot be null or whitespace.", nameof(directoryPath));

			if (storyId == null)
				throw new ArgumentNullException(nameof(storyId));

			if (piedPiper == null)
				throw new ArgumentNullException(nameof(piedPiper));

			string fullDirectory = FileStoryPaths.GetFullDirectoryPath(directoryPath);
			_filePath = FileStoryPaths.GetStoryFilePath(fullDirectory, storyId);
			_piedPiper = piedPiper;
			_readLock = new object();
			_bookmark = 0;
			_cachedLength = -1;
			_cachedTailChecksum = 0;
		}
		#endregion

		#region properties
		public long Bookmark
		{
			get
			{
				return _bookmark;
			}
		}
		#endregion

		#region IStoryteller properties
		public bool HasSomethingForMe
		{
			get
			{
				Task<bool> result = HasSomethingForMeAsync();
				return result.Result;
			}
		}
		#endregion

		#region IStoryteller methods
		public Task<bool> HasSomethingForMeAsync()
		{
			bool result = _bookmark >= 0 && _bookmark < GetThings().Count;
			return Task.FromResult(result);
		}

		public StoryThing TellMeSomething()
		{
			Task<StoryThing> task = TellMeSomethingAsync();
			return task.Result;
		}

		public Task<StoryThing> TellMeSomethingAsync()
		{
			if (_bookmark > int.MaxValue)
				throw new InvalidOperationException("FileStoryteller does not support bookmarks > 2^31.");

			IList<StoryThing> things = GetThings();
			if (_bookmark < 0 || _bookmark >= things.Count)
				throw new InvalidOperationException("There is nothing to tell at the current bookmark.");

			StoryThing thing = things[(int)_bookmark];
			_bookmark++;
			return Task.FromResult(thing);
		}

		public void SetBookmark(long bookmark)
		{
			if (bookmark < 0)
				throw new ArgumentOutOfRangeException(nameof(bookmark), "Bookmark must be zero or greater.");

			if (bookmark > int.MaxValue)
				throw new InvalidOperationException("FileStoryteller does not support bookmarks > 2^31.");

			_bookmark = bookmark;
		}
		#endregion

		#region private methods
		private IList<StoryThing> GetThings()
		{
			lock (_readLock)
			{
				// Length alone misses a writer that trimmed a torn tail and then
				// appended a frame that landed on the same length. The tail bytes
				// change in that case, so the checksum has to be part of the key.
				long length;
				uint tailChecksum;
				ReadStamp(out length, out tailChecksum);

				if (_cachedThings != null && length == _cachedLength && tailChecksum == _cachedTailChecksum)
					return _cachedThings;

				_cachedThings = FileStoryLog.Read(_filePath, _piedPiper);
				_cachedLength = length;
				_cachedTailChecksum = tailChecksum;
				return _cachedThings;
			}
		}

		private void ReadStamp(out long length, out uint tailChecksum)
		{
			length = 0;
			tailChecksum = 0;
			if (!System.IO.File.Exists(_filePath))
				return;

			using (FileStream stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				length = stream.Length;
				int take = (int)Math.Min(length, 64L);
				if (take == 0)
					return;

				byte[] tail = new byte[take];
				stream.Position = length - take;
				int offset = 0;
				while (offset < take)
				{
					int read = stream.Read(tail, offset, take - offset);
					if (read == 0)
						break;

					offset += read;
				}

				tailChecksum = Crc32.Compute(tail, 0, offset);
			}
		}
		#endregion
	}
}
