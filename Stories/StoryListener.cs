using BigRedProf.Data;
using BigRedProf.Stories.Events;

namespace BigRedProf.Stories;
abstract public class StoryListener
{
	#region events
	public event AsyncEventHandler<SomethingHappenedEventArgs>? SomethingHappenedAsync;
	#endregion

	#region constructors
	protected StoryListener(StoryId storyId)
	{
		StoryId = storyId;
	}
	#endregion

	#region properties
	public StoryId StoryId
	{
		get;
		private set;
	}
	public long Bookmark
	{
		get;
		protected set;
	}
	#endregion

	#region methods
	abstract public void StartListening();
	abstract public Task StartListeningAsync();
	abstract public void StopListening();
	abstract public Task StopListeningAsync();
	#endregion

	#region protected methods
	protected async void InvokeSomethingHappenedEvent(long offset, Code thing)
	{
		if(SomethingHappenedAsync != null)
			await SomethingHappenedAsync(this, new SomethingHappenedEventArgs(offset, thing));
	}
	#endregion
}
