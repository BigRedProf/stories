using BigRedProf.Stories.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BigRedProf.Stories.Memory
{
	public class MemoryStoryteller : IStoryteller
    {
        #region fields
#pragma warning disable CS0067 // The event 'MemoryStoryteller.GotSomethingForYou' is never used
        public event EventHandler? GotSomethingForYou;
#pragma warning restore CS0067 // The event 'MemoryStoryteller.GotSomethingForYou' is never used
        #endregion

        #region fields
        private readonly IList<StoryThing> _things;
        #endregion

        #region constructors
        public MemoryStoryteller(IList<StoryThing> things)
        {
            if (things == null)
                throw new ArgumentNullException(nameof(things));

            _things = things;
            Bookmark = 0;
        }
        #endregion

        #region properties
        public long Bookmark
        {
            get;
            private set;
        }

        public bool HasSomethingForMe
        {
            get
            {
                Task<bool> result = HasSomethingForMeAsync();
                return result.Result;
            }
        }
        #endregion

        #region IStoryteller methods
        public Task<bool> HasSomethingForMeAsync()
        {
            bool result = (Bookmark < _things.Count);
            return Task.FromResult(result);
		}

        public StoryThing TellMeSomething()
        {
            Task<StoryThing> task = TellMeSomethingAsync();
            return task.Result;
        }

        public Task<StoryThing> TellMeSomethingAsync()
        {
			if (Bookmark > int.MaxValue)
				throw new InvalidOperationException("MemoryStoryTeller does not support bookmarks > 2^31");

			StoryThing thing = _things[(int)Bookmark];
			++Bookmark;

			return Task.FromResult(thing);
		}

		public void SetBookmark(long bookmark)
        {
            if (Bookmark > int.MaxValue)
                throw new InvalidOperationException("MemoryStoryTeller does not support bookmarks > 2^31");

            Bookmark = bookmark;
        }
        #endregion
    }
}
