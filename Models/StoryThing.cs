using BigRedProf.Data;

namespace BigRedProf.Stories.Models
{
	[GeneratePackRat(StoriesSchemaId.StoryThing)]
	public class StoryThing
	{
		#region properties
		[PackField(1, SchemaId.Int64)]
		public long Offset
		{
			get;
			set;
		}

		[PackField(2, SchemaId.Code)]
		public Code Thing
		{
			get;
			set;
		} = default!;
		#endregion
	}
}
