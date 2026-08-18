using BigRedProf.Data.Core;
using BigRedProf.Stories.Internal.ApiClient;
using BigRedProf.Stories.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.Management.Automation;

namespace BigRedProf.Stories.PowerShell
{
	/// <summary>
	/// Reads a range of things from a story and returns them as objects.
	/// </summary>
	/// <remarks>
	/// This RETURNS, which is the point. Every question worth asking of a story
	/// ("everything this agent did", "offsets 200-250") wants to finish; tailing is
	/// <see cref="WatchStoryCommand"/>'s job.
	/// </remarks>
	/// <example>
	///   <code>Get-Story bigredprof/digihouse/catalog -BaseUri http://localhost:43027</code>
	/// </example>
	[Cmdlet(VerbsCommon.Get, "Story")]
	[OutputType(typeof(StoryThingInfo))]
	public sealed class GetStoryCommand : StoryCmdletBase
	{
		#region constants
		// The service fetches in batches; this is the batch size, not a result limit.
		private const long BatchSize = 1000;
		#endregion

		#region parameters
		/// <summary>
		/// Stop after this many things. Zero, the default, reads to the end of the story.
		/// </summary>
		[Parameter]
		public long First { get; set; }
		#endregion

		#region PSCmdlet methods
		protected override void ProcessRecord()
		{
			IPiedPiper piedPiper = BuildPiedPiper();
			TextTrail storyId = ParseStoryId();

			ApiClient apiClient = new ApiClient(
				BaseUri, piedPiper, NullLogger<ApiClient>.Instance, null);
			IStoryteller storyteller = apiClient.GetStoryteller(storyId, Bookmark, BatchSize);

			long told = 0;
			while (!Stopping)
			{
				if (First > 0 && told >= First)
					break;

				if (!RunWithoutContext(() => storyteller.HasSomethingForMeAsync()))
					break;

				StoryThing thing = RunWithoutContext(() => storyteller.TellMeSomethingAsync());
				WriteObject(ToStoryThingInfo(piedPiper, thing));

				++told;
			}
		}
		#endregion
	}
}
