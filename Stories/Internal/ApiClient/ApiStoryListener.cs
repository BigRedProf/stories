using BigRedProf.Data;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Timers;
using System.Threading.Tasks;
using BigRedProf.Stories.Models;
using System.Net.NetworkInformation;

namespace BigRedProf.Stories.Internal.ApiClient
{
	internal class ApiStoryListener : StoryListenerBase, IDisposable, IAsyncDisposable
	{
		#region fields
		private Uri _baseUri;
		private ILogger<Stories.ApiClient> _logger;
		private HubConnection _hubConnection;
		private ApiHelper _apiHelper;
		private IStoryteller _catchUpStoryteller;
		private Timer _timer;
		private TimeSpan _timerPollingFrequency;
		private DateTime _lastTimeSomethingHappened;
		private bool _isInsideTimerCallback;
		private bool _isDisposed;
		#endregion

		#region constructors
		public ApiStoryListener(
			Uri baseUri,
			StoryId storyId, 
			IPiedPiper piedPiper, 
			ILogger<Stories.ApiClient> logger,
			long bookmark, 
			long? tellLimit,
			TimeSpan timerPollingFrequency
		)
			: this(baseUri, storyId, piedPiper, logger, bookmark, tellLimit, timerPollingFrequency, null, null, false)
		{
		}

		public ApiStoryListener(
			Uri baseUri, 
			StoryId storyId, 
			IPiedPiper piedPiper, 
			ILogger<Stories.ApiClient> logger,
			long bookmark, 
			long? tellLimit,
			TimeSpan timerPollingFrequency,
			LogLevel? signalRLogLevel,
			ILoggerProvider? loggerProvider,
			bool addConsoleLogging
		)
			: base(storyId)
		{
			Debug.Assert(baseUri != null);
			Debug.Assert(storyId != null);
			Debug.Assert(piedPiper != null);
			Debug.Assert(logger != null);

			_baseUri = baseUri;
			_logger = logger;
			Bookmark = bookmark;

			_apiHelper = new ApiHelper(logger, piedPiper);

			_catchUpStoryteller = new ApiStoryteller(baseUri, StoryId, piedPiper, Bookmark, tellLimit);

			_timerPollingFrequency = timerPollingFrequency;
			_timer = new Timer(_timerPollingFrequency.TotalMilliseconds);
			_timer.Elapsed += Timer_Elapsed;
			_lastTimeSomethingHappened = DateTime.MinValue;
			_isInsideTimerCallback = false;

			_hubConnection = new HubConnectionBuilder()
					.WithUrl(new Uri(_baseUri, $"_StorylistenerHub"))
					.AddMessagePackProtocol()
					.WithAutomaticReconnect()
					.ConfigureLogging(
						logging =>
						{
							logging.SetMinimumLevel(LogLevel.Information);
							if (signalRLogLevel != null)
							{
								logging.AddFilter("Microsoft.AspNetCore.SignalR", signalRLogLevel.Value);
								logging.AddFilter("Microsoft.AspNetCore.Http.Connections", signalRLogLevel.Value);
							}
							if (addConsoleLogging)
								logging.AddConsole();

							// NOTE: Trying to AddConsole logging in BlazorWasm throws an InvalidOperationException. Use
							// @inject ILoggerProvider LoggerProvider
							// and pass it as the loggerProvider parameter here instead.
							if (loggerProvider != null)
								logging.AddProvider(loggerProvider);
						}
					)
					.Build();

			_hubConnection.Closed += HubConnection_Closed;
			_hubConnection.Reconnecting += HubConnection_Reconnecting;
			_hubConnection.Reconnected += HubConnection_Reconnected;
			_hubConnection.On<long, byte[]>("SomethingHappened", HubConnection_OnSomethingHappened);

			_isDisposed = false;
			_isInsideTimerCallback = false;
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
			_timer.Start();
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
			_timer.Stop();
		}
		#endregion

