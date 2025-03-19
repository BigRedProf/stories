using BigRedProf.Data.Core;
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
	public IActionResult RecordSomething(string story)
    {
		story = Helper.HackHackFixStoryId(story);

		ListOfThings listOfThings;
		using (CodeReader codeReader = new CodeReader(Request.Body))
		{
			try
			{
				listOfThings = _listOfThingsPackRat.UnpackModel(codeReader);
			}
			catch (Exception ex)
			{
				Guid correlationId = Guid.NewGuid();
				string message = ex.Message;
				_logger.LogWarning(
					ex, 
					"Failed to unpack ListOfThings model. Correlation ID: {correlationId} Message: {message}", 
					correlationId,  
					message
				);
				return BadRequest($"Failed to unpack ListOfThings model. Correlation ID: {correlationId}");
			}
		}

		IScribe scribe = _storyManager.GetScribe(story);
		scribe.RecordSomething(listOfThings.Things.ToArray());

		return Ok();
    }
	#endregion web methods
}
