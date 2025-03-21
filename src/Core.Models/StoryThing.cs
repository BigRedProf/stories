﻿using BigRedProf.Data.Core;

namespace BigRedProf.Stories.Models
{
	[GeneratePackRat(StoriesSchemaId.StoryThing)]
	public class StoryThing
	{
		#region properties
		[PackField(1, CoreSchema.Int64)]
		public long Offset
		{
			get;
			set;
		}

		[PackField(2, CoreSchema.Code)]
		public Code Thing
		{
			get;
			set;
		} = default!;
		#endregion
	}
}
