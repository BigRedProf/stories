using BigRedProf.Data;
using BigRedProf.Stories;
using BigRedProf.Stories.Api.Internal;
using BigRedProf.Stories.Memory;
using Microsoft.AspNetCore.Mvc;

namespace BigRedProf.Stories.Api.Controllers;

[ApiController]
public class ScribeController : ControllerBase
{
	#region fields
	private readonly IPiedPiper _piedPiper;
	private readonly MemoryStoryManager _storyManager;
	private readonly ILogger<ScribeController> _logger;
	#endregion

	#region constructors
	public ScribeController(IPiedPiper piedPiper, MemoryStoryManager storyManager, ILogger<ScribeController> logger)
    {
		_piedPiper = piedPiper;
		_storyManager = storyManager;
        _logger = logger;
    }
	#endregion constructors

	#region web methods
	[HttpPost]
	[Route("v1/{story}/[controller]/[action]")]
	public void RecordSomething(string story)
    {
		story = Helper.HackHackFixStoryId(story);

		PackRat<Code> packRat = _piedPiper.GetPackRat<Code>(SchemaId.Code);

		Code something;
		using (CodeReader codeReader = new CodeReader(Request.Body))
		{
			something = packRat.UnpackModel(codeReader);
		}

		IScribe scribe = _storyManager.GetScribe(story);
		scribe.RecordSomething(something);
    }
	#endregion web methods
}
