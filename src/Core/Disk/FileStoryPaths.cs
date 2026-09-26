using System;
using System.IO;
using BigRedProf.Data.Core;

namespace BigRedProf.Stories.Disk
{
	internal static class FileStoryPaths
	{
		#region functions
		public static string GetFullDirectoryPath(string directoryPath)
		{
			if (string.IsNullOrWhiteSpace(directoryPath))
				throw new ArgumentException("Value cannot be null or whitespace.", nameof(directoryPath));

			return Path.GetFullPath(directoryPath);
		}

		public static string GetStoryFilePath(string directoryPath, TextTrail storyId)
		{
			return Path.Combine(directoryPath, GetStoryFileName(storyId));
		}

		public static string GetLockFilePath(string directoryPath, TextTrail storyId)
		{
			return Path.Combine(directoryPath, GetStoryFileName(storyId) + ".lock");
		}
		#endregion

		#region private functions
		private static string GetStoryFileName(TextTrail storyId)
		{
			// The multihash string is multibase base32: filename-safe on Windows and Linux.
			return TextTrailSerializer.ToMultihashString(storyId) + ".story";
		}
		#endregion
	}
}
