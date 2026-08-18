using BigRedProf.Data.Core;
using BigRedProf.Stories.Events;
using BigRedProf.Stories.Internal.ApiClient;
using BigRedProf.Stories.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;

namespace BigRedProf.Stories.PowerShell
{
	/// <summary>
	/// Tails a story, emitting each thing as it lands, until you stop it.
	/// </summary>
	/// <remarks>
	/// The counterpart to <see cref="GetStoryCommand"/>. Use this one to watch a room
	/// while you poke at it; use Get-Story to ask a question and get an answer.
	/// </remarks>
	/// <example>
	///   <code>Watch-Story bigredprof/digihouse/rooms/profhouse/family-room -BaseUri http://localhost:43027</code>
	/// </example>
	[Cmdlet(VerbsCommon.Watch, "Story")]
	[OutputType(typeof(StoryThingInfo))]
	public sealed class WatchStoryCommand : StoryCmdletBase
	{
		#region constants
		private const long BatchSize = 1000;
		private static readonly TimeSpan PollingFrequency = TimeSpan.FromSeconds(5);

		// How long the drain loop waits before checking Stopping again. Short enough
		// that Ctrl+C feels immediate, long enough not to spin.
		private static readonly TimeSpan StopCheckInterval = TimeSpan.FromMilliseconds(250);
		#endregion

		#region fields
		private readonly BlockingCollection<StoryThing> _things = new BlockingCollection<StoryThing>();
		#endregion

		#region PSCmdlet methods
		protected override void ProcessRecord()
		{
			IPiedPiper piedPiper = BuildPiedPiper();
			TextTrail storyId = ParseStoryId();

			ApiClient apiClient = new ApiClient(
				BaseUri, piedPiper, NullLogger<ApiClient>.Instance, null);
			IStoryListener listener = apiClient.GetStoryListener(
				BatchSize, PollingFrequency, storyId, Bookmark);

			listener.SomethingHappenedAsync += OnSomethingHappenedAsync;
			try
			{
				listener.StartListening();

				// The listener raises its event on ITS OWN thread, and WriteObject may
				// only be called from the pipeline thread. So the handler queues, and
				// this loop -- which IS the pipeline thread -- drains and writes.
				while (!Stopping)
				{
					if (_things.TryTake(out StoryThing? thing, (int)StopCheckInterval.TotalMilliseconds)
						&& thing != null)
					{
						WriteObject(ToStoryThingInfo(piedPiper, thing));
					}
				}
			}
			finally
			{
				listener.SomethingHappenedAsync -= OnSomethingHappenedAsync;
				listener.StopListening();
			}
		}

		protected override void StopProcessing()
		{
			// Ctrl+C. The drain loop notices via Stopping within StopCheckInterval;
			// completing the collection wakes it immediately rather than making the
			// user wait out the timeout.
			_things.CompleteAdding();
		}
		#endregion

		#region event handlers
		private Task OnSomethingHappenedAsync(object? sender, SomethingHappenedEventArgs e)
		{
			if (!_things.IsAddingCompleted)
				_things.Add(e.Thing);

			return Task.CompletedTask;
		}
		#endregion
	}
}
