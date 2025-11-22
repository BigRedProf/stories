using BigRedProf.Data.Tape;
using BigRedProf.Data.Tape.Libraries;

namespace BigRedProf.Stories.StoriesCli.Test._TestHelpers
{
	internal class TapeTestHelper
	{
		#region methods
		public static TapeLibrary CreateTapeLibrary(string rootPath)
		{
			Directory.CreateDirectory(rootPath);
			return new DiskLibrary(rootPath);
		}
		#endregion
	}
}
