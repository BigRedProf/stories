using BigRedProf.Data.Core;
using System.Collections.Generic;

namespace BigRedProf.Stories.Data
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
