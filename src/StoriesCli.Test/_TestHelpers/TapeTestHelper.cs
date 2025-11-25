using BigRedProf.Data.Core;
using BigRedProf.Data.Tape;
using BigRedProf.Data.Tape.Libraries;
using BigRedProf.Stories.Models;

namespace BigRedProf.Stories.StoriesCli.Test._TestHelpers
{
	internal static class TapeTestHelper
	{
		#region methods
		public static IPiedPiper CreatePiedPiper()
		{
			IPiedPiper piedPiper = new PiedPiper();
			piedPiper.RegisterCorePackRats();
			piedPiper.RegisterPackRats(typeof(StoryThing).Assembly);
			return piedPiper;
		}

		public static TapeLibrary CreateTapeLibrary()
		{
			return new MemoryLibrary();
		}
		#endregion
	}
}
