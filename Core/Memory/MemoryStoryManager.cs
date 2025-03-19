using BigRedProf.Stories.Models;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BigRedProf.Stories.Memory
{
	public class MemoryStoryManager
	{
		#region fields
		private IDictionary<StoryId, ObservableCollection<StoryThing>> _storyThingsDictionary;
		private IDictionary<StoryId, MemoryScribe> _scribeDictionary;
		#endregion

		#region constructors
		public MemoryStoryManager()
		{
			_storyThingsDictionary = new ConcurrentDictionary<StoryId, ObservableCollection<StoryThing>>();
			_scribeDictionary = new ConcurrentDictionary<StoryId, MemoryScribe>();
		}
		#endregion

		#region methods
		public MemoryScribe GetScribe(StoryId id)
		{
			return GetOrCreateScribe(id);
		}

		public MemoryStoryteller GetStoryteller(StoryId id)
		{
			return new MemoryStoryteller(GetOrCreateListOfThings(id));
		}

		public MemoryStoryListener GetStoryListener(StoryId id)
		{
			return new MemoryStoryListener(id, GetOrCreateListOfThings(id));
		}
		#endregion

		#region private methods
		private ObservableCollection<StoryThing> GetOrCreateListOfThings(StoryId id)
		{
			ObservableCollection<StoryThing>? things = null;
			if(!_storyThingsDictionary.TryGetValue(id, out things))
			{
				things = new ObservableCollection<StoryThing>();
				_storyThingsDictionary.Add(id, things);
			}

			return things;
		}

		private MemoryScribe GetOrCreateScribe(StoryId id)
		{
			MemoryScribe? scribe = null;
			if(!_scribeDictionary.TryGetValue(id, out scribe))
			{
				scribe = new MemoryScribe(GetOrCreateListOfThings(id));
				_scribeDictionary.Add(id, scribe);
			}

			return scribe;
		}
		#endregion
	}
}
