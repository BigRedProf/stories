namespace BigRedProf.Stories.StoriesCli.Tapes
{
	/// <summary>
	/// What one generation of tapes holds: for every story, how long it was when last backed up,
	/// and the segments that make it up, in order.
	/// </summary>
	/// <remarks>
	/// <b>The manifest is the commit point.</b> Segments are written first and are never changed
	/// afterwards; the manifest names them only once a whole backup run has succeeded, and is
	/// replaced atomically. So a run that dies partway leaves segments nothing names, which are
	/// harmless, and the next run simply writes that range again. Nothing ever has to be
	/// truncated or repaired.
	/// </remarks>
	public sealed class TapeManifest
	{
		#region properties
		/// <summary>Which generation this is, for example <c>2026-W39</c>.</summary>
		public string Generation { get; set; } = default!;

		/// <summary>When a backup run last committed this manifest.</summary>
		public DateTime UpdatedUtc { get; set; }

		/// <summary>Every story backed up in this generation, by story ID hash.</summary>
		public SortedDictionary<string, StoryTapes> Stories { get; set; } =
			new SortedDictionary<string, StoryTapes>(StringComparer.Ordinal);
		#endregion

		#region methods
		/// <summary>How much of <paramref name="storyIdHash"/> is on tape; zero for a story that is not.</summary>
		public long GetLength(string storyIdHash)
		{
			long length = 0;
			if (Stories.TryGetValue(storyIdHash, out StoryTapes? story))
				length = story.Length;

			return length;
		}
		#endregion
	}

	/// <summary>One story's tapes within a generation.</summary>
	public sealed class StoryTapes
	{
		#region properties
		/// <summary>How many of the story's things are on tape: always the last segment's <c>To</c>.</summary>
		public long Length { get; set; }

		/// <summary>The segments, contiguous and in order, from offset zero.</summary>
		public List<TapeSegment> Segments { get; set; } = new List<TapeSegment>();
		#endregion
	}

	/// <summary>
	/// One tape series holding the story's things from offset <see cref="From"/> up to, not
	/// including, <see cref="To"/>.
	/// </summary>
	public sealed class TapeSegment
	{
		#region properties
		public Guid SeriesId { get; set; }

		public long From { get; set; }

		public long To { get; set; }
		#endregion
	}
}
