namespace BigRedProf.Stories.StoriesCli.Tapes
{
	/// <summary>What one backup run put on tape.</summary>
	public sealed class BackupResult
	{
		#region constructors
		public BackupResult(int storiesGrown, long thingsWritten)
		{
			StoriesGrown = storiesGrown;
			ThingsWritten = thingsWritten;
		}
		#endregion

		#region properties
		/// <summary>How many stories had something new, and so got a new segment.</summary>
		public int StoriesGrown { get; }

		/// <summary>How many things were written, over every story.</summary>
		public long ThingsWritten { get; }
		#endregion
	}
}
