using BigRedProf.Stories.StoriesCli.Tapes;
using Microsoft.Extensions.Logging;

namespace BigRedProf.Stories.StoriesCli
{
	public sealed class BackupAllCommand : TapeCommand
	{
		#region constructors
		public BackupAllCommand(ILogger<ApiClient> apiClientLogger)
			: base(apiClientLogger)
		{
		}
		#endregion

		#region TapeCommand methods
		protected override async Task<int> RunAsync(BaseCommandLineOptions baseOptions)
		{
			BackupAllOptions options = (BackupAllOptions)baseOptions;
			TapeShelf shelf = new TapeShelf(options.TapeRoot);
			string generation = string.IsNullOrWhiteSpace(options.Generation)
				? TapeShelf.GenerationFor(DateTime.UtcNow)
				: options.Generation;

			TapeManifest manifest = shelf.LoadManifest(generation);
			TapeEngine engine = new TapeEngine(CreatePiedPiper(), shelf.OpenLibrary(generation), Guid.NewGuid);
			BackupResult result = await engine.BackupAsync(CreateSource(options), manifest);

			// The commit point: segments written above are named only from here on.
			manifest.UpdatedUtc = DateTime.UtcNow;
			shelf.SaveManifest(manifest);

			Console.WriteLine(
				$"Backed up {generation}: {result.StoriesGrown} stories grew by {result.ThingsWritten} things; " +
				$"{manifest.Stories.Count} stories on tape.");

			return ExitOk;
		}
		#endregion
	}
}
