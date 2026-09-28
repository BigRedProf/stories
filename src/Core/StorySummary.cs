namespace BigRedProf.Stories
{
	/// <summary>
	/// A story the service holds, as the service knows it: by the hash of its id, and how many
	/// things are in it.
	/// </summary>
	/// <remarks>
	/// The id itself is never sent to the service -- clients send its hash -- so the hash is all
	/// a listing can give back. Travels as JSON on <c>GET v1/stories</c>; never recorded in a
	/// story.
	/// </remarks>
	public class StorySummary
	{
		#region properties
		/// <summary>The story id's hash, as <see cref="TextTrailSerializer.ToMultihashString"/> makes it.</summary>
		public string StoryIdHash { get; set; } = default!;

		/// <summary>How many things the story holds, which is also the next offset it will write.</summary>
		public long Length { get; set; }
		#endregion
	}
}
