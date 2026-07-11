using BigRedProf.Data.Core;
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
		private IDictionary<string, MemoryStoryListener> _storyToStoryListenerMap;
		private IDictionary<MemoryStoryListener, string> _storyListenerToStoryIdHashMap;
		private IDictionary<string, HashSet<string>> _storyToClientsMap;
		private IDictionary<string, HashSet<string>> _clientToStoriesMap;
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

			_storyToStoryListenerMap = new Dictionary<string, MemoryStoryListener>();
			_storyListenerToStoryIdHashMap = new Dictionary<MemoryStoryListener, string>();
			_storyToClientsMap = new Dictionary<string, HashSet<string>>();
			_clientToStoriesMap = new Dictionary<string, HashSet<string>>();
		}
		#endregion constructors

		#region methods
		public void StartListeningToStory(string clientId, string storyIdHash)
		{
			lock(_startStopLock)
			{
				// if nobody's listening to this story yet, create a new storyteller
				CreateStoryListener(storyIdHash);

				// associate this client with this story
				HashSet<string> clientsSet = GetClientsSet(storyIdHash);
				clientsSet.Add(clientId);

				// associate this story with this client
				HashSet<string> storiesSet = GetStoriesSet(clientId);
				storiesSet.Add(storyIdHash);
			}
		}

		public void StopListeningToStory(string clientId, string storyIdHash)
		{
			lock(_startStopLock)
			{
				// dissociate this story from this client
				HashSet<string> storiesSet = GetStoriesSet(clientId);
				storiesSet.Remove(storyIdHash);

				// dissociate this client from this story
				HashSet<string> clientsSet = GetClientsSet(storyIdHash);
				clientsSet.Remove(clientId);

				// if there are no more clients listening, we can stop listening too
				if (clientsSet.Count == 0)
					DestroyStoryListener(storyIdHash);
			}
		}

		public void DisconnectClient(string clientId)
		{
			lock (_disconnectLock)
			{
				// if the client didn't already stop listening, make it stop listening now
				if (_clientToStoriesMap.ContainsKey(clientId))
				{
					HashSet<string> storyIdHashes = new HashSet<string>(_clientToStoriesMap[clientId]);
					foreach (string storyIdHash in storyIdHashes)
						StopListeningToStory(clientId, storyIdHash);
				}
			}
		}
		#endregion

		#region event handlers
		private async Task StoryListener_SomethingHappenedAsync(object? sender, SomethingHappenedEventArgs e)
		{
			MemoryStoryListener memoryStoryListener = (MemoryStoryListener)sender!;
			IHubClients hubClients = _hubContext.Clients;
			string storyIdHash = _storyListenerToStoryIdHashMap[memoryStoryListener];
			IClientProxy clientProxy = hubClients.Group(storyIdHash);
			byte[] thingAsByteArray = GetByteArrayFromStoryThing(e.Thing);
			await clientProxy.SendAsync(
				"SomethingHappened",
				e.Thing.Offset,
				thingAsByteArray
			);
		}
		#endregion

		#region private methods
		private MemoryStoryListener CreateStoryListener(string storyIdHash)
		{
			MemoryStoryListener? storyListener;
			if (!_storyToStoryListenerMap.TryGetValue(storyIdHash, out storyListener))
			{
				TextTrail internalStoryId = TextTrailSerializer.ToInternalStoryId(storyIdHash);
				storyListener = _storyManager.GetStoryListener(internalStoryId);
				_storyToStoryListenerMap.Add(storyIdHash, storyListener);
				_storyListenerToStoryIdHashMap.Add(storyListener, storyIdHash);
				storyListener.SomethingHappenedAsync += StoryListener_SomethingHappenedAsync;
				storyListener.StartListening();
			}

			return storyListener;
		}

		private void DestroyStoryListener(string storyIdHash)
		{
			if (_storyToStoryListenerMap.TryGetValue(storyIdHash, out MemoryStoryListener? storyListener))
			{
				storyListener.StopListening();
				storyListener.SomethingHappenedAsync -= StoryListener_SomethingHappenedAsync;
				_storyToStoryListenerMap.Remove(storyIdHash);
				_storyListenerToStoryIdHashMap.Remove(storyListener);
			}
		}

		private HashSet<string> GetClientsSet(string storyIdHash)
		{
			HashSet<string>? clientsMap;
			if(!_storyToClientsMap.TryGetValue(storyIdHash, out clientsMap))
			{
				clientsMap = new HashSet<string>();
				_storyToClientsMap.Add(storyIdHash, clientsMap);
			}

			return clientsMap;
		}

		private HashSet<string> GetStoriesSet(string clientId)
		{
			HashSet<string>? storiesMap;
			if(!_clientToStoriesMap.TryGetValue(clientId, out storiesMap))
			{
				storiesMap = new HashSet<string>();
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
