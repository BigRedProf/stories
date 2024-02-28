using BigRedProf.Data;
using System.Collections.Generic;

namespace BigRedProf.Stories.Models
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
