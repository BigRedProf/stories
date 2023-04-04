using BigRedProf.Data;

namespace BigRedProf.Stories.Memory
{
    public class MemoryScribe : IScribe
    {
        #region fields
        private readonly IList<Code> _things;
        #endregion

        #region constructors
        public MemoryScribe(IList<Code> things)
        {
            if (things == null)
                throw new ArgumentNullException(nameof(things));

            _things = things;
        }
        #endregion

        #region IScribe methods
        public void RecordSomething(Code something)
        {
            _things.Add(something);
        }

        public Task RecordSomethingAsync(Code something)
        {
            RecordSomething(something);
            return Task.CompletedTask;
        }
        #endregion
    }
}
