using BigRedProf.Data.Core;
using System;
using System.Collections.Generic;
using System.IO;

namespace BigRedProf.Stories.Disk
{
	/// <summary>
	/// File-backed counterpart of <see cref="BigRedProf.Stories.Memory.MemoryStoryManager"/>. Stories
	/// live as files under a caller-supplied directory, so a later process can open
	/// the same directory and replay them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The directory is the root, not a single story. A catalog kept at
	/// <c>&lt;store&gt;/catalog/</c> is one story inside that directory, addressed
	/// by its <see cref="TextTrail"/>. One writer is allowed per story;
	/// <see cref="FileScribe"/> holds that writer until it is disposed.
	/// </para>
	/// <para>
	/// Each story is <c>{storyIdHash}.story</c>. The hash is
	/// <see cref="TextTrailSerializer.ToMultihashString"/>, which is filename-safe
	/// on Windows and Linux. <c>{storyIdHash}.story.lock</c> is the writer lock.
	/// Version 1 of the story file is a 16-byte header (<c>BRPSTORY</c>, then
	/// little-endian version 1, then a reserved 0) followed by frames. A frame is
	/// a little-endian payload length, the payload, and a little-endian IEEE
	/// CRC-32 of the length and the payload. The payload is the
	/// <see cref="BigRedProf.Stories.Data.StoryThing"/> packed through the pied
	/// piper, including the offset assigned at append time.
	/// </para>
	/// <para>
	/// A reader returns every intact frame and stops at a partial trailing frame,
	/// so a crash mid-append does not hide or corrupt earlier records. The next
	/// writer trims that torn tail before appending.
	/// </para>
	/// </remarks>
	public class FileStoryManager : IDisposable
	{
		#region fields
		private readonly string _directoryPath;
		private readonly IPiedPiper _piedPiper;
		private readonly IDictionary<TextTrail, FileScribe> _scribes;
		private readonly object _gate;
		private bool _disposed;
		#endregion

		#region constructors
		public FileStoryManager(string directoryPath, IPiedPiper piedPiper)
		{
			if (piedPiper == null)
				throw new ArgumentNullException(nameof(piedPiper));

			_directoryPath = FileStoryPaths.GetFullDirectoryPath(directoryPath);
			_piedPiper = piedPiper;
			_scribes = new Dictionary<TextTrail, FileScribe>(TextTrailSerializer.CreateEqualityComparer());
			_gate = new object();

			Directory.CreateDirectory(_directoryPath);
		}
		#endregion

		#region methods
		public FileScribe GetScribe(TextTrail storyId)
		{
			if (storyId == null)
				throw new ArgumentNullException(nameof(storyId));

			lock (_gate)
			{
				if (_disposed)
					throw new ObjectDisposedException(nameof(FileStoryManager));

				FileScribe? scribe;
				if (!_scribes.TryGetValue(storyId, out scribe) || scribe.IsDisposed)
				{
					scribe = new FileScribe(_directoryPath, storyId, _piedPiper);
					_scribes[storyId] = scribe;
				}

				return scribe;
			}
		}

		public FileStoryteller GetStoryteller(TextTrail storyId)
		{
			if (storyId == null)
				throw new ArgumentNullException(nameof(storyId));

			lock (_gate)
			{
				if (_disposed)
					throw new ObjectDisposedException(nameof(FileStoryManager));
			}

			return new FileStoryteller(_directoryPath, storyId, _piedPiper);
		}
		#endregion

		#region IDisposable methods
		public void Dispose()
		{
			lock (_gate)
			{
				if (_disposed)
					return;

				_disposed = true;
				foreach (FileScribe scribe in _scribes.Values)
					scribe.Dispose();

				_scribes.Clear();
			}
		}
		#endregion
	}
}