		#region IDisposable methods
		public void Dispose()
		{
			_logger.LogWarning("ApiStoryListener.Dispose called. Consider using async method instead.");

			if (!_isDisposed)
				_hubConnection.DisposeAsync().AsTask().Wait();
		}
		#endregion

		#region IAsyncDisposable methods
		public async ValueTask DisposeAsync()
		{
			_logger.LogDebug("Enter ApiStoryListener.DisposeAsync");

			if (!_isDisposed)
			{
				await _hubConnection.DisposeAsync();
				_isDisposed = true;
			}
		}
		#endregion

		#region event handlers
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

		private async Task HubConnection_OnSomethingHappened(long offset, byte[] byteArray)
		{
			_logger.LogDebug("Enter ApiStoryListener.HubConnection_OnSomethingHappened. Offset={offset}", offset);

			if (_isInsideTimerCallback)
			{
				// the catch-up storyteller is busy, so defer to it
				_logger.LogDebug("The catch-up storyteller is busy, so defer to it.");
				return; 
			}

			if (Bookmark > offset)
			{
				// the catch-up storyteller is ahead of us and must have already told us about this thing
				_logger.LogDebug("The catch-up storyteller is ahead of us and must have already told us about this thing.");
				return;
			}

			while (Bookmark < offset)
			{
				// we've fallen behind in the story and need to catch up with a Storyteller
				_logger.LogDebug(
					"We've fallen behind in the story and need to catch up with a Storyteller. Bookmark={Bookmark},Offset={Offset}", 
					Bookmark, 
					offset
				);
				_catchUpStoryteller.SetBookmark(Bookmark);
				StoryThing catchUpThing = await _catchUpStoryteller.TellMeSomethingAsync();
				_logger.LogDebug("Catch-up thing retrieved. Offset={Offset}", catchUpThing.Offset);
				_logger.LogTrace("Catch-up thing retrieved. Thing={Thing}", catchUpThing.Thing.ToString());
				await InvokeSomethingHappenedEventAsync(catchUpThing);

				++Bookmark;
			}

			StoryThing thing = _apiHelper.GetStoryThingFromByteArray(byteArray);
			_logger.LogDebug("Thing retrieved. Offset={Offset}", thing.Offset);
			_logger.LogTrace("Thing retrieved. Thing={Thing}", thing.Thing.ToString());
			await InvokeSomethingHappenedEventAsync(thing);
			
			++Bookmark;
		}


		private async void Timer_Elapsed(object sender, ElapsedEventArgs e)
		{
			_logger.LogDebug(
				"Enter ApiStoryListener.Timer_Elapsed. IsInsideTimerCallback={IsInsideTimerCallback}", 
				_isInsideTimerCallback
			);

			if (_isInsideTimerCallback)
				return;

			DateTime now = DateTime.UtcNow;
			if (now - _lastTimeSomethingHappened < _timerPollingFrequency)
				return;

			try
			{
				_logger.LogDebug("Setting IsInsideTimerCallback to true.");
				_isInsideTimerCallback = true;

				_catchUpStoryteller.SetBookmark(Bookmark);
				while (await _catchUpStoryteller.HasSomethingForMeAsync())
				{
					_logger.LogDebug("Requesting thing. Bookmark={Bookmark}.", Bookmark);
					StoryThing catchUpThing = await _catchUpStoryteller.TellMeSomethingAsync();
					_logger.LogDebug("Catch-up thing retrieved. Offset={Offset}", catchUpThing.Offset);
					_logger.LogTrace("Catch-up thing retrieved. Thing={Thing}", catchUpThing.Thing.ToString());
					await InvokeSomethingHappenedEventAsync(catchUpThing);

					++Bookmark;
				}
			}
			finally
			{
				_logger.LogDebug("Finally, setting IsInsideTimerCallback to false.");
				_isInsideTimerCallback = false;
			}
		}
		#endregion
	}
}