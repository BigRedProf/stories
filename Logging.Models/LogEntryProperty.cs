using BigRedProf.Data;
using Microsoft.Extensions.Logging;

namespace BigRedProf.Stories.Logging.Models
{
	[RegisterPackRat(StoriesLoggingSchemaId.LogEntryProperty)]
	public class LogEntryProperty
	{
		#region properties
		[PackField(1, SchemaId.TextUtf8)]
		public string Key { get; set; } = default!;

		[PackField(2, SchemaId.TextUtf8)]
		public string Value { get; set; } = default!;
		#endregion
	}
}
