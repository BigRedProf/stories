using System.Text;

namespace BigRedProf.Stories.LibrarySemanticsLab
{
	/// <summary>
	/// What the existing tape backup actually is: an ordered copy of code bits
	/// with a checkpoint count, and no edition, content store, or retention.
	/// </summary>
	public static class TapeSeriesBackup
	{
		#region functions
		public static TapeCopy CopyHeads(Library source, string tapePath, FaultInjector? faults)
		{
			if (source == null)
				throw new ArgumentNullException(nameof(source));

			if (string.IsNullOrWhiteSpace(tapePath))
				throw new ArgumentException("A tape path is required.", nameof(tapePath));

			EditionView started = source.ReadCurrent();
			List<string> storyIds = new List<string>();
			for (int i = 0; i < started.Stories.Count; i++)
				storyIds.Add(started.Stories[i].StoryId);

			storyIds.Sort(StringComparer.Ordinal);
			StringBuilder tape = new StringBuilder();
			tape.Append("BRP-LAB-TAPE-1\n");
			Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
			for (int i = 0; i < storyIds.Count; i++)
			{
				string storyId = storyIds[i];
				if (faults != null)
					faults.Hit("before-story:" + storyId);

				IReadOnlyList<StoredRecord> head = source.ReadCommitted(storyId);
				tape.Append("story\t");
				tape.Append(storyId);
				tape.Append('\n');
				for (int r = 0; r < head.Count; r++)
				{
					tape.Append("code\t");
					tape.Append(head[r].Offset.ToString());
					tape.Append('\t');
					tape.Append(LabCode.Describe(head[r].Body));
					tape.Append('\n');
				}

				tape.Append("checkpoint\t");
				tape.Append(storyId);
				tape.Append('\t');
				tape.Append(head.Count.ToString());
				tape.Append('\n');
				counts[storyId] = head.Count;
			}

			File.WriteAllText(tapePath, tape.ToString());
			bool matches = CountsMatch(started, counts);
			string detail = matches
				? "The tape counts match the edition observed at the start."
				: "The tape counts match no single edition. Stories were copied from moving heads.";
			return new TapeCopy(tapePath, started.Id, counts, matches, detail);
		}
		#endregion

		#region private functions
		private static bool CountsMatch(EditionView edition, Dictionary<string, int> counts)
		{
			if (edition.Stories.Count != counts.Count)
				return false;

			for (int i = 0; i < edition.Stories.Count; i++)
			{
				StoryPrefix story = edition.Stories[i];
				int count;
				if (!counts.TryGetValue(story.StoryId, out count))
					return false;

				if (count != story.RecordCount)
					return false;
			}

			return true;
		}
		#endregion
	}

	public sealed class TapeCopy
	{
		#region constructors
		public TapeCopy(string path, string startedEditionId, Dictionary<string, int> counts, bool matchesStartEdition, string detail)
		{
			Path = path;
			StartedEditionId = startedEditionId;
			Counts = counts;
			MatchesStartEdition = matchesStartEdition;
			Detail = detail;
		}
		#endregion

		#region properties
		public string Path
		{
			get;
		}

		public string StartedEditionId
		{
			get;
		}

		public Dictionary<string, int> Counts
		{
			get;
		}

		public bool MatchesStartEdition
		{
			get;
		}

		public string Detail
		{
			get;
		}
		#endregion
	}
}
