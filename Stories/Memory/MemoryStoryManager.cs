using BigRedProf.Data;
using BigRedProf.Stories.Models;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BigRedProf.Stories.Memory
{
	public class MemoryStoryManager
	{
		#region fields
		private IDictionary<StoryId, ObservableCollection<StoryThing>> _dictionary;
		#endregion

		#region constructors
		public MemoryStoryManager()
		{
			_dictionary = new ConcurrentDictionary<StoryId, ObservableCollection<StoryThing>>();
		}
		#endregion

		#region methods
		public MemoryScribe GetScribe(StoryId id)
		{
			return new MemoryScribe(GetOrCreateListOfThings(id));
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
			if(!_dictionary.TryGetValue(id, out things))
			{
				things = new ObservableCollection<StoryThing>();
				_dictionary.Add(id, things);
			}

			return things;
		}
		#endregion
	}
}
