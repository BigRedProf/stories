using BigRedProf.Data.Core;
using BigRedProf.Data.Tape;
using BigRedProf.Stories.Memory;
using BigRedProf.Stories.Models;
using BigRedProf.Stories.StoriesCli.Test._TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace BigRedProf.Stories.StoriesCli.Test.Integration;

public class BackupAndRestoreTests
{
	#region unit tests
	[Trait("Region", "BackupAndRestore integration tests")]
	[Fact]
    public void BackupAndRestore_Test1()
	{
		TapeLibrary tapeLibrary = TapeTestHelper.CreateTapeLibrary();

		// Arrange
		IList<StoryThing> things = new List<StoryThing>();
		IScribe scribe = new MemoryScribe(things);
		IStoryteller storyteller = new MemoryStoryteller(things);

		Guid seriesId = new Guid("11111111-1111-1111-1111-111111111111");
		//scribe.RecordSomething("0", "00", "10101");
		scribe.RecordSomething("0000 0001", "0000 0010", "0001 0101");

		// Act
		BackupEngine.BackupStoryToSeries(
			TapeTestHelper.CreatePiedPiper(),
			storyteller,
			tapeLibrary,
			seriesId,
			incrementalBackup: false,
			NullLogger.Instance
		);

		IList<StoryThing> restoredThings = new List<StoryThing>();
		IScribe restoredScribe = new MemoryScribe(restoredThings);
		IStoryteller restoredStoryteller = new MemoryStoryteller(restoredThings);
		BackupEngine.RestoreStoryFromSeries(
			TapeTestHelper.CreatePiedPiper(),
			tapeLibrary,
			seriesId,
			restoredScribe,
			NullLogger.Instance
		);

		// Assert
		Code code1 = restoredStoryteller.TellMeSomething().Thing;
		Assert.Equal("00000001", code1);
		Code code2 = restoredStoryteller.TellMeSomething().Thing;
		Assert.Equal("00000010", code2);
		Code code3 = restoredStoryteller.TellMeSomething().Thing;
		Assert.Equal("00010101", code3);
	}
	#endregion
}