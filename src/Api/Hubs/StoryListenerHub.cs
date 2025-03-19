using Microsoft.AspNetCore.SignalR;

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
		public async Task StartListeningToStory(string storyId)
		{
			string clientId = Context.ConnectionId;

			// add this client to the SignalR group for this story
			await Groups.AddToGroupAsync(clientId, storyId);

			// inform the story listener manager
			_storyListenerManager.StartListeningToStory(clientId, storyId);
		}

		public async Task StopListeningToStory(string storyId)
		{
			string clientId = Context.ConnectionId;

			// remove this client from the SignalR group for this story
			await Groups.RemoveFromGroupAsync(clientId, storyId);

			// inform the story listener manager
			_storyListenerManager.StopListeningToStory(clientId, storyId);
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
