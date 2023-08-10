using BigRedProf.Data;
using BigRedProf.Stories.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BigRedProf.Stories.Memory
{
	public class MemoryScribe : IScribe
    {
        #region fields
        private readonly IList<StoryThing> _things;
        private object _writeLock;
        #endregion

        #region constructors
        public MemoryScribe(IList<StoryThing> things)
        {
            if (things == null)
                throw new ArgumentNullException(nameof(things));

            _things = things;

            _writeLock = new object();
        }
        #endregion

        #region IScribe methods
        public void RecordSomething(Code something)
        {
            RecordSomethingAsync(something);
        }

        public Task RecordSomethingAsync(Code something)
        {
			if (something == null)
				throw new ArgumentNullException(nameof(something));

			lock (_writeLock)
			{
				StoryThing storyThing = new StoryThing()
				{
					Offset = _things.Count,
					Thing = something
				};
				_things.Add(storyThing);
			}

			return Task.CompletedTask;
        }
        #endregion
    }
}
