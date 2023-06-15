using BigRedProf.Data;
using BigRedProf.Stories.Internal;
using BigRedProf.Stories.Internal.ApiClient;

namespace BigRedProf.Stories.StoriesCli
{
    public class ListenCommand : Command
	{
		#region fields
		private IStoryListener? _storyListener;
		#endregion

		#region Command methods
		public override int Run(CommandLineOptions options)
		{
			IPiedPiper piedPiper = new PiedPiper();
			piedPiper.RegisterDefaultPackRats();

			long bookmark = options.Bookmark == null ? 0 : options.Bookmark.Value;

			ApiClient apiClient = new ApiClient(options.BaseUri!, piedPiper);
			_storyListener = apiClient.GetStoryListener(options.Story!, bookmark);
			_storyListener.SomethingHappenedAsync += StoryListener_SomethingHappenedAsync;
			_storyListener.StartListening();

			while (true)
				Thread.Sleep(TimeSpan.FromSeconds(3));
		}

		protected override void OnCancelKeyPress()
		{
			if (_storyListener == null)
				return;

			_storyListener.SomethingHappenedAsync -= StoryListener_SomethingHappenedAsync;
			_storyListener.StopListening();
		}
		#endregion

		#region event handlers
		private Task StoryListener_SomethingHappenedAsync(object? sender, Events.SomethingHappenedEventArgs e)
		{
			Console.WriteLine($"{e.Offset}: {e.Thing}");
			return Task.CompletedTask;
		}
		#endregion
	}
}
