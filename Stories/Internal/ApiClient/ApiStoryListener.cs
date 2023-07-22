using BigRedProf.Data;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Timers;
using System.Threading.Tasks;
using System.Web;

namespace BigRedProf.Stories.Internal.ApiClient
{
    internal class ApiStoryListener : StoryListenerBase, IDisposable, IAsyncDisposable
	{
		#region fields
		private Uri _baseUri;
		private IPiedPiper _piedPiper;
		private HubConnection _hubConnection;
		private IStoryteller _catchUpStoryteller;
		private Timer _timer;
		private TimeSpan _timerPollingFrequency;
		private DateTime _lastTimeSomethingHappened;
		private bool _isInsideTimerCallback;
		private bool _isDisposed;
		#endregion

		#region constructors
		public ApiStoryListener(Uri baseUri, StoryId storyId, IPiedPiper piedPiper, long bookmark, TimeSpan timerPollingFrequency)
			: this(baseUri, storyId, piedPiper, bookmark, timerPollingFrequency, null, null, false)
		{
		}

		public ApiStoryListener(
			Uri baseUri, 
			StoryId storyId, 
			IPiedPiper piedPiper, 
			long bookmark, 
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

			_baseUri = baseUri;
			_piedPiper = piedPiper;
			Bookmark = bookmark;

			_catchUpStoryteller = new ApiStoryteller(baseUri, StoryId, piedPiper, Bookmark);

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
		}
		#endregion

		#region StoryListener methods
		override public void StartListening()
		{
			if (_hubConnection == null)
				throw new ObjectDisposedException(nameof(ApiStoryListener));

			StartListeningAsync().Wait();
		}

		override public async Task StartListeningAsync()
		{
			if (_hubConnection == null)
				throw new ObjectDisposedException(nameof(ApiStoryListener));

			await _hubConnection.StartAsync();
			await _hubConnection.InvokeAsync("StartListeningToStory", StoryId.ToString());
			_timer.Start();
		}

		override public void StopListening()
		{
			if (_hubConnection == null)
				throw new ObjectDisposedException(nameof(ApiStoryListener));

			StopListeningAsync().Wait();
		}

		override public async Task StopListeningAsync()
		{
			if (_isDisposed)
				throw new ObjectDisposedException(nameof(ApiStoryListener));

			await _hubConnection.InvokeAsync("StopListeningToStory", StoryId.ToString());
			await _hubConnection.StopAsync();
			_timer.Stop();
		}
		#endregion

		#region IDisposable methods
		public void Dispose()
		{
			if (!_isDisposed)
			{
				_hubConnection.DisposeAsync().AsTask().Wait();
				_isDisposed = true;
			}
		}
		#endregion

		#region IAsyncDisposable methods
		public async ValueTask DisposeAsync()
		{
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
			await InvokeConnectionStatusChangedAsync("reconnected", arg, null);
		}

		private async Task HubConnection_Reconnecting(Exception? arg)
		{
			await InvokeConnectionStatusChangedAsync("reconnecting", null, arg);
		}

		private async Task HubConnection_Closed(Exception? arg)
		{
			await InvokeConnectionStatusChangedAsync("closed", null, arg);
		}

		private async Task HubConnection_OnSomethingHappened(long offset, byte[] byteArray)
		{
			if (_isInsideTimerCallback)
				return;	// the catch-up storyteller is busy, so defer to it

			if (Bookmark > offset)
				return; // issue warning?? not sure we should ever be ahead of these events

			while (Bookmark < offset)
			{
				// we've fallen behind in the story and need to catch up with a Storyteller
				_catchUpStoryteller.SetBookmark(Bookmark);
				Code catchUpCode = await _catchUpStoryteller.TellMeSomethingAsync();
				await InvokeSomethingHappenedEventAsync(Bookmark, catchUpCode);

				++Bookmark;
			}

			Code code = GetCodeFromByteArray(byteArray);
			await InvokeSomethingHappenedEventAsync(Bookmark, code);
			
			++Bookmark;
		}


		private async void Timer_Elapsed(object sender, ElapsedEventArgs e)
		{
			if (_isInsideTimerCallback)
				return;

			DateTime now = DateTime.UtcNow;
			if (now - _lastTimeSomethingHappened < _timerPollingFrequency)
				return;

			try
			{
				_isInsideTimerCallback = true;

				_catchUpStoryteller.SetBookmark(Bookmark);
				while (await _catchUpStoryteller.HasSomethingForMeAsync())
				{
					Code catchUpCode = await _catchUpStoryteller.TellMeSomethingAsync();
					await InvokeSomethingHappenedEventAsync(Bookmark, catchUpCode);

					++Bookmark;
				}
			}
			finally
			{
				_isInsideTimerCallback = false;
			}
		}
		#endregion

		#region private methods
		private Code GetCodeFromByteArray(byte[] byteArray)
		{
			Code code;
			PackRat<Code> packRat = _piedPiper.GetPackRat<Code>(SchemaId.Code);
			MemoryStream memoryStream = new MemoryStream(byteArray);
			using (CodeReader reader = new CodeReader(memoryStream))
			{
				code = packRat.UnpackModel(reader);
			}

			return code;
		}
		#endregion
	}
}