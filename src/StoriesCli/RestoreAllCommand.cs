using BigRedProf.Stories.StoriesCli.Tapes;
using Microsoft.Extensions.Logging;

namespace BigRedProf.Stories.StoriesCli
{
	public sealed class RestoreAllCommand : TapeCommand
	{
		#region constructors
		public RestoreAllCommand(ILogger<ApiClient> apiClientLogger)
			: base(apiClientLogger)
		{
		}
		#endregion

		#region TapeCommand methods
		protected override async Task<int> RunAsync(BaseCommandLineOptions baseOptions)
		{
			RestoreAllOptions options = (RestoreAllOptions)baseOptions;
			TapeShelf shelf = new TapeShelf(options.TapeRoot);
			string generation = RequireLatestGeneration(shelf, options.Generation);

			TapeManifest manifest = shelf.LoadManifest(generation);
			TapeEngine engine = new TapeEngine(CreatePiedPiper(), shelf.OpenLibrary(generation), Guid.NewGuid);
			long thingsRestored = await engine.RestoreAsync(manifest, CreateSource(options));

			Console.WriteLine($"Restored {generation}: {manifest.Stories.Count} stories, {thingsRestored} things.");

			return ExitOk;
		}
		#endregion
	}
}
