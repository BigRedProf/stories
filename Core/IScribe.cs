using BigRedProf.Data.Core;
using System.Threading.Tasks;

namespace BigRedProf.Stories
{
	public interface IScribe
	{
		#region methods
		public void RecordSomething(params Code[] things);
        public Task RecordSomethingAsync(params Code[] things);
        #endregion
    }
}
