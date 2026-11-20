using BigRedProf.Data.Core;

namespace BigRedProf.Stories.LibrarySemanticsLab
{
	/// <summary>
	/// A second application, a specimen notebook, on the same library contracts.
	/// Accessions are stone. Photos and GPS tracks are sand. Nothing here is Digihouse.
	/// </summary>
	public static class FieldNotes
	{
		#region constants
		public const string CatalogStoryId = "fieldnotes/catalog";
		public const string AccessionStoryId = "fieldnotes/accessions";
		#endregion

		#region functions
		public static string TrackStoryId(string specimenId)
		{
			return "fieldnotes/track/" + specimenId;
		}

		public static AppendResult Accession(Library library, string specimenId, string collector, FaultInjector? faults)
		{
			string trackStory = TrackStoryId(specimenId);
			RecordDraft accession = new RecordDraft(
				RetentionClass.Stone,
				LabCode.FromText("accession|" + specimenId + "|" + collector)
			);
			accession.DeclaresStoryId = trackStory;
			RecordDraft opened = new RecordDraft(RetentionClass.Stone, LabCode.FromText("track-open|" + specimenId));
			StoryAppend catalog = new StoryAppend(
				AccessionStoryId,
				library.HighWater(AccessionStoryId),
				new RecordDraft[] { accession }
			);
			StoryAppend track = new StoryAppend(trackStory, library.HighWater(trackStory), new RecordDraft[] { opened });
			AppendRequest request = new AppendRequest(
				"accession:" + specimenId + ":" + collector,
				library.CurrentEditionId,
				new StoryAppend[] { catalog, track }
			);
			return library.Append(request, faults);
		}

		public static AppendResult AttachPhoto(Library library, string specimenId, Code image, FaultInjector? faults)
		{
			string contentId = Library.ContentIdFor(image);
			string storyId = TrackStoryId(specimenId);
			RecordDraft record = new RecordDraft(RetentionClass.Sand, LabCode.FromText("photo|" + specimenId + "|" + contentId));
			record.ContentId = contentId;
			StoryAppend story = new StoryAppend(storyId, library.HighWater(storyId), new RecordDraft[] { record });
			AppendRequest request = new AppendRequest(
				"field-photo:" + specimenId + ":" + contentId,
				library.CurrentEditionId,
				new StoryAppend[] { story }
			);
			request.Content = new ContentWrite[] { new ContentWrite(contentId, image) };
			return library.Append(request, faults);
		}

		public static AppendResult Track(Library library, string specimenId, string latitude, string longitude, int sequence, FaultInjector? faults)
		{
			string storyId = TrackStoryId(specimenId);
			RecordDraft record = new RecordDraft(
				RetentionClass.Sand,
				LabCode.FromText("track|" + specimenId + "|" + latitude + "|" + longitude)
			);
			StoryAppend story = new StoryAppend(storyId, library.HighWater(storyId), new RecordDraft[] { record });
			AppendRequest request = new AppendRequest(
				"track:" + specimenId + ":" + sequence.ToString(),
				library.CurrentEditionId,
				new StoryAppend[] { story }
			);
			return library.Append(request, faults);
		}

		public static AppendResult CompactTrack(Library library, string specimenId, FaultInjector? faults)
		{
			string storyId = TrackStoryId(specimenId);
			EditionView current = library.ReadCurrent();
			List<StoredRecord> points = new List<StoredRecord>();
			for (int i = 0; i < current.Records.Count; i++)
			{
				StoredRecord record = current.Records[i];
				if (record.StoryId != storyId)
					continue;

				if (LabCode.ToText(record.Body).StartsWith("track|", StringComparison.Ordinal))
					points.Add(record);
			}

			if (points.Count == 0)
				return new AppendResult(AppendStatus.Rejected, library.CurrentEditionId, "There are no track points.", 0);

			points.Sort(delegate(StoredRecord left, StoredRecord right)
			{
				return left.Offset.CompareTo(right.Offset);
			});
			long start = points[0].Offset;
			long end = points[points.Count - 1].Offset + 1;
			string last = LabCode.ToText(points[points.Count - 1].Body);
			string[] parts = last.Split('|');
			return library.CompactSand(
				storyId,
				start,
				end,
				"track-summary|" + specimenId + "|" + points.Count.ToString() + "|" + parts[2] + "|" + parts[3],
				"compact-track|" + specimenId + "|" + start.ToString() + "|" + end.ToString(),
				"compact-track:" + specimenId + ":" + start.ToString() + ":" + end.ToString(),
				library.CurrentEditionId,
				faults
			);
		}

		public static FieldNotesState Read(Library library, string editionId)
		{
			EditionView edition = library.ReadEdition(editionId);
			FieldNotesState state = new FieldNotesState();
			List<StoredRecord> ordered = edition.Records
				.OrderBy(item => item.StoryId, StringComparer.Ordinal)
				.ThenBy(item => item.Offset)
				.ToList();
			for (int i = 0; i < ordered.Count; i++)
			{
				StoredRecord record = ordered[i];
				if (record.Effect == EffectKind.CompactSand)
					continue;

				string text = LabCode.ToText(record.Body);
				string[] parts = text.Split('|');
				if (parts[0] == "accession" && parts.Length == 3)
					state.Collectors[parts[1]] = parts[2];
				else if (parts[0] == "photo" && parts.Length == 3)
					state.Photos[parts[1]] = record.ContentId ?? "";
				else if (parts[0] == "track-summary" && parts.Length == 5)
					state.TrackEnds[parts[1]] = parts[2] + " points ending " + parts[3] + "," + parts[4];
				else if (parts[0] == "track" && parts.Length == 4)
					state.TrackEnds[parts[1]] = "point " + parts[2] + "," + parts[3];
			}

			return state;
		}
		#endregion
	}

	public sealed class FieldNotesState
	{
		#region properties
		public Dictionary<string, string> Collectors
		{
			get;
		} = new Dictionary<string, string>(StringComparer.Ordinal);

		public Dictionary<string, string> Photos
		{
			get;
		} = new Dictionary<string, string>(StringComparer.Ordinal);

		public Dictionary<string, string> TrackEnds
		{
			get;
		} = new Dictionary<string, string>(StringComparer.Ordinal);
		#endregion

		#region methods
		public string Render()
		{
			List<string> lines = new List<string>();
			foreach (KeyValuePair<string, string> collector in Collectors.OrderBy(item => item.Key, StringComparer.Ordinal))
				lines.Add("specimen " + collector.Key + " " + collector.Value);

			foreach (KeyValuePair<string, string> photo in Photos.OrderBy(item => item.Key, StringComparer.Ordinal))
				lines.Add("photo " + photo.Key + " " + photo.Value);

			foreach (KeyValuePair<string, string> track in TrackEnds.OrderBy(item => item.Key, StringComparer.Ordinal))
				lines.Add("track " + track.Key + " " + track.Value);

			return string.Join("\n", lines);
		}
		#endregion
	}
}
