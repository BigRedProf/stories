using BigRedProf.Stories.Data;
using System;
using System.Threading.Tasks;

namespace BigRedProf.Stories
{
	public interface IStoryteller
	{
		#region events
		public event EventHandler GotSomethingForYou;
		#endregion

		#region properties
		public long Bookmark { get; }
		public bool HasSomethingForMe { get; }
		#endregion

		#region methods
		public Task<bool> HasSomethingForMeAsync();
		public StoryThing TellMeSomething();
        public Task<StoryThing> TellMeSomethingAsync();
        public void SetBookmark(long bookmark);
		#endregion
	}
}
