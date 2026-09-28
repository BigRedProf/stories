using BigRedProf.Data.Core;
using BigRedProf.Data.Tape;
using BigRedProf.Data.Tape.Libraries;
using BigRedProf.Stories.StoriesCli.Tapes;
using BigRedProf.Stories.StoriesCli.Test._TestHelpers;

namespace BigRedProf.Stories.StoriesCli.Test.Tapes;

/// <summary>
/// digihouse#397: every story backed up, restored exactly, and found out when it cannot be.
/// </summary>
public class TapeEngineTests
{
	#region static fields
	private static readonly Code[] Ledger = new Code[] { "0", "00", "10101", "1100110011" };
	private static readonly Code[] Roster = new Code[] { "1", "0110" };
	#endregion

	#region fields
	private int _nextSeries;
	#endregion

	#region unit tests
	[Trait("Region", "TapeEngine methods")]
	[Fact]
	public async Task BackupAsync_ShouldRestoreEveryStoryExactly()
	{
		MemoryStorySource source = new MemoryStorySource();
		string ledger = source.Record("app/ledger", Ledger);
		string roster = source.Record("app/roster", Roster);
		TapeEngine engine = CreateEngine(new MemoryLibrary());
		TapeManifest manifest = NewManifest();

		BackupResult result = await engine.BackupAsync(source, manifest);
		MemoryStorySource restored = new MemoryStorySource();
		long thingsRestored = await engine.RestoreAsync(manifest, restored);

		Assert.Equal(2, result.StoriesGrown);
		Assert.Equal(6, result.ThingsWritten);
		Assert.Equal(6, thingsRestored);
		Assert.Equal(Ledger, restored.ReadAll(ledger));
		Assert.Equal(Roster, restored.ReadAll(roster));
	}

	[Trait("Region", "TapeEngine methods")]
	[Fact]
	public async Task BackupAsync_ShouldWriteOnlyWhatIsNewAsANewSegment()
	{
		// Frames that are not whole bytes, across two runs: the second segment has to start
		// exactly where the story left off, not where a byte boundary would.
		MemoryStorySource source = new MemoryStorySource();
		string ledger = source.Record("app/ledger", Ledger[0], Ledger[1]);
		TapeEngine engine = CreateEngine(new MemoryLibrary());
		TapeManifest manifest = NewManifest();
		await engine.BackupAsync(source, manifest);
		source.Record("app/ledger", Ledger[2], Ledger[3]);

		BackupResult second = await engine.BackupAsync(source, manifest);
		MemoryStorySource restored = new MemoryStorySource();
		await engine.RestoreAsync(manifest, restored);

		Assert.Equal(2, second.ThingsWritten);
		Assert.Equal(new long[] { 0, 2 }, manifest.Stories[ledger].Segments.Select(segment => segment.From));
		Assert.Equal(4, manifest.Stories[ledger].Length);
		Assert.Equal(Ledger, restored.ReadAll(ledger));
	}

	[Trait("Region", "TapeEngine methods")]
	[Fact]
	public async Task BackupAsync_ShouldWriteNothingForAStoryThatHasNotGrown()
	{
		MemoryStorySource source = new MemoryStorySource();
		string ledger = source.Record("app/ledger", Ledger);
		TapeEngine engine = CreateEngine(new MemoryLibrary());
		TapeManifest manifest = NewManifest();
		await engine.BackupAsync(source, manifest);

		BackupResult again = await engine.BackupAsync(source, manifest);

		Assert.Equal(0, again.StoriesGrown);
		Assert.Single(manifest.Stories[ledger].Segments);
	}

	[Trait("Region", "TapeEngine methods")]
	[Fact]
	public async Task BackupAsync_ShouldRefuseAServiceThatHasLostHistory()
	{
		// The service restarted and was not restored: what it holds now is shorter than the
		// tapes. Backing it up would describe a store that does not match them.
		MemoryStorySource source = new MemoryStorySource();
		source.Record("app/ledger", Ledger);
		TapeEngine engine = CreateEngine(new MemoryLibrary());
		TapeManifest manifest = NewManifest();
		await engine.BackupAsync(source, manifest);
		MemoryStorySource emptied = new MemoryStorySource();
		emptied.Record("app/roster", Roster);

		await Assert.ThrowsAsync<TapeException>(() => engine.BackupAsync(emptied, manifest));

		Assert.Single(manifest.Stories);
	}

	[Trait("Region", "TapeEngine methods")]
	[Fact]
	public async Task BackupAsync_ShouldRefuseAServiceThatLostHistoryJustBeforeANewWeek()
	{
		// The first backup of a week has an empty manifest to compare against. Without last
		// week's as a floor, it would write the emptied store down as the latest generation --
		// the one a restore uses.
		MemoryStorySource source = new MemoryStorySource();
		source.Record("app/ledger", Ledger);
		TapeEngine engine = CreateEngine(new MemoryLibrary());
		TapeManifest lastWeek = NewManifest();
		await engine.BackupAsync(source, lastWeek);
		MemoryStorySource emptied = new MemoryStorySource();
		emptied.Record("app/roster", Roster);
		TapeManifest thisWeek = new TapeManifest() { Generation = "2026-W40" };

		await Assert.ThrowsAsync<TapeException>(() => engine.BackupAsync(emptied, thisWeek, lastWeek));

		Assert.Empty(thisWeek.Stories);
	}

