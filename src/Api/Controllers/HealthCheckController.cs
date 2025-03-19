using BigRedProf.Stories.Api.Hubs;
using Microsoft.AspNetCore.Mvc;

namespace BigRedProf.Stories.Api.Controllers
{
	[ApiController]
	[Route("health")]
	public class HealthCheckController : ControllerBase
	{
		private readonly StoryListenerManager _storyListenerManager;

		public HealthCheckController(StoryListenerManager storyListenerManager)
		{
			_storyListenerManager = storyListenerManager;
		}

		[HttpGet]
		public IActionResult GetHealth()
		{
			// Basic health check logic
			bool roomServiceHealthy = _storyListenerManager != null;

			// Add additional checks here if needed
			if (roomServiceHealthy)
			{
				return Ok(new
				{
					Status = "Healthy",
					RoomService = "Operational"
				});
			}

			return StatusCode(503, new
			{
				Status = "Unhealthy",
				RoomService = "Not Operational"
			});
		}
	}
}
