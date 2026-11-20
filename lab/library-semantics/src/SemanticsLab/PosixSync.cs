using System.Runtime.InteropServices;

namespace BigRedProf.Stories.LibrarySemanticsLab
{
	/// <summary>
	/// fsync of a directory, which is what makes a rename durable across power loss
	/// on Linux. A process kill does not need it: the kernel already has the dentry.
	/// </summary>
	internal static class PosixSync
	{
		#region constants
		private const int ReadOnly = 0;
		private const int Directory = 65536;
		#endregion

		#region functions
		public static void FsyncDirectory(string directory)
		{
			if (string.IsNullOrWhiteSpace(directory))
				throw new ArgumentException("A directory is required.", nameof(directory));

			int handle = Open(directory, ReadOnly | Directory);
			if (handle < 0)
			{
				throw new IOException(
					"Could not open '" + directory + "' to fsync it. errno " + Marshal.GetLastWin32Error().ToString() + "."
				);
			}

			try
			{
				if (Fsync(handle) != 0)
				{
					throw new IOException(
						"fsync failed for '" + directory + "'. errno " + Marshal.GetLastWin32Error().ToString() + "."
					);
				}
			}
			finally
			{
				Close(handle);
			}
		}
		#endregion

		#region private functions
		[DllImport("libc", EntryPoint = "open", SetLastError = true)]
		private static extern int Open(string path, int flags);

		[DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
		private static extern int Fsync(int handle);

		[DllImport("libc", EntryPoint = "close", SetLastError = true)]
		private static extern int Close(int handle);
		#endregion
	}
}