	[Trait("Region", "TapeEngine methods")]
	[Fact]
	public async Task BackupAsync_ShouldStartANewWeekWithAFullCopy()
	{
		MemoryStorySource source = new MemoryStorySource();
		string ledger = source.Record("app/ledger", Ledger);
		TapeEngine engine = CreateEngine(new MemoryLibrary());
		TapeManifest lastWeek = NewManifest();
		await engine.BackupAsync(source, lastWeek);
		TapeManifest thisWeek = new TapeManifest() { Generation = "2026-W40" };

		BackupResult result = await engine.BackupAsync(source, thisWeek, lastWeek);
		MemoryStorySource restored = new MemoryStorySource();
		await engine.RestoreAsync(thisWeek, restored);

		Assert.Equal(Ledger.Length, result.ThingsWritten);
		Assert.Equal(Ledger, restored.ReadAll(ledger));
	}

	[Trait("Region", "TapeEngine methods")]
	[Fact]
	public async Task RestoreAsync_ShouldRefuseATargetThatAlreadyHoldsStories()
	{
		MemoryStorySource source = new MemoryStorySource();
		source.Record("app/ledger", Ledger);
		TapeEngine engine = CreateEngine(new MemoryLibrary());
		TapeManifest manifest = NewManifest();
		await engine.BackupAsync(source, manifest);
		MemoryStorySource occupied = new MemoryStorySource();
		string roster = occupied.Record("app/roster", Roster);

		await Assert.ThrowsAsync<TapeException>(() => engine.RestoreAsync(manifest, occupied));

		Assert.Equal(Roster, occupied.ReadAll(roster));
	}

	[Trait("Region", "TapeEngine methods")]
	[Fact]
	public async Task VerifyAsync_ShouldFindNothingWrongWithGoodTapes()
	{
		MemoryStorySource source = new MemoryStorySource();
		source.Record("app/ledger", Ledger);
		TapeEngine engine = CreateEngine(new MemoryLibrary());
		TapeManifest manifest = NewManifest();
		await engine.BackupAsync(source, manifest);

		IReadOnlyList<string> problems = await engine.VerifyAsync(manifest, source);

		Assert.Empty(problems);
	}

	[Trait("Region", "TapeEngine methods")]
	[Fact]
	public async Task VerifyAsync_ShouldFindAGapBetweenSegments()
	{
		MemoryStorySource source = new MemoryStorySource();
		string ledger = source.Record("app/ledger", Ledger[0], Ledger[1]);
		TapeEngine engine = CreateEngine(new MemoryLibrary());
		TapeManifest manifest = NewManifest();
		await engine.BackupAsync(source, manifest);
		source.Record("app/ledger", Ledger[2], Ledger[3]);
		await engine.BackupAsync(source, manifest);
		manifest.Stories[ledger].Segments.RemoveAt(0);

		IReadOnlyList<string> problems = await engine.VerifyAsync(manifest, null);

		Assert.NotEmpty(problems);
	}

	[Trait("Region", "TapeEngine methods")]
	[Fact]
	public async Task VerifyAsync_ShouldFindASegmentThatIsMissing()
	{
		MemoryStorySource source = new MemoryStorySource();
		string ledger = source.Record("app/ledger", Ledger);
		TapeEngine engine = CreateEngine(new MemoryLibrary());
		TapeManifest manifest = NewManifest();
		await engine.BackupAsync(source, manifest);
		manifest.Stories[ledger].Segments[0].SeriesId = new Guid("99999999-0000-0000-0000-000000000009");

		IReadOnlyList<string> problems = await engine.VerifyAsync(manifest, null);

		Assert.Single(problems);
	}

	[Trait("Region", "TapeEngine methods")]
	[Fact]
	public async Task VerifyAsync_ShouldFindTapesLongerThanTheLiveStory()
	{
		MemoryStorySource source = new MemoryStorySource();
		source.Record("app/ledger", Ledger);
		TapeEngine engine = CreateEngine(new MemoryLibrary());
		TapeManifest manifest = NewManifest();
		await engine.BackupAsync(source, manifest);

		IReadOnlyList<string> problems = await engine.VerifyAsync(manifest, new MemoryStorySource());

		Assert.Single(problems);
	}
	#endregion

	#region private methods
	private TapeEngine CreateEngine(TapeLibrary library)
	{
		// Deterministic series ids: one per segment, in the order they are written.
		return new TapeEngine(
			TapeTestHelper.CreatePiedPiper(),
			library,
			() => new Guid(++_nextSeries, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 })
		);
	}

	private static TapeManifest NewManifest()
	{
		return new TapeManifest() { Generation = "2026-W39" };
	}
	#endregion
}
