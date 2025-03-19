using BigRedProf.Data.Core;
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
        public void RecordSomething(params Code[] things)
        {
            Task task = RecordSomethingAsync(things);
            task.Wait();
        }

        public Task RecordSomethingAsync(params Code[] things)
        {
			if (things == null)
				throw new ArgumentNullException(nameof(things));

			lock (_writeLock)
			{
                foreach(Code thing in things)
                {
					StoryThing storyThing = new StoryThing()
					{
						Offset = _things.Count,
						Thing = thing
					};
					_things.Add(storyThing);
				}
			}

			return Task.CompletedTask;
        }
        #endregion
    }
}
