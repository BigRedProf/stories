using BigRedProf.Data;
using BigRedProf.Stories.Logging.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BigRedProf.Stories.Logging
{
	public class StoryLogger : ILogger
	{
		#region fields
		private readonly IPiedPiper _piedPiper;
		private readonly string _name;
		private readonly IList<Code> _encodedLogEntries;
		#endregion

		#region constructors
		public StoryLogger(IPiedPiper piedPiper, string name, IScribe scribe)
		{
			_piedPiper = piedPiper;
			_name = name;
			Scribe = scribe;
			_encodedLogEntries = new List<Code>();
		}
		#endregion

		#region properties
		public IScribe Scribe
		{
			get;
			set;
		}
		#endregion

		#region methods
		public void Flush()
		{
			if (_encodedLogEntries.Count == 0)
				return;

			// NOTE: We need to use async here to support BlazorWasm. It's OK, though not ideal,
			// to ignore the result here since we're logging.
			Scribe.RecordSomethingAsync(_encodedLogEntries.ToArray());
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
			_encodedLogEntries.Add(encodedEntry);

			// let's autoflush errors and criticals
			if (logLevel >= LogLevel.Error)
				Flush();

			// let's also autoflush after like a thousand entries
			if (_encodedLogEntries.Count >= 1024)
				Flush();

			// otherwise, we'll wait for StoryLoggerProvider to flush us periodically or when it's disposed
		}
		#endregion
	}
}
