using BigRedProf.Data.Core;
using BigRedProf.Stories.Models;
using System;

namespace BigRedProf.Stories.Events
{
	public class SomethingHappenedEventArgs : EventArgs
	{
		#region constructors
		public SomethingHappenedEventArgs(StoryThing thing)
		{
			Thing = thing;
		}
		#endregion

		#region properties
		public StoryThing Thing
		{
			get;
			private set;
		}
		#endregion
	}
}
