using BigRedProf.Stories.StoriesCli.Tapes;
using Microsoft.Extensions.Logging;

namespace BigRedProf.Stories.StoriesCli
{
	public sealed class VerifyAllCommand : TapeCommand
	{
		#region constructors
		public VerifyAllCommand(ILogger<ApiClient> apiClientLogger)
			: base(apiClientLogger)
		{
		}
		#endregion

		#region TapeCommand methods
		protected override async Task<int> RunAsync(BaseCommandLineOptions baseOptions)
		{
			VerifyAllOptions options = (VerifyAllOptions)baseOptions;
			TapeShelf shelf = new TapeShelf(options.TapeRoot);
			string generation = RequireLatestGeneration(shelf, options.Generation);

			TapeManifest manifest = shelf.LoadManifest(generation);
			TapeEngine engine = new TapeEngine(CreatePiedPiper(), shelf.OpenLibrary(generation), Guid.NewGuid);

			// The live comparison is optional: a drill verifies tapes with no service at all.
			IStorySource? live = options.BaseUri == null ? null : CreateSource(options);
			IReadOnlyList<string> problems = await engine.VerifyAsync(manifest, live);

			foreach (string problem in problems)
				Console.Error.WriteLine("PROBLEM: " + problem);

			Console.WriteLine(
				$"Verified {generation}: {manifest.Stories.Count} stories, " +
				$"{manifest.Stories.Values.Sum(story => story.Length)} things, {problems.Count} problems.");

			return problems.Count == 0 ? ExitOk : ExitProblems;
		}
		#endregion
	}
}
