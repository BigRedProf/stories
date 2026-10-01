using BigRedProf.Data.Core;
using BigRedProf.Stories.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BigRedProf.Stories.Memory
{
	public class MemoryStoryManager
	{
		#region fields
		private IDictionary<TextTrail, ObservableCollection<StoryThing>> _storyThingsDictionary;
		private IDictionary<TextTrail, MemoryScribe> _scribeDictionary;
		#endregion

		#region constructors
		public MemoryStoryManager()
		{
			_storyThingsDictionary = new ConcurrentDictionary<TextTrail, ObservableCollection<StoryThing>>(TextTrailSerializer.CreateEqualityComparer());
			_scribeDictionary = new ConcurrentDictionary<TextTrail, MemoryScribe>(TextTrailSerializer.CreateEqualityComparer());
		}
		#endregion

		#region methods
		public MemoryScribe GetScribe(TextTrail storyId)
		{
			return GetOrCreateScribe(storyId);
		}

		public MemoryStoryteller GetStoryteller(TextTrail storyId)
		{
			return new MemoryStoryteller(GetOrCreateListOfThings(storyId));
		}

		public MemoryStoryListener GetStoryListener(TextTrail storyId)
		{
			return new MemoryStoryListener(storyId, GetOrCreateListOfThings(storyId));
		}

		/// <summary>
		/// Every story holding at least one thing, with how many it holds.
		/// </summary>
		/// <remarks>
		/// A story that has only ever been read is left out: asking about a story makes an empty
		/// one, and an empty story has nothing to keep. Keys are returned exactly as they were
		/// given -- for the Api, that is the internal trail wrapping a story ID hash.
		/// </remarks>
		public IReadOnlyList<KeyValuePair<TextTrail, long>> GetStoryLengths()
		{
			List<KeyValuePair<TextTrail, long>> lengths = new List<KeyValuePair<TextTrail, long>>();
			foreach (KeyValuePair<TextTrail, ObservableCollection<StoryThing>> story in _storyThingsDictionary.ToArray())
			{
				int length = story.Value.Count;
				if (length > 0)
					lengths.Add(new KeyValuePair<TextTrail, long>(story.Key, length));
			}

			return lengths;
		}
		#endregion

		#region private methods
		private ObservableCollection<StoryThing> GetOrCreateListOfThings(TextTrail storyId)
		{
			if (storyId == null)
				throw new ArgumentNullException(nameof(storyId));

			ObservableCollection<StoryThing>? things = null;
			if(!_storyThingsDictionary.TryGetValue(storyId, out things))
			{
				things = new ObservableCollection<StoryThing>();
				_storyThingsDictionary.Add(storyId, things);
			}

			return things;
		}

		private MemoryScribe GetOrCreateScribe(TextTrail storyId)
		{
			if (storyId == null)
				throw new ArgumentNullException(nameof(storyId));

			MemoryScribe? scribe = null;
			if(!_scribeDictionary.TryGetValue(storyId, out scribe))
			{
				scribe = new MemoryScribe(GetOrCreateListOfThings(storyId));
				_scribeDictionary.Add(storyId, scribe);
			}

			return scribe;
		}
		#endregion
	}
}
