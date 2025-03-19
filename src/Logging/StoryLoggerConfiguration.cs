using Microsoft.Extensions.Logging;

namespace BigRedProf.Stories.Logging
{
	[ProviderAlias("StoryLogger")]
	public class StoryLoggerConfiguration
	{
		#region properties
		public string BaseStoryUrl { get; set; } = default!;
		public string StoryId { get; set; } = default!;
		#endregion
	}
}
