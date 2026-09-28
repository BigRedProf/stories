using BigRedProf.Data.Core;
using BigRedProf.Stories.Memory;
using BigRedProf.Stories.StoriesCli.Tapes;

namespace BigRedProf.Stories.StoriesCli.Test._TestHelpers
{
	/// <summary>
	/// An in-memory Stories service, keyed the way the real one is: by the internal trail that
	/// wraps a story ID hash.
	/// </summary>
	internal sealed class MemoryStorySource : IStorySource
	{
		#region fields
		private readonly MemoryStoryManager _storyManager = new MemoryStoryManager();
		#endregion

		#region methods
		/// <summary>Writes <paramref name="things"/> to the story named <paramref name="storyId"/>.</summary>
		public string Record(string storyId, params Code[] things)
		{
			string storyIdHash = HashOf(storyId);
			_storyManager.GetScribe(TextTrailSerializer.ToInternalStoryId(storyIdHash)).RecordSomething(things);

			return storyIdHash;
		}

		/// <summary>Every thing in the story whose hash is <paramref name="storyIdHash"/>, in order.</summary>
		public IList<Code> ReadAll(string storyIdHash)
		{
			List<Code> things = new List<Code>();
			IStoryteller storyteller = _storyManager.GetStoryteller(TextTrailSerializer.ToInternalStoryId(storyIdHash));
			while (storyteller.HasSomethingForMe)
				things.Add(storyteller.TellMeSomething().Thing);

			return things;
		}
		#endregion

		#region functions
		public static string HashOf(string storyId)
		{
			return TextTrailSerializer.ToMultihashString(TextTrailSerializer.ParseTextRepresentation(storyId));
		}
		#endregion

		#region IStorySource methods
		public Task<IReadOnlyList<StorySummary>> ListStoriesAsync()
		{
			List<StorySummary> stories = new List<StorySummary>();
			foreach (KeyValuePair<TextTrail, long> story in _storyManager.GetStoryLengths())
			{
				stories.Add(
					new StorySummary()
					{
						StoryIdHash = story.Key.Segments[story.Key.Segments.Count - 1],
						Length = story.Value
					}
				);
			}

			stories.Sort((a, b) => string.CompareOrdinal(a.StoryIdHash, b.StoryIdHash));

			return Task.FromResult<IReadOnlyList<StorySummary>>(stories);
		}

		public IStoryteller GetStoryteller(string storyIdHash, long bookmark, long? tellLimit)
		{
			IStoryteller storyteller = _storyManager.GetStoryteller(TextTrailSerializer.ToInternalStoryId(storyIdHash));
			storyteller.SetBookmark(bookmark);

			return storyteller;
		}

		public Task RecordAsync(string storyIdHash, Code[] things)
		{
			_storyManager.GetScribe(TextTrailSerializer.ToInternalStoryId(storyIdHash)).RecordSomething(things);

			return Task.CompletedTask;
		}
		#endregion
	}
}
