using BigRedProf.Data;
using BigRedProf.Stories;
using Microsoft.AspNetCore.Mvc;

namespace BigRedProf.Stories.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class ScribeController : ControllerBase
{
	#region fields
	private readonly IPiedPiper _piedPiper;
	private readonly IScribe _scribe;
	private readonly ILogger<ScribeController> _logger;
	#endregion

	#region constructors
	public ScribeController(IPiedPiper piedPiper, IScribe scribe, ILogger<ScribeController> logger)
    {
		_piedPiper = piedPiper;
		_scribe = scribe;
        _logger = logger;
    }
	#endregion constructors

	#region web methods
	[HttpPost]
	[Route("/[controller]/[action]")]
	public void RecordSomething()
    {
		PackRat<Code> packRat = _piedPiper.GetPackRat<Code>(SchemaId.Code);

		Code something;
		using (CodeReader codeReader = new CodeReader(Request.Body))
		{
			something = packRat.UnpackModel(codeReader);
		}

		_scribe.RecordSomething(something);
    }
	#endregion web methods
}
