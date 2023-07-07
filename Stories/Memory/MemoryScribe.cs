using BigRedProf.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace BigRedProf.Stories.Memory
{
    public class MemoryScribe : IScribe
    {
        #region fields
        private readonly IList<Code> _things;
        private object _writeLock;
        #endregion

        #region constructors
        public MemoryScribe(IList<Code> things)
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
            if (something == null)
                throw new ArgumentNullException(nameof(something));

            lock (_writeLock)
            {
                _things.Add(something!);
            }
        }

        public Task RecordSomethingAsync(Code something)
        {
            RecordSomething(something);
            return Task.CompletedTask;
        }
        #endregion
    }
}
