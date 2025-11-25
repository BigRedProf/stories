using BigRedProf.Data.Core;
using BigRedProf.Stories.Models;
using BigRedProf.Data.Tape;
using Microsoft.Extensions.Logging;
using BigRedProf.Data.Tape.Libraries;

namespace BigRedProf.Stories.StoriesCli
{
	public sealed class BackupCommand : Command
	{
		#region constants
		private const long TellLimit = 50_000;
		#endregion

		#region fields
		private readonly ILogger<BackupCommand> _logger;
		private readonly ILogger<ApiClient> _apiClientLogger;
		#endregion

		#region constructors
		public BackupCommand(ILogger<BackupCommand> logger, ILogger<ApiClient> apiClientLogger)
		{
			_logger = logger;
			_apiClientLogger = apiClientLogger;
		}
		#endregion

		#region Command methods
		public override int Run(BaseCommandLineOptions baseOpts)
		{
			BackupOptions options = (BackupOptions)baseOpts;

			IPiedPiper piedPiper = new PiedPiper();
			piedPiper.RegisterCorePackRats();
			piedPiper.RegisterPackRats(typeof(StoryThing).Assembly);

			ApiClient apiClient = new ApiClient(options.BaseUri!, piedPiper, _apiClientLogger, null);
			IStoryteller storyteller = apiClient.GetStoryteller(options.StoryId, 0, TellLimit);

			DiskLibrary diskLibrary = new DiskLibrary(options.TapeRoot);
			BackupWizard backupWizard = BackupWizard.OpenExisting(diskLibrary, options.SeriesId);

			BackupEngine.BackupStoryToSeries(
				piedPiper,
				storyteller,
				diskLibrary,
				options.SeriesId,
				options.IncrementalBackup ?? false,
				_logger
			);

			return 0;
		}

		protected override void OnCancelKeyPress()
		{
		} 
		#endregion
	}
}
