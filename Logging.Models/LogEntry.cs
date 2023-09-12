using BigRedProf.Data;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace BigRedProf.Stories.Logging.Models
{
    [RegisterPackRat(StoriesLoggingSchemaId.LogEntry)]
	public class LogEntry
	{
		#region properties
		[PackField(1, SchemaId.TextUtf8)]
		public string LogName { get; set; } = default!;

		[PackField(2, SchemaId.Int32)]
		public int EventId { get; set; }

		[PackField(3, SchemaId.TextUtf8, IsNullable = true)]
		public string? EventName { get; set; }

		[PackField(4, SchemaId.Int32)]
		public LogLevel Level { get; set; }

		[PackField(5, SchemaId.TextUtf8)]
		public string Message { get; set; } = default!;

		[PackListField(6, StoriesLoggingSchemaId.LogEntryProperty, ByteAligned.Yes)]
		public IList<LogEntryProperty> Properties = default!;
		#endregion
	}
}
