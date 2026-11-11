using BigRedProf.Data.Core;

namespace BigRedProf.Stories.LibrarySemanticsLab
{
	/// <summary>
	/// A synthetic Digihouse fold. The library does not know these record texts.
	/// Purchases and transfers are one stone record each. Room initialization is
	/// two stone records plus a catalog declaration, published as one edition.
	/// </summary>
	public static class Digihouse
	{
		#region constants
		public const string CatalogStoryId = "digihouse/catalog";
		public const string OwnershipStoryId = "digihouse/ownership";
		public const string MediaStoryId = "digihouse/media";
		#endregion

		#region functions
		public static string RoomStoryId(string roomId)
		{
			return "digihouse/room/" + roomId;
		}

		public static AppendResult InitializeRoom(Library library, string roomId, FaultInjector? faults)
		{
			string roomStory = RoomStoryId(roomId);
			RecordDraft declaration = new RecordDraft(RetentionClass.Stone, LabCode.FromText("room|" + roomId));
			declaration.DeclaresStoryId = roomStory;
			RecordDraft seed = new RecordDraft(RetentionClass.Stone, LabCode.FromText("init|" + roomId + "|seed"));
			RecordDraft ready = new RecordDraft(RetentionClass.Stone, LabCode.FromText("init|" + roomId + "|ready"));
			StoryAppend catalog = new StoryAppend(
				CatalogStoryId,
				library.HighWater(CatalogStoryId),
				new RecordDraft[] { declaration }
			);
			StoryAppend room = new StoryAppend(
				roomStory,
				library.HighWater(roomStory),
				new RecordDraft[] { seed, ready }
			);
			AppendRequest request = new AppendRequest(
				"init-room:" + roomId,
				library.CurrentEditionId,
				new StoryAppend[] { catalog, room }
			);
			return library.Append(request, faults);
		}

		public static AppendResult Purchase(Library library, string goodId, string owner, FaultInjector? faults)
		{
			RecordDraft record = new RecordDraft(RetentionClass.Stone, LabCode.FromText("purchase|" + goodId + "|" + owner));
			StoryAppend story = new StoryAppend(
				OwnershipStoryId,
				library.HighWater(OwnershipStoryId),
				new RecordDraft[] { record }
			);
			AppendRequest request = new AppendRequest(
				"purchase:" + goodId + ":" + owner,
				library.CurrentEditionId,
				new StoryAppend[] { story }
			);
			return library.Append(request, faults);
		}

		public static AppendResult Transfer(Library library, string goodId, string fromOwner, string toOwner, FaultInjector? faults)
		{
			RecordDraft record = new RecordDraft(
				RetentionClass.Stone,
				LabCode.FromText("transfer|" + goodId + "|" + fromOwner + "|" + toOwner)
			);
			StoryAppend story = new StoryAppend(
				OwnershipStoryId,
				library.HighWater(OwnershipStoryId),
				new RecordDraft[] { record }
			);
			AppendRequest request = new AppendRequest(
				"transfer:" + goodId + ":" + fromOwner + ":" + toOwner,
				library.CurrentEditionId,
				new StoryAppend[] { story }
			);
			return library.Append(request, faults);
		}

		public static AppendResult AttachPhoto(Library library, string goodId, Code image, FaultInjector? faults)
		{
			if (image == null)
				throw new ArgumentNullException(nameof(image));

			string contentId = Library.ContentIdFor(image);
			RecordDraft record = new RecordDraft(RetentionClass.Sand, LabCode.FromText("photo|" + goodId + "|" + contentId));
			record.ContentId = contentId;
			StoryAppend story = new StoryAppend(
				MediaStoryId,
				library.HighWater(MediaStoryId),
				new RecordDraft[] { record }
			);
			AppendRequest request = new AppendRequest(
				"photo:" + goodId + ":" + contentId,
				library.CurrentEditionId,
				new StoryAppend[] { story }
			);
			request.Content = new ContentWrite[] { new ContentWrite(contentId, image) };
			return library.Append(request, faults);
		}

		public static AppendResult Move(Library library, string roomId, string objectId, int x, int y, int sequence, FaultInjector? faults)
		{
			RecordDraft record = new RecordDraft(
				RetentionClass.Sand,
				LabCode.FromText("move|" + roomId + "|" + objectId + "|" + x.ToString() + "|" + y.ToString())
			);
			string storyId = RoomStoryId(roomId);
			StoryAppend story = new StoryAppend(storyId, library.HighWater(storyId), new RecordDraft[] { record });
			AppendRequest request = new AppendRequest(
				"move:" + roomId + ":" + objectId + ":" + sequence.ToString(),
				library.CurrentEditionId,
				new StoryAppend[] { story }
			);
			return library.Append(request, faults);
		}

