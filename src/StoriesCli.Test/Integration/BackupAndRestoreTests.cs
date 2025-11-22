using BigRedProf.Data.Tape;
using BigRedProf.Stories.StoriesCli.Test._TestHelpers;

namespace BigRedProf.Stories.StoriesCli.Test.Integration;

[Collection("TempDir collection")]
public class BackupAndRestoreTests
{
	#region fields
	private readonly TempDirectoryFixture _fixture;
	#endregion

	#region constructors
	public BackupAndRestoreTests(TempDirectoryFixture fixture)
	{
		_fixture = fixture;
	}
	#endregion

	#region unit tests
	[Trait("Region", "BackupAndRestore integration tests")]
	[Fact]
    public void BackupAndRestore_Test1()
	{
		TapeLibrary tapeLibrary = TapeTestHelper.CreateTapeLibrary(_fixture.RootPath);

		// TODO: Record a story with a scribe. Back it up with BackupWizard. Resotre it with RestoreWizard. Verify integrity.
	}
	#endregion
}