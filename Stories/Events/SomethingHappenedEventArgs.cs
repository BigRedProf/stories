using BigRedProf.Data;

namespace BigRedProf.Stories.Events
{
	public class SomethingHappenedEventArgs : EventArgs
	{
		#region constructors
		public SomethingHappenedEventArgs(long offset, Code thing)
		{
			Thing = thing;
			Offset = offset;
		}
		#endregion

		#region properties
		public long Offset
		{
			get;
			private set;
		}

		public Code Thing
		{
			get;
			private set;
		}
		#endregion
	}
}
