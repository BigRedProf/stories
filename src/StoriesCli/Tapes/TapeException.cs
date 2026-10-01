namespace BigRedProf.Stories.StoriesCli.Tapes
{
	/// <summary>
	/// Tapes and the stories they back up disagree, in a way that must stop a backup or a
	/// restore rather than be worked around.
	/// </summary>
	public sealed class TapeException : Exception
	{
		#region constructors
		public TapeException(string message)
			: base(message)
		{
		}
		#endregion
	}
}
