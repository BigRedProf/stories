using BigRedProf.Data.Core;

namespace BigRedProf.Stories.PowerShell
{
	/// <summary>
	/// One thing from a story, as a pipeline object.
	/// </summary>
	/// <remarks>
	/// The offset travels with the thing deliberately. It is the bookmark the story
	/// replays from, so it is the coordinate you quote when reporting what you saw --
	/// and losing it is what makes text output hard to act on.
	/// </remarks>
	public sealed class StoryThingInfo
	{
		#region properties
		/// <summary>
		/// The thing's position in the story.
		/// </summary>
		public long Offset { get; set; }

		/// <summary>
		/// The raw code, always present. Stories stores codes and has no opinion about
		/// what they mean, so this is the only thing guaranteed to be readable.
		/// </summary>
		public Code Thing { get; set; } = default!;

		/// <summary>
		/// The decoded model, when a schema was named and its pack rat was registered.
		/// Null otherwise -- including for a thing whose schema this host does not know,
		/// which is normal when reading a story written by a newer build.
		/// </summary>
		public object? Model { get; set; }
		#endregion

		#region object methods
		public override string ToString()
		{
			return $"{Offset}: {Model?.ToString() ?? Thing.ToString()}";
		}
		#endregion
	}
}
