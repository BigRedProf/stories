using BigRedProf.Data.Core;

namespace BigRedProf.Stories.StoriesCli.Tapes
{
	/// <summary>
	/// The stories a tape engine backs up from or restores into, known by hash.
	/// </summary>
	/// <remarks>
	/// A seam so the engine can be tested against memory rather than a running service. The
	/// service only ever knows a story by the hash of its id, so that is all this knows too.
	/// </remarks>
	public interface IStorySource
	{
		#region methods
		/// <summary>Every story holding at least one thing, with its length.</summary>
		Task<IReadOnlyList<StorySummary>> ListStoriesAsync();

		/// <summary>Tells the story's things from <paramref name="bookmark"/> on.</summary>
		IStoryteller GetStoryteller(string storyIdHash, long bookmark, long? tellLimit);

		/// <summary>Appends <paramref name="things"/> to the story, in order.</summary>
		Task RecordAsync(string storyIdHash, Code[] things);
		#endregion
	}
}
