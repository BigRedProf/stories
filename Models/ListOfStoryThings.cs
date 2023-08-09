using BigRedProf.Data;
using System.Collections.Generic;

namespace BigRedProf.Stories.Models
{
	[RegisterPackRat(StoriesSchemaId.StoryThing)]
	public class ListOfStoryThings
	{
		#region properties
		[PackListField(1, StoriesSchemaId.ListOfStoryThings, ByteAligned.Yes)]
		public IList<StoryThing> StoryThings = default!;
		#endregion
	}
}
