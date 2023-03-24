using BigRedProf.Data;
using BigRedProf.Stories;
using BigRedProf.Stories.Memory;
using Microsoft.AspNetCore.Mvc;

namespace BigRedProf.Stories.Api.Controllers;

[ApiController]
public class StorytellerController : ControllerBase
{
	#region fields
	private readonly IPiedPiper _piedPiper;
	private readonly MemoryStoryManager _storyManager;
	private readonly ILogger<ScribeController> _logger;
	#endregion

	#region constructors
	public StorytellerController(IPiedPiper piedPiper, MemoryStoryManager storageManager, ILogger<ScribeController> logger)
    {
		_piedPiper = piedPiper;
		_storyManager = storageManager;
        _logger = logger;
    }
	#endregion constructors

	#region web methods
	[HttpGet]
	[Route("{story}/[controller]/[action]/{bookmark}")]
	public void TellMeSomething(string story, long bookmark)
    {
		PackRat<Code> packRat = _piedPiper.GetPackRat<Code>(SchemaId.Code);

		IStoryteller storyteller = _storyManager.GetStoryteller(story);
		Code thing = storyteller.TellMeSomething();
		storyteller.SetBookmark(bookmark);

		Response.ContentType = "application/octet-stream";
		using (CodeWriter writer = new CodeWriter(Response.Body))
		{
			packRat.PackModel(writer, thing);
		}
    }
	#endregion web methods
}