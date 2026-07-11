using BigRedProf.Data.Core;
using BigRedProf.Stories.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;

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
