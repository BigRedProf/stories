using BigRedProf.Data;
using System.Collections.Generic;

namespace BigRedProf.Stories.Models
{
	[RegisterPackRat(StoriesSchemaId.StoryThing)]
	public class ListOfThings
	{
		#region properties
		[PackListField(1, SchemaId.Code, ByteAligned.Yes)]
		public IList<Code> Things = default!;
		#endregion
	}
}
