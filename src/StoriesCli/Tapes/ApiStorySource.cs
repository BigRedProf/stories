using BigRedProf.Data.Core;

namespace BigRedProf.Stories.StoriesCli.Tapes
{
	/// <summary>
	/// A running Stories service, as an <see cref="IStorySource"/>.
	/// </summary>
	public sealed class ApiStorySource : IStorySource
	{
		#region fields
		private readonly ApiClient _apiClient;
		#endregion

		#region constructors
		public ApiStorySource(ApiClient apiClient)
		{
			ArgumentNullException.ThrowIfNull(apiClient);

			_apiClient = apiClient;
		}
		#endregion

		#region IStorySource methods
		public Task<IReadOnlyList<StorySummary>> ListStoriesAsync()
		{
			return _apiClient.ListStoriesAsync();
		}

		public IStoryteller GetStoryteller(string storyIdHash, long bookmark, long? tellLimit)
		{
			return _apiClient.GetStorytellerByHash(storyIdHash, bookmark, tellLimit);
		}

		public Task RecordAsync(string storyIdHash, Code[] things)
		{
			return _apiClient.GetScribeByHash(storyIdHash).RecordSomethingAsync(things);
		}
		#endregion
	}
}
