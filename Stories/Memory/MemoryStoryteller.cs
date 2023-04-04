﻿using BigRedProf.Data;

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
        private readonly IList<Code> _things;
        #endregion

        #region constructors
        public MemoryStoryteller(IList<Code> things)
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
                return Bookmark < _things.Count;
            }
        }
        #endregion

        #region IStoryteller methods
        public Task<bool> HasSomethingForMeAsync()
        {
            Task<bool> task = new Task<bool>(() => HasSomethingForMe);
            return task;
        }

        public Code TellMeSomething()
        {
            if (Bookmark > int.MaxValue)
                throw new InvalidOperationException("MemoryStoryTeller does not support bookmarks > 2^31");

            Code thing = _things[(int)Bookmark];
            ++Bookmark;

            return thing;
        }

        public Task<Code> TellMeSomethingAsync()
        {
            Task<Code> task = new Task<Code>(() => TellMeSomething());
            return task;
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
