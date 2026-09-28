using BigRedProf.Data.Core;
using BigRedProf.Data.Tape;
using BigRedProf.Stories.Data;

namespace BigRedProf.Stories.StoriesCli.Tapes
{
	/// <summary>
	/// Backs every story up to tape, restores it, and checks the tapes can be restored.
	/// </summary>
	/// <remarks>
	/// digihouse#397. Each backup appends a new <b>segment</b> per story that grew -- a tape
	/// series holding just the things since the last one -- and then commits a
	/// <see cref="TapeManifest"/> naming it. Segments are never written to again, so there is no
	/// partial state to resume: see the manifest's remarks.
	///
	/// A frame is the thing packed as a <see cref="CoreSchema.Code"/>, so it carries its own
	/// length. A segment's checkpoint is how many frames it holds, and a reader reads exactly
	/// that many: nothing ever has to guess where a series ends.
	/// </remarks>
	public sealed class TapeEngine
	{
		#region constants
		private const long TellLimit = 50_000;
		private const int RestoreBatchSize = 500;
		#endregion

		#region fields
		private readonly IPiedPiper _piedPiper;
		private readonly TapeLibrary _library;
		private readonly Func<Guid> _newSeriesId;
		private readonly PackRat<Code> _codePackRat;
		#endregion

		#region constructors
		public TapeEngine(IPiedPiper piedPiper, TapeLibrary library, Func<Guid> newSeriesId)
		{
			ArgumentNullException.ThrowIfNull(piedPiper);
			ArgumentNullException.ThrowIfNull(library);
			ArgumentNullException.ThrowIfNull(newSeriesId);

			_piedPiper = piedPiper;
			_library = library;
			_newSeriesId = newSeriesId;
			_codePackRat = piedPiper.GetPackRat<Code>(CoreSchema.Code);
		}
		#endregion

		#region methods
		/// <summary>
		/// Puts on tape everything <paramref name="source"/> holds that <paramref name="manifest"/>
		/// does not, and records it in the manifest -- in memory; the caller commits it.
		/// </summary>
		/// <param name="floor">
		/// The previous generation's manifest, when <paramref name="manifest"/> is a new one. A
		/// new generation starts empty, so on its own it would accept a store that has just lost
		/// its history and write that down as the latest -- and a restore then brings back
		/// nothing. The floor is checked exactly as the manifest is.
		/// </param>
		/// <returns>How many stories grew, and by how many things in all.</returns>
		/// <exception cref="TapeException">
		/// A story holds less than is already on tape. The service has lost history -- restarted
		/// without being restored, most likely -- and backing it up now would describe a store
		/// that no longer matches its tapes. Nothing is recorded in the manifest.
		/// </exception>
		public async Task<BackupResult> BackupAsync(IStorySource source, TapeManifest manifest, TapeManifest? floor = null)
		{
			ArgumentNullException.ThrowIfNull(source);
			ArgumentNullException.ThrowIfNull(manifest);

			IReadOnlyList<StorySummary> stories = await source.ListStoriesAsync();
			Dictionary<string, long> liveLengths = stories.ToDictionary(
				story => story.StoryIdHash, story => story.Length, StringComparer.Ordinal);

			// Checked for every story before anything is written, so a store that has lost
			// history is refused whole rather than half backed up.
			IEnumerable<KeyValuePair<string, StoryTapes>> taped = floor == null
				? manifest.Stories
				: manifest.Stories.Concat(floor.Stories);
			foreach (KeyValuePair<string, StoryTapes> story in taped)
			{
				liveLengths.TryGetValue(story.Key, out long liveLength);
				if (liveLength < story.Value.Length)
				{
					throw new TapeException(
						$"Story {story.Key} holds {liveLength} things but {story.Value.Length} are on tape. " +
						"The service has lost history: restore it before backing up again.");
				}
			}

			int storiesGrown = 0;
			long thingsWritten = 0;
			foreach (StorySummary story in stories)
			{
				long from = manifest.GetLength(story.StoryIdHash);
				if (story.Length <= from)
					continue;

				TapeSegment segment = WriteSegment(source, story.StoryIdHash, from, story.Length);
				if (!manifest.Stories.TryGetValue(story.StoryIdHash, out StoryTapes? storyTapes))
				{
					storyTapes = new StoryTapes();
					manifest.Stories[story.StoryIdHash] = storyTapes;
				}

				storyTapes.Segments.Add(segment);
				storyTapes.Length = segment.To;

				++storiesGrown;
				thingsWritten += segment.To - segment.From;
			}

			return new BackupResult(storiesGrown, thingsWritten);
		}

