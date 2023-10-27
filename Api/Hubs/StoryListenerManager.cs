using BigRedProf.Data;
using BigRedProf.Stories.Events;
using BigRedProf.Stories.Memory;
using BigRedProf.Stories.Models;
using Microsoft.AspNetCore.SignalR;

namespace BigRedProf.Stories.Api.Hubs
{
	// TODO: review concurrency logic (was written quickly and doesn't feel right)
	public class StoryListenerManager
	{
		#region fields
		private readonly object _startStopLock;
		private readonly object _disconnectLock;
		private readonly IPiedPiper _piedPiper;
		private readonly MemoryStoryManager _storyManager;
		private readonly ILogger<StoryListenerManager> _logger;
		private readonly IHubContext<StoryListenerHub> _hubContext;
		private IDictionary<StoryId, MemoryStoryListener> _storyToStoryListenerMap;
		private IDictionary<StoryId, HashSet<string>> _storyToClientsMap;
		private IDictionary<string, HashSet<StoryId>> _clientToStoriesMap;
		#endregion

		#region constructors
		public StoryListenerManager(
			IPiedPiper piedPiper, 
			MemoryStoryManager storageManager, 
			ILogger<StoryListenerManager> logger,
			IHubContext<StoryListenerHub> hubContext
		)
		{
			_startStopLock = new object();
			_disconnectLock = new object();

			_piedPiper = piedPiper;
			_storyManager = storageManager;
			_logger = logger;
			_hubContext = hubContext;

			_storyToStoryListenerMap = new Dictionary<StoryId, MemoryStoryListener>();
			_storyToClientsMap = new Dictionary<StoryId, HashSet<string>>();
			_clientToStoriesMap = new Dictionary<string, HashSet<StoryId>>();
		}
		#endregion constructors

		#region methods
		public void StartListeningToStory(string clientId, string storyId)
		{
			lock(_startStopLock)
			{
				// if nobody's listening to this story yet, create a new storyteller
				CreateStoryListener(storyId);

				// associate this client with this story
				HashSet<string> clientsSet = GetClientsSet(storyId);
				clientsSet.Add(clientId);

				// associate this story with this client
				HashSet<StoryId> storiesSet = GetStoriesSet(clientId);
				storiesSet.Add(storyId);
			}
		}

		public void StopListeningToStory(string clientId, string storyId)
		{
			lock(_startStopLock)
			{
				// dissociate this story from this client
				HashSet<StoryId> storiesSet = GetStoriesSet(clientId);
				storiesSet.Remove(storyId);

				// dissociate this client from this story
				HashSet<string> clientsSet = GetClientsSet(storyId);
				clientsSet.Remove(clientId);

				// if there are no more clients listening, we can stop listening too
				if (clientsSet.Count == 0)
					DestroyStoryListener(storyId);
			}
		}

		public void DisconnectClient(string clientId)
		{
			lock (_disconnectLock)
			{
				// if the client didn't already stop listening, make it stop listening now
				if (_clientToStoriesMap.ContainsKey(clientId))
				{
					HashSet<StoryId> storyIds = _clientToStoriesMap[clientId];
					foreach (StoryId storyId in storyIds)
						StopListeningToStory(clientId, storyId.ToString());
				}
			}
		}
		#endregion

		#region event handlers
		private async Task StoryListener_SomethingHappenedAsync(object? sender, SomethingHappenedEventArgs e)
		{
			MemoryStoryListener memoryStoryListener = (MemoryStoryListener)sender!;
			IHubClients hubClients = _hubContext.Clients;
			IClientProxy clientProxy = hubClients.Group(memoryStoryListener.StoryId.ToString());
			byte[] thingAsByteArray = GetByteArrayFromStoryThing(e.Thing);
			await clientProxy.SendAsync(
				"SomethingHappened",
				e.Thing.Offset,
				thingAsByteArray
			);
		}
		#endregion

		#region private methods
		private MemoryStoryListener CreateStoryListener(StoryId storyId)
		{
			MemoryStoryListener? storyListener;
			if (!_storyToStoryListenerMap.TryGetValue(storyId, out storyListener))
			{
				storyListener = _storyManager.GetStoryListener(storyId);
				_storyToStoryListenerMap.Add(storyId, storyListener);
				storyListener.SomethingHappenedAsync += StoryListener_SomethingHappenedAsync;
				storyListener.StartListening();
			}

			return storyListener;
		}

		private void DestroyStoryListener(StoryId storyId)
		{
			if (_storyToStoryListenerMap.TryGetValue(storyId, out MemoryStoryListener? storyListener))
			{
				storyListener.StopListening();
				storyListener.SomethingHappenedAsync -= StoryListener_SomethingHappenedAsync;
				_storyToStoryListenerMap.Remove(storyId);
			}
		}

		private HashSet<string> GetClientsSet(StoryId storyId)
		{
			HashSet<string>? clientsMap;
			if(!_storyToClientsMap.TryGetValue(storyId, out clientsMap))
			{
				clientsMap = new HashSet<string>();
				_storyToClientsMap.Add(storyId, clientsMap);
			}

			return clientsMap;
		}

		private HashSet<StoryId> GetStoriesSet(string clientId)
		{
			HashSet<StoryId>? storiesMap;
			if(!_clientToStoriesMap.TryGetValue(clientId, out storiesMap))
			{
				storiesMap = new HashSet<StoryId>();
				_clientToStoriesMap.Add(clientId, storiesMap);
			}

			return storiesMap;
		}

		private byte[] GetByteArrayFromStoryThing(StoryThing storyThing)
		{
			MemoryStream memoryStream = new MemoryStream(storyThing.Thing.Length / 8 + 1 + 4);
			PackRat<StoryThing> packRat = _piedPiper.GetPackRat<StoryThing>(StoriesSchemaId.StoryThing);
			using (CodeWriter writer = new CodeWriter(memoryStream))
			{
				packRat.PackModel(writer, storyThing);
			}
			
			return memoryStream.ToArray();
		}
		#endregion
	}
}
