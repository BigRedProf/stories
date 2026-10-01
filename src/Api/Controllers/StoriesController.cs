using BigRedProf.Data.Core;
using BigRedProf.Stories.Memory;
using Microsoft.AspNetCore.Mvc;

namespace BigRedProf.Stories.Api.Controllers;

/// <summary>
/// What the service holds, as a whole.
/// </summary>
/// <remarks>
/// For backing everything up (digihouse#397): a list of stories kept by the application is a
/// list that forgets one silently, and a story left off it is a story that is never backed up.
/// The service is the one place that knows every story it has. It knows them by hash only,
/// because a hash is all a client ever sends it.
/// </remarks>
[ApiController]
public class StoriesController : ControllerBase
{
	#region fields
	private readonly MemoryStoryManager _storyManager;
	#endregion

	#region constructors
	public StoriesController(MemoryStoryManager storyManager)
	{
		_storyManager = storyManager;
	}
	#endregion

	#region web methods
	[HttpGet]
	[Route("v1/stories")]
	public ActionResult<IList<StorySummary>> ListStories()
	{
		List<StorySummary> stories = new List<StorySummary>();
		foreach (KeyValuePair<TextTrail, long> story in _storyManager.GetStoryLengths())
		{
			// Every story the Api writes is keyed internally by the trail that wraps its hash
			// (TextTrailSerializer.ToInternalStoryId); its last segment is the hash.
			string storyIdHash = story.Key.Segments[story.Key.Segments.Count - 1];
			if (!TextTrailSerializer.IsValidStoryIdHash(storyIdHash))
				continue;

			stories.Add(new StorySummary() { StoryIdHash = storyIdHash, Length = story.Value });
		}

		stories.Sort((a, b) => string.CompareOrdinal(a.StoryIdHash, b.StoryIdHash));

		return Ok(stories);
	}
	#endregion
}
