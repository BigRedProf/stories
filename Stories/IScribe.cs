using BigRedProf.Data;

namespace BigRedProf.Stories
{
	internal interface IScribe
	{
		#region methods
		public void RecordSomething(Code something);
		#endregion
	}
}
