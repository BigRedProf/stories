using BigRedProf.Data;
using BigRedProf.Stories.Internal.ApiClient;
using Microsoft.Extensions.Logging;
using System;

namespace BigRedProf.Stories
{
	public class ApiClient
	{
		#region fields
		private Uri _baseUri;
		private IPiedPiper _piedPiper;
		ILogger<ApiClient> _logger;
		#endregion

		#region constructors
		public ApiClient(Uri baseUri, IPiedPiper piedPiper, ILogger<ApiClient> logger)
		{
			if(baseUri == null)
				throw new ArgumentNullException(nameof(baseUri));

			if(piedPiper == null)
				throw new ArgumentNullException(nameof(piedPiper));

			if(logger == null)
				throw new ArgumentNullException(nameof(logger));

			_baseUri = baseUri;
			_piedPiper = piedPiper;
			_logger = logger;
		}
		#endregion

		#region methods
		public IScribe GetScribe(StoryId storyId)
		{
			if (storyId == null) 
				throw new ArgumentNullException(nameof(storyId));

			return new ApiScribe(_baseUri, storyId, _piedPiper);
		}

		public IStoryteller GetStoryteller(StoryId storyId, long bookmark, long? tellLimit)
		{
			if (storyId == null)
				throw new ArgumentNullException(nameof(storyId));

			if(bookmark < 0)
				throw new ArgumentOutOfRangeException(nameof(bookmark));

			return new ApiStoryteller(_baseUri, storyId, _piedPiper, bookmark, tellLimit);
		}

		public IStoryListener GetStoryListener(StoryId storyId, long bookmark, long? tellLimit, TimeSpan timerPollingFrequency)
		{
			if (storyId == null)
				throw new ArgumentNullException(nameof(storyId));

			if (bookmark < 0)
				throw new ArgumentOutOfRangeException(nameof(bookmark));

			return new ApiStoryListener(_baseUri, storyId, _piedPiper, _logger, bookmark, tellLimit, timerPollingFrequency);
		}

		public IStoryListener GetStoryListener(
			StoryId storyId, 
			long bookmark,
			long? tellLimit,
			TimeSpan timerPollingFrequency,
			LogLevel? signalRLogLevel,
			ILoggerProvider? loggerProvider,
			bool addConsoleLogging
		)
		{
			if (storyId == null)
				throw new ArgumentNullException(nameof(storyId));

			if (bookmark < 0)
				throw new ArgumentOutOfRangeException(nameof(bookmark));

			return new ApiStoryListener(
				_baseUri, storyId, _piedPiper, _logger, bookmark, tellLimit, timerPollingFrequency, 
				signalRLogLevel, loggerProvider, addConsoleLogging
			);
		}
		#endregion
	}
}
