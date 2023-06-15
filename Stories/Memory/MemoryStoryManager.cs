using BigRedProf.Data;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace BigRedProf.Stories.Memory
{
	public class MemoryStoryManager
	{
		#region fields
		private IDictionary<StoryId, ObservableCollection<Code>> _dictionary;
		#endregion

		#region constructors
		public MemoryStoryManager()
		{
			_dictionary = new ConcurrentDictionary<StoryId, ObservableCollection<Code>>();
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
		private ObservableCollection<Code> GetOrCreateListOfThings(StoryId id)
		{
			ObservableCollection<Code>? things = null;
			if(!_dictionary.TryGetValue(id, out things))
			{
				things = new ObservableCollection<Code>();
				_dictionary.Add(id, things);
			}

			return things;
		}
		#endregion
	}
}
