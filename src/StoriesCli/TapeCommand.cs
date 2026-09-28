using BigRedProf.Data.Core;
using BigRedProf.Stories.Data;
using BigRedProf.Stories.StoriesCli.Tapes;
using Microsoft.Extensions.Logging;

namespace BigRedProf.Stories.StoriesCli
{
	/// <summary>
	/// What <c>backup-all</c>, <c>restore-all</c> and <c>verify-all</c> share: the shelf, the
	/// service, and what their exit codes mean.
	/// </summary>
	/// <remarks>
	/// Exit codes are for the scripts that schedule these (digihouse#397): 0 done, 2 the tapes
	/// have problems, 3 refused -- the tapes and the service disagree in a way that must not be
	/// worked around -- and 4 failed for any other reason.
	/// </remarks>
	public abstract class TapeCommand : Command
	{
		#region constants
		public const int ExitOk = 0;
		public const int ExitProblems = 2;
		public const int ExitRefused = 3;
		public const int ExitFailed = 4;
		#endregion

		#region fields
		private readonly ILogger<ApiClient> _apiClientLogger;
		#endregion

		#region constructors
		protected TapeCommand(ILogger<ApiClient> apiClientLogger)
		{
			ArgumentNullException.ThrowIfNull(apiClientLogger);

			_apiClientLogger = apiClientLogger;
		}
		#endregion

		#region Command methods
		public override int Run(BaseCommandLineOptions options)
		{
			int exitCode;
			try
			{
				exitCode = RunAsync(options).GetAwaiter().GetResult();
			}
			catch (TapeException ex)
			{
				Console.Error.WriteLine("REFUSED: " + ex.Message);
				exitCode = ExitRefused;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine("FAILED: " + ex);
				exitCode = ExitFailed;
			}

			return exitCode;
		}

		protected override void OnCancelKeyPress()
		{
		}
		#endregion

		#region protected methods
		protected abstract Task<int> RunAsync(BaseCommandLineOptions options);

		protected IStorySource CreateSource(BaseCommandLineOptions options)
		{
			if (options.BaseUri == null)
				throw new ArgumentException("--baseUri is required: the stories service to use.");

			return new ApiStorySource(new ApiClient(options.BaseUri, CreatePiedPiper(), _apiClientLogger, null));
		}
		#endregion

		#region protected functions
		protected static IPiedPiper CreatePiedPiper()
		{
			IPiedPiper piedPiper = new PiedPiper();
			piedPiper.RegisterCorePackRats();
			piedPiper.RegisterPackRats(typeof(StoryThing).Assembly);

			return piedPiper;
		}

		protected static string RequireLatestGeneration(TapeShelf shelf, string? generation)
		{
			string? resolved = string.IsNullOrWhiteSpace(generation) ? shelf.FindLatestGeneration() : generation;
			if (resolved == null)
				throw new TapeException("There are no tapes to use: no generation has a manifest.");

			return resolved;
		}
		#endregion
	}
}
