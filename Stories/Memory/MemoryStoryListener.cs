using BigRedProf.Data;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;

namespace BigRedProf.Stories.Memory
{
    public class MemoryStoryListener : StoryListenerBase
	{
		#region fields
		private readonly ObservableCollection<Code> _things;
		#endregion

		#region constructors
		public MemoryStoryListener(StoryId storyId, ObservableCollection<Code> things)
			: base(storyId)
		{
			_things = things;
			Bookmark = things.LongCount();
		}
		#endregion

		#region StoryListener methods
		public override void StartListening()
		{
			_things.CollectionChanged += Things_CollectionChanged;
		}

		public override Task StartListeningAsync()
		{
			StartListening();
			return Task.CompletedTask;
		}

		public override void StopListening()
		{
			_things.CollectionChanged -= Things_CollectionChanged;
		}

		public override Task StopListeningAsync()
		{
			StopListening();
			return Task.CompletedTask;
		}
		#endregion

		#region event handlers
		private async void Things_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if (e.Action != NotifyCollectionChangedAction.Add)
				return;

			if (Bookmark > int.MaxValue)
				throw new InvalidOperationException("MemoryStoryListener does not support bookmarks > 2^31");

			while (Bookmark < _things.Count)
			{
				await InvokeSomethingHappenedEventAsync(Bookmark, _things[(int) Bookmark]);
				++Bookmark;
			}
		}
		#endregion
	}
}
