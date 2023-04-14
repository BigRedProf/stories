using BigRedProf.Data;

namespace BigRedProf.Stories.Memory
{
	public class MemoryStoryManager
	{
		#region fields
		private IDictionary<StoryId, IList<Code>> _dictionary;
		#endregion

		#region constructors
		public MemoryStoryManager()
		{
			_dictionary = new Dictionary<StoryId, IList<Code>>();
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
		#endregion

		#region private methods
		private IList<Code> GetOrCreateListOfThings(StoryId id)
		{
			IList<Code>? things = null;
			if(!_dictionary.TryGetValue(id, out things))
			{
				things = new List<Code>();
				_dictionary.Add(id, things);
			}

			return things;
		}
		#endregion
	}
}
