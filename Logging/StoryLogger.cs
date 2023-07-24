using BigRedProf.Data;
using Microsoft.Extensions.Logging;
using System;

namespace BigRedProf.Stories.Logging
{
	public class StoryLogger : ILogger
	{
		#region fields
		private readonly IPiedPiper _piedPiper;
		private readonly string _name;
		#endregion

		#region constructors
		public StoryLogger(IPiedPiper piedPiper, string name, IScribe scribe)
		{
			_piedPiper = piedPiper;
			_name = name;
			Scribe = scribe;
		}
		#endregion

		#region properties
		public IScribe Scribe
		{
			get;
			set;
		}
		#endregion

		#region ILogger methods
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull
		{
			// TODO: implement this
			return null;
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			// TODO: implement this correctly
			return true;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			// TODO: consider the other arguments provided (logLevel, eventId)
			// TODO: support batching

			string message = formatter(state, exception);
			string entry = $"{_name} :{eventId}: {logLevel}: message";

			Code encodedEntry = _piedPiper.EncodeModel<string>(entry, SchemaId.TextUtf8);

			// NOTE: We need to use async here to support BlazorWasm. It's OK, though not ideal,
			// to ignore the result here since we're logging.
			Scribe.RecordSomethingAsync(encodedEntry);
		}
		#endregion
	}
}
