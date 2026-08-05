using BigRedProf.Data.Core;
using BigRedProf.Stories;
using BigRedProf.Stories.Memory;
using BigRedProf.Stories.Models;
using Microsoft.AspNetCore.Mvc;

namespace BigRedProf.Stories.Api.Controllers;

[ApiController]
public class StorytellerController : ControllerBase
{
	#region fields
	private readonly IPiedPiper _piedPiper;
	private readonly MemoryStoryManager _storyManager;
	private readonly ILogger<StorytellerController> _logger;
	private readonly PackRat<ListOfStoryThings> _listOfStoryThingsPackRat;
	#endregion

	#region constructors
	public StorytellerController(IPiedPiper piedPiper, MemoryStoryManager storageManager, ILogger<StorytellerController> logger)
    {
		_piedPiper = piedPiper;
		_storyManager = storageManager;
        _logger = logger;

		_listOfStoryThingsPackRat = _piedPiper.GetPackRat<ListOfStoryThings>(StoriesSchemaId.ListOfStoryThings);
	}
	#endregion constructors

	#region web methods
	[HttpGet]
	[Route("v1/{storyIdHash}/[controller]/[action]/{bookmark}")]
	public ActionResult<bool> HasSomethingForMe(string storyIdHash, long bookmark)
	{
		if (!TextTrailSerializer.IsValidStoryIdHash(storyIdHash))
		{
			_logger.LogWarning(
				"Rejected HasSomethingForMe for malformed story ID hash: {storyIdHash}",
				storyIdHash
			);
			return BadRequest(
				"The story ID hash is not a multibase multihash string. Pass the hash of the " +
				"story ID rather than the story ID itself."
			);
		}

		TextTrail internalStoryId = TextTrailSerializer.ToInternalStoryId(storyIdHash);
		IStoryteller storyteller = _storyManager.GetStoryteller(internalStoryId);
		storyteller.SetBookmark(bookmark);
		bool hasSomethingForMe = storyteller.HasSomethingForMe;

		return Ok(hasSomethingForMe);
	}

	[HttpGet]
	[Route("v1/{storyIdHash}/[controller]/[action]/{bookmark}")]
	public IActionResult TellMeSomething(string storyIdHash, long bookmark, long? limit = null)
    {
		if (!TextTrailSerializer.IsValidStoryIdHash(storyIdHash))
		{
			_logger.LogWarning(
				"Rejected TellMeSomething for malformed story ID hash: {storyIdHash}",
				storyIdHash
			);
			return BadRequest(
				"The story ID hash is not a multibase multihash string. Pass the hash of the " +
				"story ID rather than the story ID itself."
			);
		}

		if (limit.HasValue && limit.Value < 1)
			return BadRequest("The 'limit' parameter must be at least 1.");

		IList<StoryThing> storyThings = limit.HasValue ?
			new List<StoryThing>((int)limit.Value) :
			new List<StoryThing>();

		TextTrail internalStoryId = TextTrailSerializer.ToInternalStoryId(storyIdHash);
		IStoryteller storyteller = _storyManager.GetStoryteller(internalStoryId);
		storyteller.SetBookmark(bookmark);

		bool hasReachedLimit = false;
		while (storyteller.HasSomethingForMe && !hasReachedLimit)
		{
			long expectedOffset = storyteller.Bookmark;
			StoryThing storyThing = storyteller.TellMeSomething();
			if (storyThing.Offset != expectedOffset)
			{
				throw new InvalidOperationException(
					$"Story corrupt. Expected offset {storyteller.Bookmark}. Actual offset {storyThing.Offset}"
				);
			}
			storyThings.Add(storyThing);
			hasReachedLimit = limit.HasValue && (storyThings.Count == limit.Value);
		}

		Response.ContentType = "application/octet-stream";
		using (CodeWriter writer = new CodeWriter(Response.Body))
		{
			ListOfStoryThings listOfStoryThings = new ListOfStoryThings()
			{
				StoryThings = storyThings
			};
			_listOfStoryThingsPackRat.PackModel(writer, listOfStoryThings);
		}

		return new EmptyResult();	// can't return "OK" here since we manually wrote to Response.Body
    }
	#endregion web methods
}
