using BigRedProf.Data;
using BigRedProf.Stories.Logging.Models;
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
			// TODO: support batching

			string message = formatter(state, exception);
			LogEntry logEntry = new LogEntry()
			{
				LogName = _name,
				EventId = eventId.Id,
				EventName = eventId.Name,
				Level = logLevel,
				Message = message
			};

			Code encodedEntry = _piedPiper.EncodeModelWithSchema(logEntry, StoriesLoggingSchemaId.LogEntry);

			// NOTE: We need to use async here to support BlazorWasm. It's OK, though not ideal,
			// to ignore the result here since we're logging.
			Scribe.RecordSomethingAsync(encodedEntry);
		}
		#endregion
	}
}
