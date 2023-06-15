using BigRedProf.Stories.Events;

namespace BigRedProf.Stories;
public interface IStoryListener
{
	#region events
	public event AsyncEventHandler<SomethingHappenedEventArgs>? SomethingHappenedAsync;
	#endregion

	#region properties
	public long Bookmark { get; }
	#endregion

	#region methods
	public void StartListening();
	public Task StartListeningAsync();
	public void StopListening();
	public Task StopListeningAsync();
	#endregion
}
