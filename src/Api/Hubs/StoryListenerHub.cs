using Microsoft.AspNetCore.SignalR;

using BigRedProf.Data.Core;

namespace BigRedProf.Stories.Api.Hubs
{
	public class StoryListenerHub : Hub
	{
		#region fields
		private readonly StoryListenerManager _storyListenerManager;
		private readonly ILogger<StoryListenerHub> _logger;
		#endregion

		#region constructors
		public StoryListenerHub(StoryListenerManager storyListenerManager, ILogger<StoryListenerHub> logger)
		{
			_storyListenerManager = storyListenerManager;
			_logger = logger;
		}
		#endregion constructors

		#region methods
		public async Task StartListeningToStory(string storyIdHash)
		{
			// Validate before joining the group, so a malformed hash never becomes a
			// SignalR group name nor reaches the story listener manager.
			ThrowIfStoryIdHashIsInvalid(storyIdHash, nameof(StartListeningToStory));

			string clientId = Context.ConnectionId;

			// add this client to the SignalR group for this story
			await Groups.AddToGroupAsync(clientId, storyIdHash);

			// inform the story listener manager
			_storyListenerManager.StartListeningToStory(clientId, storyIdHash);
		}

		public async Task StopListeningToStory(string storyIdHash)
		{
			ThrowIfStoryIdHashIsInvalid(storyIdHash, nameof(StopListeningToStory));

			string clientId = Context.ConnectionId;

			// remove this client from the SignalR group for this story
			await Groups.RemoveFromGroupAsync(clientId, storyIdHash);

			// inform the story listener manager
			_storyListenerManager.StopListeningToStory(clientId, storyIdHash);
		}
		#endregion

		#region private methods
		private void ThrowIfStoryIdHashIsInvalid(string storyIdHash, string hubMethod)
		{
			if (TextTrailSerializer.IsValidStoryIdHash(storyIdHash))
				return;

			_logger.LogWarning(
				"Rejected {hubMethod} for malformed story ID hash: {storyIdHash}",
				hubMethod,
				storyIdHash
			);
			throw new HubException(
				"The story ID hash is not a multibase multihash string. Pass the hash of the " +
				"story ID rather than the story ID itself."
			);
		}
		#endregion

		#region Hub methods
		public override async Task OnConnectedAsync()
		{
			await base.OnConnectedAsync();
		}

		public override async Task OnDisconnectedAsync(Exception? exception)
		{
			string clientId = Context.ConnectionId;
			_storyListenerManager.DisconnectClient(clientId);

			await base.OnDisconnectedAsync(exception);
		}
		#endregion
	}
}