		/// <summary>
		/// Replays every story in <paramref name="manifest"/> into <paramref name="target"/>, which
		/// must hold nothing at all.
		/// </summary>
		/// <remarks>
		/// Refusing a target that already holds stories is the point, not a formality: restoring
		/// underneath history written since would interleave two pasts into one story, and
		/// offsets are forever.
		/// </remarks>
		public async Task<long> RestoreAsync(TapeManifest manifest, IStorySource target)
		{
			ArgumentNullException.ThrowIfNull(manifest);
			ArgumentNullException.ThrowIfNull(target);

			IReadOnlyList<StorySummary> existing = await target.ListStoriesAsync();
			if (existing.Count > 0)
			{
				throw new TapeException(
					$"The target already holds {existing.Count} stories. Restore only into an empty service.");
			}

			long thingsRestored = 0;
			foreach (KeyValuePair<string, StoryTapes> story in manifest.Stories)
			{
				List<Code> batch = new List<Code>(RestoreBatchSize);
				foreach (TapeSegment segment in story.Value.Segments)
				{
					foreach (Code thing in ReadSegment(segment))
					{
						batch.Add(thing);
						if (batch.Count == RestoreBatchSize)
						{
							await target.RecordAsync(story.Key, batch.ToArray());
							thingsRestored += batch.Count;
							batch.Clear();
						}
					}
				}

				if (batch.Count > 0)
				{
					await target.RecordAsync(story.Key, batch.ToArray());
					thingsRestored += batch.Count;
				}
			}

			// Read back rather than trusted: a restore that is short is found here, not the day
			// somebody walks into an empty room.
			IReadOnlyList<StorySummary> restored = await target.ListStoriesAsync();
			Dictionary<string, long> restoredLengths = restored.ToDictionary(
				story => story.StoryIdHash, story => story.Length, StringComparer.Ordinal);
			foreach (KeyValuePair<string, StoryTapes> story in manifest.Stories)
			{
				restoredLengths.TryGetValue(story.Key, out long length);
				if (length != story.Value.Length)
				{
					throw new TapeException(
						$"Story {story.Key} was restored with {length} things; its tapes hold {story.Value.Length}.");
				}
			}

			return thingsRestored;
		}

		/// <summary>
		/// Checks every segment in <paramref name="manifest"/> decodes, holds exactly what the
		/// manifest says, and joins the next with no gap -- and, given a live source, that no
		/// story on tape is longer than the story itself.
		/// </summary>
		/// <returns>Every problem found; none means the tapes restore.</returns>
		public async Task<IReadOnlyList<string>> VerifyAsync(TapeManifest manifest, IStorySource? live)
		{
			ArgumentNullException.ThrowIfNull(manifest);

			List<string> problems = new List<string>();
			foreach (KeyValuePair<string, StoryTapes> story in manifest.Stories)
			{
				long expectedFrom = 0;
				foreach (TapeSegment segment in story.Value.Segments)
				{
					if (segment.From != expectedFrom || segment.To <= segment.From)
					{
						problems.Add(
							$"Story {story.Key}: segment {segment.SeriesId} covers [{segment.From},{segment.To}) " +
							$"but should start at {expectedFrom}.");
					}

					try
					{
						long frames = ReadSegment(segment).LongCount();
						if (frames != segment.To - segment.From)
							problems.Add($"Story {story.Key}: segment {segment.SeriesId} holds {frames} things, not {segment.To - segment.From}.");
					}
					catch (Exception ex) when (ex is not OutOfMemoryException)
					{
						problems.Add($"Story {story.Key}: segment {segment.SeriesId} cannot be read: {ex.Message}");
					}

					expectedFrom = segment.To;
				}

				if (expectedFrom != story.Value.Length)
					problems.Add($"Story {story.Key}: its segments end at {expectedFrom} but it is {story.Value.Length} long.");
			}

			if (live != null)
			{
				IReadOnlyList<StorySummary> stories = await live.ListStoriesAsync();
				Dictionary<string, long> liveLengths = stories.ToDictionary(
					story => story.StoryIdHash, story => story.Length, StringComparer.Ordinal);
				foreach (KeyValuePair<string, StoryTapes> story in manifest.Stories)
				{
					liveLengths.TryGetValue(story.Key, out long liveLength);
					if (liveLength < story.Value.Length)
						problems.Add($"Story {story.Key}: {story.Value.Length} things on tape but only {liveLength} in the service.");
				}
			}

			return problems;
		}
		#endregion

		#region private methods
		private TapeSegment WriteSegment(IStorySource source, string storyIdHash, long from, long to)
		{
			Guid seriesId = _newSeriesId();
			BackupWizard wizard = BackupWizard.CreateNew(
				_library, seriesId, "story " + storyIdHash, $"Things [{from},{to}) of story {storyIdHash}.");

			// Exactly the things the listing promised, however many arrive while this runs: the
			// rest belong to the next backup, and a segment is only ever what it says it is.
			IStoryteller storyteller = source.GetStoryteller(storyIdHash, from, TellLimit);
			long expected = from;
			while (expected < to)
			{
				if (!storyteller.HasSomethingForMe)
				{
					throw new TapeException(
						$"Story {storyIdHash} was listed as {to} long but told only {expected} things.");
				}

				StoryThing storyThing = storyteller.TellMeSomething();
				if (storyThing.Offset != expected)
				{
					throw new TapeException(
						$"Story {storyIdHash} told offset {storyThing.Offset} where {expected} was expected.");
				}

				wizard.Append(_piedPiper.PackModel(storyThing.Thing, CoreSchema.Code));
				++expected;
			}

			wizard.SetLatestCheckpoint(_piedPiper.PackModel(to - from, CoreSchema.Int64));

			return new TapeSegment() { SeriesId = seriesId, From = from, To = to };
		}

		private IEnumerable<Code> ReadSegment(TapeSegment segment)
		{
			long count = ReadFrameCount(segment.SeriesId);
			if (count != segment.To - segment.From)
			{
				throw new TapeException(
					$"Segment {segment.SeriesId} is checkpointed at {count} things, not {segment.To - segment.From}.");
			}

			using (RestorationWizard wizard = RestorationWizard.OpenExistingTapeSeries(_library, segment.SeriesId, 0))
			{
				for (long i = 0; i < count; ++i)
					yield return _codePackRat.UnpackModel(wizard.CodeReader);
			}
		}

		private long ReadFrameCount(Guid seriesId)
		{
			BackupWizard wizard = BackupWizard.OpenExisting(_library, seriesId);
			Code checkpoint = wizard.GetLatestCheckpoint();

			return _piedPiper.UnpackModel<long>(checkpoint, CoreSchema.Int64);
		}
		#endregion
	}
}
