using BigRedProf.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
		public Code TellMeSomething();
        public Task<Code> TellMeSomethingAsync();
        public void SetBookmark(long bookmark);
		#endregion
	}
}
