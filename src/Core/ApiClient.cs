using BigRedProf.Data.Core;
using BigRedProf.Stories.Internal.ApiClient;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

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
		public IScribe GetScribe(TextTrail storyId)
		{
			if (storyId == null)
				throw new ArgumentNullException(nameof(storyId));

			return new ApiScribe(_baseUri, TextTrailSerializer.ToMultihashString(storyId), _piedPiper);
		}

		/// <summary>
		/// A scribe for the story whose hash is <paramref name="storyIdHash"/>, for tools that
		/// only ever know stories by hash -- which is all the service itself knows them by.
		/// </summary>
		public IScribe GetScribeByHash(string storyIdHash)
		{
			ThrowIfNotAStoryIdHash(storyIdHash);

			return new ApiScribe(_baseUri, storyIdHash, _piedPiper);
		}

		public IStoryteller GetStoryteller(TextTrail storyId, long bookmark, long? tellLimit)
		{
			if (storyId == null)
				throw new ArgumentNullException(nameof(storyId));

			if(bookmark < 0)
				throw new ArgumentOutOfRangeException(nameof(bookmark));

			return new ApiStoryteller(_baseUri, TextTrailSerializer.ToMultihashString(storyId), _piedPiper, bookmark, tellLimit);
		}

		/// <summary>
		/// A storyteller for the story whose hash is <paramref name="storyIdHash"/>. See
		/// <see cref="GetScribeByHash(string)"/>.
		/// </summary>
		public IStoryteller GetStorytellerByHash(string storyIdHash, long bookmark, long? tellLimit)
		{
			ThrowIfNotAStoryIdHash(storyIdHash);

			if (bookmark < 0)
				throw new ArgumentOutOfRangeException(nameof(bookmark));

			return new ApiStoryteller(_baseUri, storyIdHash, _piedPiper, bookmark, tellLimit);
		}

		/// <summary>
		/// Every story the service holds anything in, by hash, with how many things each holds.
		/// </summary>
		/// <remarks>
		/// For backing up everything: a list kept by hand is a list that forgets a story
		/// silently, and a list from the service holding them is complete by construction.
		/// </remarks>
		public async Task<IReadOnlyList<StorySummary>> ListStoriesAsync()
		{
			using (HttpClient client = new HttpClient())
			{
				List<StorySummary>? stories = await client.GetFromJsonAsync<List<StorySummary>>(
					new Uri(_baseUri, "v1/stories"));

				return stories ?? new List<StorySummary>();
			}
		}

		public IStoryListener GetStoryListener(
			long? tellLimit,
			TimeSpan pollingFrequency,
			TextTrail storyId,
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

		#region private functions
		private static void ThrowIfNotAStoryIdHash(string storyIdHash)
		{
			if (!TextTrailSerializer.IsValidStoryIdHash(storyIdHash))
			{
				throw new ArgumentException(
					"Not a story ID hash. Pass TextTrailSerializer.ToMultihashString(storyId).",
					nameof(storyIdHash));
			}
		}
		#endregion
	}
}
