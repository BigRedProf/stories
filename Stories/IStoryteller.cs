using BigRedProf.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BigRedProf.Stories
{
	internal interface IStoryteller
	{
		#region events
		public event EventHandler GotSomethingForYou;
		#endregion

		#region properties
		public long Bookmark { get; }
		public bool HasSomethingForMe { get; }
		#endregion

		#region methods
		public Code TellMeSomething();
		#endregion
	}
}
