using BigRedProf.Data;
using BigRedProf.Stories;
using Microsoft.AspNetCore.Mvc;

namespace BigRedProf.Stories.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class StorytellerController : ControllerBase
{
	#region fields
	private readonly IPiedPiper _piedPiper;
	private readonly IStoryteller _storyteller;
	private readonly ILogger<ScribeController> _logger;
	#endregion

	#region constructors
	public StorytellerController(IPiedPiper piedPiper, IStoryteller storyteller, ILogger<ScribeController> logger)
    {
		_piedPiper = piedPiper;
		_storyteller = storyteller;
        _logger = logger;
    }
	#endregion constructors

	#region web methods
	[HttpGet]
	[Route("/[controller]/[action]/{bookmark}")]
	public void TellMeSomething(long bookmark)
    {
		PackRat<Code> packRat = _piedPiper.GetPackRat<Code>(SchemaId.Code);

		Code thing = _storyteller.TellMeSomething();
		_storyteller.SetBookmark(bookmark);

		Response.ContentType = "application/octet-stream";
		using (CodeWriter writer = new CodeWriter(Response.Body))
		{
			packRat.PackModel(writer, thing);
		}
    }
	#endregion web methods
}
