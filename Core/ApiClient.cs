using BigRedProf.Data.Core;
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
		Action<ILoggingBuilder> _signalRLoggingBuilderCallback;
		#endregion

		#region constructors
		public ApiClient(Uri baseUri, IPiedPiper piedPiper, ILogger<ApiClient> logger, Action<ILoggingBuilder>? signalRLoggingBuilderCallback)
		{
			if (baseUri == null)
				throw new ArgumentNullException(nameof(baseUri));

			if (piedPiper == null)
				throw new ArgumentNullException(nameof(piedPiper));

			if (logger == null)
				throw new ArgumentNullException(nameof(logger));

			_baseUri = baseUri;
			_piedPiper = piedPiper;
			_logger = logger;
			_signalRLoggingBuilderCallback = signalRLoggingBuilderCallback ?? (_ => { });
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

		public IStoryListener GetStoryListener(
			long? tellLimit,
			TimeSpan pollingFrequency,
			StoryId storyId,
			long bookmark
		)
		{
			if (storyId == null)
				throw new ArgumentNullException(nameof(storyId));

			if (bookmark < 0)
				throw new ArgumentOutOfRangeException(nameof(bookmark));

			return new ApiStoryListener(_piedPiper, _logger, _signalRLoggingBuilderCallback, tellLimit, pollingFrequency, _baseUri, storyId, bookmark);
		}
		#endregion
	}
}
