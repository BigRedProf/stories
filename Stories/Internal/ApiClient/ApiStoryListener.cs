using BigRedProf.Data;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
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
		private bool _isDisposed;
		#endregion

		#region constructors
		public ApiStoryListener(Uri baseUri, StoryId storyId, IPiedPiper piedPiper, long bookmark)
			: this(baseUri, storyId, piedPiper, bookmark, LogLevel.Information, false)
		{
		}

		public ApiStoryListener(
			Uri baseUri, 
			StoryId storyId, 
			IPiedPiper piedPiper, 
			long bookmark, 
			LogLevel signalRLogLevel, 
			bool addConsoleLogging)
			: base(storyId)
		{
			Debug.Assert(baseUri != null);
			Debug.Assert(storyId != null);
			Debug.Assert(piedPiper != null);

			_baseUri = baseUri;
			_piedPiper = piedPiper;
			Bookmark = bookmark;

			_catchUpStoryteller = new ApiStoryteller(baseUri, StoryId, piedPiper, Bookmark);

			_hubConnection = new HubConnectionBuilder()
					.WithUrl(new Uri(_baseUri, $"_StorylistenerHub"))
					.AddMessagePackProtocol()
					.ConfigureLogging(
						logging =>
						{
							// NOTE: Trying to AddConsole logging in BlazorWasm throws an InvalidOperationException
							if(addConsoleLogging)
								logging.AddConsole();
							logging.SetMinimumLevel(LogLevel.Information);
							logging.AddFilter("Microsoft.AspNetCore.SignalR", signalRLogLevel);
							logging.AddFilter("Microsoft.AspNetCore.Http.Connections", signalRLogLevel);
						}
					)
					.Build();

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
		private void HubConnection_OnSomethingHappened(long offset, byte[] byteArray)
		{
			if (Bookmark > offset)
				return; // issue warning?? not sure we should ever be ahead of these events

			while (Bookmark < offset)
			{
				// we've fallen behind in the story and need to catch up with a Storyteller
				_catchUpStoryteller.SetBookmark(Bookmark);
				Code catchUpCode = _catchUpStoryteller.TellMeSomething();
				InvokeSomethingHappenedEvent(Bookmark, catchUpCode);

				++Bookmark;
			}

			Code code = GetCodeFromByteArray(byteArray);
			InvokeSomethingHappenedEvent(Bookmark, code);
			
			++Bookmark;
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