using BigRedProf.Stories.Events;
using BigRedProf.Stories.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BigRedProf.Stories.Internal.ApiClient
{
	internal class StoryThingSequencer : IAsyncDisposable
	{
		#region events
		public event AsyncEventHandler<SomethingHappenedEventArgs>? SomethingHappenedAsync;
		#endregion

		#region fields
		private readonly ILogger<Stories.ApiClient> _logger;
		private readonly IStoryteller _catchUpStoryteller;
		private readonly TimeSpan _pollingFrequency;
		private long _bookmark;
		private DateTime _lastContactTime;
		private readonly ConcurrentQueue<StoryThing> _storyThingQueue;
		private readonly CancellationTokenSource _cancellationTokenSource;
		private readonly Task _workerTask; 
		private bool _isDisposed;
		#endregion

		#region constructors
		public StoryThingSequencer(ILogger<Stories.ApiClient> logger, IStoryteller catchUpStoryTeller, long bookmark, TimeSpan pollingFrequency)
		{
			_logger = logger;
			_catchUpStoryteller = catchUpStoryTeller;
			_bookmark = bookmark;
			_pollingFrequency = pollingFrequency;

			_lastContactTime = DateTime.MinValue;
			_storyThingQueue = new ConcurrentQueue<StoryThing>();
			_cancellationTokenSource = new CancellationTokenSource();
			_workerTask = Task.Run(() => WorkerLoopAsync(_cancellationTokenSource.Token));
			_isDisposed = false;
		}
		#endregion

		#region methods
		public void SequenceStoryThing(StoryThing storyThing)
		{
			_logger.LogDebug("Enter StoryThingSequencer.SequenceStoryThing. Offset={Offset}", storyThing.Offset);

			if (_isDisposed)
			{
				_logger.LogWarning("StoryThingSequencer.SequenceStoryThing called when already disposed.");
				throw new ObjectDisposedException(nameof(ApiStoryListener));
			}

			_storyThingQueue.Enqueue(storyThing);
		}
		#endregion

		#region IAsyncDisposable methods
		public ValueTask DisposeAsync()
		{
			_cancellationTokenSource.Cancel();
			_workerTask.Wait();
			_isDisposed = true;
			return new ValueTask();
		}
		#endregion

		#region protected methods
		protected async Task InvokeSomethingHappenedEventAsync(StoryThing thing)
		{
			SetLastContactTime();

			if (SomethingHappenedAsync != null)
				await SomethingHappenedAsync(this, new SomethingHappenedEventArgs(thing));
		}
		#endregion

		#region private methods
		private async Task WorkerLoopAsync(CancellationToken cancellationToken)
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				if (_storyThingQueue.TryDequeue(out StoryThing storyThing))
				{
					try
					{
						await ProcessStoryThingAsync(storyThing);
					}
					catch (Exception ex)
					{
						_logger.LogError(
							ex, 
							"Failed to process story thing. Offset={Offset}. Message={Message}.", 
							storyThing.Offset, 
							ex.Message
						);
					}
				}
				else if(IsTimeToPoll())
				{
					try
					{
						await PollAsync();
					}
					catch(Exception ex)
					{
						_logger.LogError(
							ex,
							"Failed to poll. Message={Message}",
							ex.Message
						);
					}
				}
				else
				{
					await Task.Delay(TimeSpan.FromMilliseconds(10));
				}
			}
		}

		private async Task ProcessStoryThingAsync(StoryThing storyThing)
		{
			_logger.LogDebug(
				"Enter StoryThingSequencer.ProcessStoryThingAsync. Offset={Offset}", 
				storyThing.Offset
			);

			long offset = storyThing.Offset;

			if (_bookmark > offset)
			{
				// the catch-up storyteller is ahead of us and must have already told us about this thing
				_logger.LogDebug(
					"The catch-up storyteller is ahead of us and must have already told us about " + 
					"this thing. Bookmark={Bookmark},Offset={Offset}",
					_bookmark,
					offset
				);
				return;
			}

			while (_bookmark < offset)
			{
				// we've fallen behind in the story and need to catch up with a Storyteller
				_logger.LogDebug(
					"We've fallen behind in the story and need to catch up with a Storyteller. " +
					"Bookmark={Bookmark},Offset={Offset}",
					_bookmark,
					offset
				);
				_catchUpStoryteller.SetBookmark(_bookmark);
				StoryThing catchUpThing = await _catchUpStoryteller.TellMeSomethingAsync();
				_logger.LogDebug("Catch-up thing retrieved. Offset={Offset}", catchUpThing.Offset);
				_logger.LogTrace("Catch-up thing retrieved. Thing={Thing}", catchUpThing.Thing.ToString());
				await InvokeSomethingHappenedEventAsync(catchUpThing);
				++_bookmark;
			}

			_logger.LogDebug("Thing retrieved. Offset={Offset}", storyThing.Offset);
			_logger.LogTrace("Thing retrieved. Thing={Thing}", storyThing.Thing.ToString());
			await InvokeSomethingHappenedEventAsync(storyThing);

			++_bookmark;
		}

		private async Task PollAsync()
		{
			_logger.LogDebug("Enter StoryThingSequencer.PollAsync.");

			_catchUpStoryteller.SetBookmark(_bookmark);
			while (await _catchUpStoryteller.HasSomethingForMeAsync())
			{
				_logger.LogDebug("Requesting thing. Bookmark={Bookmark}.", _bookmark);
				StoryThing catchUpThing = await _catchUpStoryteller.TellMeSomethingAsync();
				_logger.LogDebug("Catch-up thing retrieved. Offset={Offset}", catchUpThing.Offset);
				_logger.LogTrace("Catch-up thing retrieved. Thing={Thing}", catchUpThing.Thing.ToString());
				await InvokeSomethingHappenedEventAsync(catchUpThing);

				++_bookmark;
			}

			SetLastContactTime();
		}

		private void SetLastContactTime()
		{
			_lastContactTime = DateTime.UtcNow;
		}

		private bool IsTimeToPoll()
		{
			DateTime now = DateTime.UtcNow;
			return (now - _lastContactTime > _pollingFrequency);
		}
		#endregion
	}
}
