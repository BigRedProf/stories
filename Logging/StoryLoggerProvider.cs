using BigRedProf.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Timers;

namespace BigRedProf.Stories.Logging
{
	public class StoryLoggerProvider : ILoggerProvider
	{
		#region fields
		private readonly IPiedPiper _piedPiper;
		private readonly IDisposable? _onChangeToken;
		private StoryLoggerConfiguration _config;
		private readonly ConcurrentDictionary<string, StoryLogger> _loggers;
		private IScribe _scribe;
		private Timer _timer;
		private bool _isDisposed;
		#endregion

		#region constructors
		public StoryLoggerProvider(IPiedPiper piedPiper, ILogger<ApiClient> apiClientLogger, IOptionsMonitor<StoryLoggerConfiguration> config)
		{
			_piedPiper = piedPiper;
			_config = config.CurrentValue;
			_onChangeToken = config.OnChange(OnConfigChange);
			_loggers = new ConcurrentDictionary<string, StoryLogger>();

			Uri baseStoryUrl = new Uri(config.CurrentValue.BaseStoryUrl);
			StoryId storyId = config.CurrentValue.StoryId;
			ApiClient apiClient = new ApiClient(baseStoryUrl, piedPiper, apiClientLogger);
			_scribe = apiClient.GetScribe(storyId);

			_timer = new Timer(TimeSpan.FromMinutes(5).TotalMilliseconds);
			_timer.Elapsed += Timer_Elapsed;
			_timer.Start();

			_isDisposed = false;
		}
		#endregion

		#region methods
		public void FlushAllStoryLoggers()
		{
			if(_isDisposed)
				throw new ObjectDisposedException(nameof(StoryLoggerProvider));

			foreach(StoryLogger logger in _loggers.Values)
				logger.Flush();
		}
		#endregion

		#region ILoggerProvider methods
		public ILogger CreateLogger(string categoryName)
		{
			if (_isDisposed)
				throw new ObjectDisposedException(nameof(StoryLoggerProvider));

			return _loggers.GetOrAdd(categoryName, categoryName => new StoryLogger(_piedPiper, categoryName, _scribe));
		}

		public void Dispose()
		{
			if (_isDisposed)
				return;

			_timer.Stop();
			FlushAllStoryLoggers();

			_loggers.Clear();
			_onChangeToken?.Dispose();
		}
		#endregion

		#region private methods
		private StoryLoggerConfiguration GetConfig()
		{
			return _config;
		}
		#endregion

		#region event handlers
		private void Timer_Elapsed(object sender, ElapsedEventArgs e)
		{
			FlushAllStoryLoggers();
		}
		#endregion

		#region callbacks
		private void OnConfigChange(StoryLoggerConfiguration config)
		{
			// TODO: update each logger's scribe
		}
		#endregion
	}
}
