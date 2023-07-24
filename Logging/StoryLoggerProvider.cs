using BigRedProf.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;

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
		#endregion

		#region constructors
		public StoryLoggerProvider(IPiedPiper piedPiper, IOptionsMonitor<StoryLoggerConfiguration> config)
		{
			_piedPiper = piedPiper;
			_config = config.CurrentValue;
			_onChangeToken = config.OnChange(OnConfigChange);
			_loggers = new ConcurrentDictionary<string, StoryLogger>();

			Uri baseStoryUrl = new Uri(config.CurrentValue.BaseStoryUrl);
			StoryId storyId = config.CurrentValue.StoryId;
			ApiClient apiClient = new ApiClient(baseStoryUrl, piedPiper);
			_scribe = apiClient.GetScribe(storyId);
		}
		#endregion

		#region ILoggerProvider methods
		public ILogger CreateLogger(string categoryName)
		{
			return _loggers.GetOrAdd(categoryName, categoryName => new StoryLogger(_piedPiper, categoryName, _scribe));
		}

		public void Dispose()
		{
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

		#region callbacks
		private void OnConfigChange(StoryLoggerConfiguration config)
		{
			// TODO: update each logger's scribe
		}
		#endregion
	}
}
