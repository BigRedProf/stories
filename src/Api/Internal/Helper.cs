namespace BigRedProf.Stories.Api.Internal
{
	internal static class Helper
	{
		#region methods
		public static string HackHackFixStoryId(string storyId)
		{
			// HACKHACK: There's a frustrating bug in ASP.NET that doesn't decode slashes (/) in
			// routes properly.
			// https://github.com/dotnet/aspnetcore/issues/11544

			// We can fix this for story identifiers because percent (%) happens to be an illegal 
			// character. But there's no general fix since "%2f" is a perfectly legal string in general.

			return storyId
				.Replace("%2f", "/")
				.Replace("%2F", "/")
			;
		}
		#endregion
	}
}
