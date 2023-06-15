using BigRedProf.Data;
using BigRedProf.Stories.Events;

namespace BigRedProf.Stories;

abstract public class StoryListenerBase : IStoryListener
{
    #region IStoryListener events
    public event AsyncEventHandler<SomethingHappenedEventArgs>? SomethingHappenedAsync;
    #endregion

    #region constructors
    protected StoryListenerBase(StoryId storyId)
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
    #endregion

    #region IStoryListener properties
    public long Bookmark
    {
        get;
        protected set;
    }
    #endregion

    #region IStoryListener methods
    abstract public void StartListening();
    abstract public Task StartListeningAsync();
    abstract public void StopListening();
    abstract public Task StopListeningAsync();
    #endregion

    #region protected methods
    protected async void InvokeSomethingHappenedEvent(long offset, Code thing)
    {
        if (SomethingHappenedAsync != null)
            await SomethingHappenedAsync(this, new SomethingHappenedEventArgs(offset, thing));
    }
    #endregion
}
