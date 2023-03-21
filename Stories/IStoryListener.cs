using BigRedProf.Data;

namespace BigRedProf.Stories;
public interface IStoryListener
{
	#region methods
	public void OnSomethingHappened(long offset, Code thing);
	#endregion
}
