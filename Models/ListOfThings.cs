using BigRedProf.Data;
using System.Collections.Generic;

namespace BigRedProf.Stories.Models
{
	[GeneratePackRat(StoriesSchemaId.ListOfThings)]
	public class ListOfThings
	{
		#region properties
		[PackListField(1, CoreSchema.Code, ByteAligned.Yes)]
		public IList<Code> Things = default!;
		#endregion
	}
}
