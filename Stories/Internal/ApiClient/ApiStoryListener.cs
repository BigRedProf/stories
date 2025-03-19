using BigRedProf.Data.Core;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using BigRedProf.Stories.Models;

namespace BigRedProf.Stories.Internal.ApiClient
{
	internal class ApiStoryListener : StoryListenerBase, IAsyncDisposable
	{
		#region fields
		private ILogger<Stories.ApiClient> _logger;
		private HubConnection _hubConnection;
		private ApiHelper _apiHelper;
		private StoryThingSequencer _storyThingSequencer;
		private bool _isDisposed;
		#endregion

		#region constructors
		public ApiStoryListener(
			IPiedPiper piedPiper, 
			ILogger<Stories.ApiClient> logger,
			Action<ILoggingBuilder> signalRLoggingBuilderCallback,
			long? tellLimit,
			TimeSpan pollingFrequency,
			Uri baseUri,
			StoryId storyId,
			long bookmark
		)
			: base(storyId)
		{
			Debug.Assert(baseUri != null);
			Debug.Assert(storyId != null);
			Debug.Assert(piedPiper != null);
			Debug.Assert(logger != null);

			_logger = logger;
			Bookmark = bookmark;

			_apiHelper = new ApiHelper(logger, piedPiper);

			IStoryteller catchUpStoryteller = new ApiStoryteller(baseUri, StoryId, piedPiper, Bookmark, tellLimit);
			_storyThingSequencer = new StoryThingSequencer(logger, catchUpStoryteller, bookmark, pollingFrequency);
			_storyThingSequencer.SomethingHappenedAsync += StoryThingSequencer_SomethingHappenedAsync;

			_hubConnection = new HubConnectionBuilder()
					.WithUrl(new Uri(baseUri, $"_StorylistenerHub"))
					.AddMessagePackProtocol()
					.WithAutomaticReconnect()
					.ConfigureLogging(signalRLoggingBuilderCallback)
					.Build();

			_hubConnection.Closed += HubConnection_Closed;
			_hubConnection.Reconnecting += HubConnection_Reconnecting;
			_hubConnection.Reconnected += HubConnection_Reconnected;
			_hubConnection.On<long, byte[]>("SomethingHappened", HubConnection_OnSomethingHappened);

			_isDisposed = false;
		}
		#endregion

		#region StoryListener methods
		override public void StartListening()
		{
			if (_isDisposed)
			{
				_logger.LogWarning("ApiStoryListener.StartListening called when already disposed.");
				throw new ObjectDisposedException(nameof(ApiStoryListener));
			}

			_logger.LogWarning("ApiStoryListener.StartListening called. Consider using async method instead.");

			StartListeningAsync().Wait();
		}

		override public async Task StartListeningAsync()
		{
			if (_isDisposed)
			{
				_logger.LogWarning("ApiStoryListener.StartListeningAsync called when already disposed.");
				throw new ObjectDisposedException(nameof(ApiStoryListener));
			}

			_logger.LogDebug("Enter ApiStoryListener.StartListeningAsync");

			await _hubConnection.StartAsync();
			await _hubConnection.InvokeAsync("StartListeningToStory", StoryId.ToString());
		}

		override public void StopListening()
		{
			if (_isDisposed)
			{
				_logger.LogWarning("ApiStoryListener.StopListening called when already disposed.");
				throw new ObjectDisposedException(nameof(ApiStoryListener));
			}

			_logger.LogWarning("ApiStoryListener.StopListening called. Consider using async method instead.");

			StopListeningAsync().Wait();
		}

		override public async Task StopListeningAsync()
		{
			if (_isDisposed)
			{
				_logger.LogWarning("ApiStoryListener.StopListeningAsync called when already disposed.");
				throw new ObjectDisposedException(nameof(ApiStoryListener));
			}

			_logger.LogDebug("Enter ApiStoryListener.StopListeningAsync");

			await _hubConnection.InvokeAsync("StopListeningToStory", StoryId.ToString());
			await _hubConnection.StopAsync();
		}
		#endregion

		#region IAsyncDisposable methods
		public async ValueTask DisposeAsync()
		{
			_logger.LogDebug("Enter ApiStoryListener.DisposeAsync");

			if (!_isDisposed)
			{
				await _storyThingSequencer.DisposeAsync();
				await _hubConnection.DisposeAsync();
				_isDisposed = true;
			}
		}
		#endregion

		#region event handlers
		private async Task StoryThingSequencer_SomethingHappenedAsync(object? sender, Events.SomethingHappenedEventArgs e)
		{
			_logger.LogDebug(
				"Enter ApiStoryListener.StoryThingSequencer_SomethingHappenedAsync. Offset={Offset}", 
				e.Thing.Offset
			);

			Bookmark = e.Thing.Offset;
			try
			{
				await InvokeSomethingHappenedEventAsync(e.Thing);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(
					ex,
					"An exception was thrown by an ApiStoryListener.SomethingHappenedAsync event handler." +
					"Offset={Offset},Message={Message}",
					e.Thing.Offset,
					ex.Message
				);
			}
		}

		private async Task HubConnection_Reconnected(string? arg)
		{
			_logger.LogInformation("Enter ApiStoryListener.HubConnection_Reconnected. Arg={arg}", arg);

			await InvokeConnectionStatusChangedAsync("reconnected", arg, null);
		}

		private async Task HubConnection_Reconnecting(Exception? arg)
		{
			_logger.LogInformation("Enter ApiStoryListener.HubConnection_Reconnecting. Arg={arg}", arg);

			await InvokeConnectionStatusChangedAsync("reconnecting", null, arg);
		}

		private async Task HubConnection_Closed(Exception? arg)
		{
			_logger.LogInformation("Enter ApiStoryListener.HubConnection_Closed. Arg={arg}", arg);

			await InvokeConnectionStatusChangedAsync("closed", null, arg);
		}

		private Task HubConnection_OnSomethingHappened(long offset, byte[] byteArray)
		{
			_logger.LogDebug("Enter ApiStoryListener.HubConnection_OnSomethingHappened. Offset={offset}", offset);

			StoryThing storyThing = _apiHelper.GetStoryThingFromByteArray(byteArray);
			_storyThingSequencer.SequenceStoryThing(storyThing);
			return Task.CompletedTask;
		}
		#endregion
	}
}