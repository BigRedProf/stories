using BigRedProf.Data;
using BigRedProf.Stories;
using BigRedProf.Stories.Api.Internal;
using BigRedProf.Stories.Memory;
using BigRedProf.Stories.Models;
using Microsoft.AspNetCore.Mvc;

namespace BigRedProf.Stories.Api.Controllers;

[ApiController]
public class ScribeController : ControllerBase
{
	#region fields
	private readonly IPiedPiper _piedPiper;
	private readonly MemoryStoryManager _storyManager;
	private readonly ILogger<ScribeController> _logger;
	private readonly PackRat<ListOfThings> _listOfThingsPackRat;
	#endregion

	#region constructors
	public ScribeController(IPiedPiper piedPiper, MemoryStoryManager storyManager, ILogger<ScribeController> logger)
    {
		_piedPiper = piedPiper;
		_storyManager = storyManager;
        _logger = logger;

		_listOfThingsPackRat = _piedPiper.GetPackRat<ListOfThings>(StoriesSchemaId.ListOfThings);
	}
	#endregion constructors

	#region web methods
	[HttpPost]
	[Route("v1/{story}/[controller]/[action]")]
	public void RecordSomething(string story)
    {
		story = Helper.HackHackFixStoryId(story);

		ListOfThings listOfThings;
		using (CodeReader codeReader = new CodeReader(Request.Body))
		{
			listOfThings = _listOfThingsPackRat.UnpackModel(codeReader);
		}

		IScribe scribe = _storyManager.GetScribe(story);
		scribe.RecordSomething(listOfThings.Things.ToArray());
    }
	#endregion web methods
}
