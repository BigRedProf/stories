using BigRedProf.Data.Core;
using System.Collections.Generic;

namespace BigRedProf.Stories.Data
{
	[GeneratePackRat(StoriesSchemaId.ListOfStoryThings)]
	public class ListOfStoryThings
	{
		#region properties
		[PackListField(1, StoriesSchemaId.StoryThing, ByteAligned.Yes)]
		public IList<StoryThing> StoryThings = default!;
		#endregion
	}
}
