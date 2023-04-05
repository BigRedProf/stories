using BigRedProf.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BigRedProf.Stories.Memory
{
	public class MemoryStoryManager
	{
		#region fields
		private IDictionary<StoryId, Tuple<MemoryScribe, MemoryStoryteller>> _dictionary;
		#endregion

		#region constructors
		public MemoryStoryManager()
		{
			_dictionary = new Dictionary<StoryId, Tuple<MemoryScribe, MemoryStoryteller>>();
		}
		#endregion

		#region methods
		public MemoryScribe GetScribe(StoryId id)
		{
			return GetOrCreateStoryActors(id).Item1;
		}

		public MemoryStoryteller GetStoryteller(StoryId id)
		{
			return GetOrCreateStoryActors(id).Item2;
		}
		#endregion

		#region private methods
		private Tuple<MemoryScribe, MemoryStoryteller> GetOrCreateStoryActors(StoryId id)
		{
			Tuple<MemoryScribe, MemoryStoryteller>? actors = null ;
			if(!_dictionary.TryGetValue(id, out actors))
			{
				IList<Code> things = new List<Code>();
				actors = new Tuple<MemoryScribe, MemoryStoryteller>
				(
					new MemoryScribe(things),
					new MemoryStoryteller(things)
				);				
				_dictionary.Add(id, actors);
			}

			return actors;
		}
		#endregion
	}
}
