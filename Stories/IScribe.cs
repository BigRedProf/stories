using BigRedProf.Data;
using System.Threading.Tasks;

namespace BigRedProf.Stories
{
	public interface IScribe
	{
		#region methods
		public void RecordSomething(Code something);
        public Task RecordSomethingAsync(Code something);
        #endregion
    }
}
