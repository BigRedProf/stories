using BigRedProf.Data.Core;
using System;
using System.Threading.Tasks;

namespace BigRedProf.Stories.Disk
{
	/// <summary>
	/// An <see cref="IScribe"/> that appends to a story file under a caller-supplied
	/// directory. The directory is the story root — for example <c>&lt;store&gt;/catalog/</c> —
	/// and this scribe is bound to one story inside it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Each <see cref="RecordSomething"/> record is written and flushed to disk before
	/// the method returns, so a process restart can replay it. A crash mid-append
	/// leaves at most a trailing partial frame; earlier records stay readable.
	/// </para>
	/// <para>
	/// One writer is allowed at a time. A second <see cref="FileScribe"/> for the same
	/// story throws <see cref="System.IO.IOException"/> until this one is disposed.
	/// The pied piper must already have the core pack rats and the
	/// <see cref="BigRedProf.Stories.Data.StoryThing"/> pack rat registered.
	/// </para>
	/// </remarks>
	public class FileScribe : IScribe, IDisposable
	{
		#region fields
		private readonly FileStoryLog _log;
		private bool _disposed;
		#endregion

		#region constructors
		public FileScribe(string directoryPath, TextTrail storyId, IPiedPiper piedPiper)
		{
			if (storyId == null)
				throw new ArgumentNullException(nameof(storyId));

			if (piedPiper == null)
				throw new ArgumentNullException(nameof(piedPiper));

			_log = new FileStoryLog(directoryPath, storyId, piedPiper);
		}
		#endregion

		#region internal properties
		internal bool IsDisposed
		{
			get
			{
				return _disposed;
			}
		}
		#endregion

		#region IScribe methods
		public void RecordSomething(params Code[] things)
		{
			Task task = RecordSomethingAsync(things);
			task.Wait();
		}

		public Task RecordSomethingAsync(params Code[] things)
		{
			if (_disposed)
				throw new ObjectDisposedException(nameof(FileScribe));

			_log.Append(things);
			return Task.CompletedTask;
		}
		#endregion

		#region IDisposable methods
		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;
			_log.Dispose();
		}
		#endregion
	}
}