		public static AppendResult CompactMoves(Library library, string roomId, FaultInjector? faults)
		{
			string storyId = RoomStoryId(roomId);
			EditionView current = library.ReadCurrent();
			List<StoredRecord> moves = new List<StoredRecord>();
			for (int i = 0; i < current.Records.Count; i++)
			{
				StoredRecord record = current.Records[i];
				if (record.StoryId != storyId)
					continue;

				string text = LabCode.ToText(record.Body);
				if (text.StartsWith("move|", StringComparison.Ordinal))
					moves.Add(record);
			}

			if (moves.Count == 0)
			{
				return new AppendResult(AppendStatus.Rejected, library.CurrentEditionId, "There are no moves to compact.", 0);
			}

			moves.Sort(delegate(StoredRecord left, StoredRecord right)
			{
				return left.Offset.CompareTo(right.Offset);
			});
			long start = moves[0].Offset;
			long end = moves[moves.Count - 1].Offset + 1;
			string last = LabCode.ToText(moves[moves.Count - 1].Body);
			string[] parts = last.Split('|');
			string summary = "move-summary|" + roomId + "|" + parts[2] + "|" + moves.Count.ToString() + "|" + parts[3] + "|" + parts[4];
			string certificate = "compact-moves|" + roomId + "|" + start.ToString() + "|" + end.ToString();
			return library.CompactSand(
				storyId,
				start,
				end,
				summary,
				certificate,
				"compact-moves:" + roomId + ":" + start.ToString() + ":" + end.ToString(),
				library.CurrentEditionId,
				faults
			);
		}

		public static AppendResult ReleasePhoto(Library library, string goodId, FaultInjector? faults)
		{
			EditionView current = library.ReadCurrent();
			StoredRecord? photo = null;
			for (int i = 0; i < current.Records.Count; i++)
			{
				StoredRecord record = current.Records[i];
				if (record.StoryId != MediaStoryId)
					continue;

				string text = LabCode.ToText(record.Body);
				if (text.StartsWith("photo|" + goodId + "|", StringComparison.Ordinal))
					photo = record;
			}

			if (photo == null)
			{
				return new AppendResult(AppendStatus.Rejected, library.CurrentEditionId, "There is no photo for " + goodId + ".", 0);
			}

			return library.ReleaseClaim(
				MediaStoryId,
				photo.Offset,
				"erase-photo|" + goodId,
				"erase-photo:" + goodId,
				library.CurrentEditionId,
				faults
			);
		}

		public static DigihouseState Read(Library library, string editionId)
		{
			EditionView edition = library.ReadEdition(editionId);
			DigihouseState state = new DigihouseState();
			HashSet<string> released = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < edition.Records.Count; i++)
			{
				if (edition.Records[i].Effect == EffectKind.ReleaseClaim)
					released.Add(edition.Records[i].EffectArgument);
			}

			List<StoredRecord> ordered = edition.Records
				.OrderBy(item => item.StoryId, StringComparer.Ordinal)
				.ThenBy(item => item.Offset)
				.ToList();
			for (int i = 0; i < ordered.Count; i++)
			{
				StoredRecord record = ordered[i];
				if (record.Effect == EffectKind.ReleaseClaim || record.Effect == EffectKind.CompactSand)
					continue;

				string text = LabCode.ToText(record.Body);
				string[] parts = text.Split('|');
				if (parts[0] == "room" && parts.Length == 2)
					state.Rooms.Add(parts[1]);
				else if (parts[0] == "init" && parts.Length == 3 && parts[2] == "ready")
					state.RoomsReady.Add(parts[1]);
				else if (parts[0] == "purchase" && parts.Length == 3)
					state.Owners[parts[1]] = parts[2];
				else if (parts[0] == "transfer" && parts.Length == 4)
					state.Owners[parts[1]] = parts[3];
				else if (parts[0] == "photo" && parts.Length == 3)
				{
					string claim = record.StoryId + "|" + record.Offset.ToString() + "|" + record.ContentId;
					state.Photos[parts[1]] = released.Contains(claim) ? "erased:" + parts[2] : parts[2];
				}
				else if (parts[0] == "move" && parts.Length == 5)
					state.Positions[parts[1] + "/" + parts[2]] = parts[3] + "," + parts[4];
				else if (parts[0] == "move-summary" && parts.Length == 6)
					state.Positions[parts[1] + "/" + parts[2]] = parts[4] + "," + parts[5];
			}

			return state;
		}
		#endregion
	}

	public sealed class DigihouseState
	{
		#region properties
		public Dictionary<string, string> Owners
		{
			get;
		} = new Dictionary<string, string>(StringComparer.Ordinal);

		public Dictionary<string, string> Photos
		{
			get;
		} = new Dictionary<string, string>(StringComparer.Ordinal);

		public Dictionary<string, string> Positions
		{
			get;
		} = new Dictionary<string, string>(StringComparer.Ordinal);

		public HashSet<string> Rooms
		{
			get;
		} = new HashSet<string>(StringComparer.Ordinal);

		public HashSet<string> RoomsReady
		{
			get;
		} = new HashSet<string>(StringComparer.Ordinal);
		#endregion

		#region methods
		public string Render()
		{
			List<string> lines = new List<string>();
			foreach (string room in Rooms.OrderBy(item => item, StringComparer.Ordinal))
				lines.Add("room " + room + (RoomsReady.Contains(room) ? " ready" : " incomplete"));

			foreach (KeyValuePair<string, string> owner in Owners.OrderBy(item => item.Key, StringComparer.Ordinal))
				lines.Add("owns " + owner.Key + " " + owner.Value);

			foreach (KeyValuePair<string, string> photo in Photos.OrderBy(item => item.Key, StringComparer.Ordinal))
				lines.Add("photo " + photo.Key + " " + photo.Value);

			foreach (KeyValuePair<string, string> position in Positions.OrderBy(item => item.Key, StringComparer.Ordinal))
				lines.Add("at " + position.Key + " " + position.Value);

			return string.Join("\n", lines);
		}
		#endregion
	}
}
