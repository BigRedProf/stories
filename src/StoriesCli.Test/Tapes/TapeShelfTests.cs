using BigRedProf.Data.Core;
using BigRedProf.Stories.StoriesCli.Tapes;
using BigRedProf.Stories.StoriesCli.Test._TestHelpers;

namespace BigRedProf.Stories.StoriesCli.Test.Tapes;

/// <summary>
/// Generations on disk, which is where production keeps them (digihouse#397).
/// </summary>
public sealed class TapeShelfTests : IDisposable
{
	#region fields
	private readonly string _root;
	private int _nextSeries;
	#endregion

	#region constructors
	public TapeShelfTests()
	{
		// A fixed name under the temp directory, emptied first, so a run never sees another
		// run's tapes and the test itself holds no randomness.
		_root = Path.Combine(Path.GetTempPath(), "stories-tape-shelf-tests", nameof(TapeShelfTests));
		if (Directory.Exists(_root))
			Directory.Delete(_root, true);
	}
	#endregion

	#region unit tests
	[Trait("Region", "TapeShelf functions")]
	[Theory]
	[InlineData(2026, 9, 27, "2026-W39")]
	[InlineData(2027, 1, 1, "2026-W53")]
	[InlineData(2027, 1, 4, "2027-W01")]
	public void GenerationFor_ShouldBeTheIsoWeek(int year, int month, int day, string expected)
	{
		Assert.Equal(expected, TapeShelf.GenerationFor(new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc)));
	}

	[Trait("Region", "TapeShelf methods")]
	[Fact]
	public async Task Disk_ShouldRestoreAcrossRunsAndReloadedManifests()
	{
		// Each run loads the manifest from disk and opens the library afresh, exactly as a
		// scheduled backup-all does.
		MemoryStorySource source = new MemoryStorySource();
		string ledger = source.Record("app/ledger", "0", "00", "10101");
		await BackupOnceAsync(source, "2026-W39");
		source.Record("app/ledger", "1100110011", "1");
		await BackupOnceAsync(source, "2026-W39");

		TapeShelf shelf = new TapeShelf(_root);
		TapeManifest manifest = shelf.LoadManifest("2026-W39");
		MemoryStorySource restored = new MemoryStorySource();
		await CreateEngine(shelf, "2026-W39").RestoreAsync(manifest, restored);

		Assert.Equal(new Code[] { "0", "00", "10101", "1100110011", "1" }, restored.ReadAll(ledger));
		Assert.Equal(2, manifest.Stories[ledger].Segments.Count);
	}

	[Trait("Region", "TapeShelf methods")]
	[Fact]
	public async Task FindLatestGeneration_ShouldBeTheNewestWeekWithAManifest()
	{
		MemoryStorySource source = new MemoryStorySource();
		source.Record("app/ledger", "0");
		await BackupOnceAsync(source, "2026-W38");
		await BackupOnceAsync(source, "2026-W39");
		Directory.CreateDirectory(Path.Combine(_root, "2026-W40"));

		Assert.Equal("2026-W39", new TapeShelf(_root).FindLatestGeneration());
	}

	[Trait("Region", "TapeShelf methods")]
	[Theory]
	[InlineData("..")]
	[InlineData(".")]
	[InlineData("../2026-W39")]
	[InlineData("2026-W39/..")]
	[InlineData("latest")]
	[InlineData("2026-W00")]
	[InlineData("2026-W54")]
	public void LoadManifest_ShouldRefuseAnythingButAnIsoWeek(string generation)
	{
		// Anything else could put a manifest outside the shelf, where the default restore never
		// looks -- or overwrite one that is not a generation's.
		TapeShelf shelf = new TapeShelf(_root);

		Assert.Throws<ArgumentException>(() => shelf.LoadManifest(generation));
		Assert.Throws<ArgumentException>(() => shelf.OpenLibrary(generation));
	}

	[Trait("Region", "TapeShelf methods")]
	[Fact]
	public void FindLatestGeneration_ShouldIgnoreDirectoriesThatAreNotGenerations()
	{
		Directory.CreateDirectory(Path.Combine(_root, "zz-not-a-week"));
		File.WriteAllText(Path.Combine(_root, "zz-not-a-week", "manifest.json"), "{}");

		Assert.Null(new TapeShelf(_root).FindLatestGeneration());
	}

	[Trait("Region", "TapeShelf methods")]
	[Fact]
	public void FindLatestGeneration_ShouldBeNullWithNoTapes()
	{
		Assert.Null(new TapeShelf(_root).FindLatestGeneration());
	}
	#endregion

	#region IDisposable methods
	public void Dispose()
	{
		if (Directory.Exists(_root))
			Directory.Delete(_root, true);
	}
	#endregion

	#region private methods
	private async Task BackupOnceAsync(MemoryStorySource source, string generation)
	{
		TapeShelf shelf = new TapeShelf(_root);
		TapeManifest manifest = shelf.LoadManifest(generation);
		await CreateEngine(shelf, generation).BackupAsync(source, manifest);
		shelf.SaveManifest(manifest);
	}

	private TapeEngine CreateEngine(TapeShelf shelf, string generation)
	{
		return new TapeEngine(
			TapeTestHelper.CreatePiedPiper(),
			shelf.OpenLibrary(generation),
			() => new Guid(++_nextSeries, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, 2 })
		);
	}
	#endregion
}
