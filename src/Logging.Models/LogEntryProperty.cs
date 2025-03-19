using BigRedProf.Data.Core;
using Microsoft.Extensions.Logging;

namespace BigRedProf.Stories.Logging.Models
{
	[GeneratePackRat(StoriesLoggingSchemaId.LogEntryProperty)]
	public class LogEntryProperty
	{
		#region properties
		[PackField(1, CoreSchema.TextUtf8)]
		public string Name { get; set; } = default!;

		[PackField(2, CoreSchema.TextUtf8)]
		public string Value { get; set; } = default!;
		#endregion
	}
}
