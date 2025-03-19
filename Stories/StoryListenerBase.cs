using BigRedProf.Data.Core;
using BigRedProf.Stories.Events;
using BigRedProf.Stories.Models;
using System;
using System.Threading.Tasks;

namespace BigRedProf.Stories
{
	abstract public class StoryListenerBase : IStoryListener
	{
		#region IStoryListener events
		public event AsyncEventHandler<SomethingHappenedEventArgs>? SomethingHappenedAsync;
		public event AsyncEventHandler<ConnectionStatusEventArgs>? ConnectionStatusChangedAsync;
		#endregion

		#region constructors
		protected StoryListenerBase(StoryId storyId)
		{
			StoryId = storyId;
		}
		#endregion

		#region properties
		public StoryId StoryId
		{
			get;
			private set;
		}
		#endregion

		#region IStoryListener properties
		public long Bookmark
		{
			get;
			protected set;
		}
		#endregion

		#region IStoryListener methods
		abstract public void StartListening();
		abstract public Task StartListeningAsync();
		abstract public void StopListening();
		abstract public Task StopListeningAsync();
		#endregion

		#region protected methods
		protected async Task InvokeSomethingHappenedEventAsync(StoryThing thing)
		{
			if (SomethingHappenedAsync != null)
				await SomethingHappenedAsync(this, new SomethingHappenedEventArgs(thing));
		}

		protected async Task InvokeConnectionStatusChangedAsync(string status, string? message, Exception? exception)
		{
			if (ConnectionStatusChangedAsync != null)
				await ConnectionStatusChangedAsync(this, new ConnectionStatusEventArgs(status, message, exception));
		}
		#endregion
	}
}