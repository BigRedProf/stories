using BigRedProf.Data.Core;

namespace BigRedProf.Stories.LibrarySemanticsLab
{
	public enum BatchFormat
	{
		PerFrame = 1,
		CommitMarker = 2,
		LengthPrefixed = 3
	}

	public enum RetentionClass
	{
		Stone = 1,
		Sand = 2
	}

	public enum EffectKind
	{
		None = 0,
		ReleaseClaim = 1,
		CompactSand = 2,
		MovementSummary = 3,
		PromoteClaim = 4
	}

	public enum AppendStatus
	{
		Committed = 1,
		AlreadyCommitted = 2,
		PositionConflict = 3,
		IdempotencyConflict = 4,
		PartialBatch = 5,
		Rejected = 6
	}

	public enum CheckResult
	{
		NotChecked = 0,
		Pass = 1,
		Fail = 2
	}

	/// <summary>
	/// One opaque code plus the library facts that sit beside it. The code bits
	/// are not parsed for retention, claims, or idempotency.
	/// </summary>
	public sealed class RecordDraft
	{
		#region constructors
		public RecordDraft(RetentionClass retention, Code body)
		{
			if (body == null)
				throw new ArgumentNullException(nameof(body));

			Retention = retention;
			Body = body;
			EffectArgument = "";
			Supersedes = Array.Empty<KeyHash>();
		}
		#endregion

		#region properties
		public RetentionClass Retention
		{
			get;
		}

		public Code Body
		{
			get;
		}

		public string? DeclaresStoryId
		{
			get;
			set;
		}

		public string? ContentId
		{
			get;
			set;
		}

		public EffectKind Effect
		{
			get;
			set;
		}

		public string EffectArgument
		{
			get;
			set;
		}

		public IReadOnlyList<KeyHash> Supersedes
		{
			get;
			set;
		}
		#endregion
	}

	public sealed class KeyHash
	{
		#region constructors
		public KeyHash(string key, string bodyHash)
		{
			if (string.IsNullOrWhiteSpace(key))
				throw new ArgumentException("A superseded key is required.", nameof(key));

			if (string.IsNullOrWhiteSpace(bodyHash))
				throw new ArgumentException("A superseded body hash is required.", nameof(bodyHash));

			Key = key;
			BodyHash = bodyHash;
		}
		#endregion

		#region properties
		public string Key
		{
			get;
		}

		public string BodyHash
		{
			get;
		}
		#endregion
	}

	public sealed class StoryAppend
	{
		#region constructors
		public StoryAppend(string storyId, long expectedOffset, IReadOnlyList<RecordDraft> records)
		{
			if (string.IsNullOrWhiteSpace(storyId))
				throw new ArgumentException("A story id is required.", nameof(storyId));

			if (records == null)
				throw new ArgumentNullException(nameof(records));

			if (expectedOffset < 0)
				throw new ArgumentOutOfRangeException(nameof(expectedOffset));

			StoryId = storyId;
			ExpectedOffset = expectedOffset;
			Records = records;
		}
		#endregion

		#region properties
		public string StoryId
		{
			get;
		}

		public long ExpectedOffset
		{
			get;
		}

		public IReadOnlyList<RecordDraft> Records
		{
			get;
		}
		#endregion
	}

	public sealed class ContentWrite
	{
		#region constructors
		public ContentWrite(string contentId, Code body)
		{
			if (string.IsNullOrWhiteSpace(contentId))
				throw new ArgumentException("A content id is required.", nameof(contentId));

			if (body == null)
				throw new ArgumentNullException(nameof(body));

			ContentId = contentId;
			Body = body;
		}
		#endregion

		#region properties
		public string ContentId
		{
			get;
		}

		public Code Body
		{
			get;
		}
		#endregion
	}

	public sealed class AppendRequest
	{
		#region constructors
		public AppendRequest(
			string idempotencyKey,
			string expectedEditionId,
			IReadOnlyList<StoryAppend> stories
		)
		{
			if (string.IsNullOrWhiteSpace(idempotencyKey))
				throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));

			if (expectedEditionId == null)
				throw new ArgumentNullException(nameof(expectedEditionId));

			if (stories == null)
				throw new ArgumentNullException(nameof(stories));

			IdempotencyKey = idempotencyKey;
			ExpectedEditionId = expectedEditionId;
			Stories = stories;
			Content = Array.Empty<ContentWrite>();
		}
		#endregion

		#region properties
		public string IdempotencyKey
		{
			get;
		}

		public string ExpectedEditionId
		{
			get;
		}

		public IReadOnlyList<StoryAppend> Stories
		{
			get;
		}

		public IReadOnlyList<ContentWrite> Content
		{
			get;
			set;
		}

		/// <summary>
		/// Comparison path only. Skips the expected-edition check.
		/// </summary>
		public bool SkipExpectedEditionCheck
		{
			get;
			set;
		}

		/// <summary>
		/// Comparison path only. Assigns offsets from the live high-water instead of
		/// rejecting a stale expected offset, so two writers both land.
		/// </summary>
		public bool SkipExpectedOffsetCheck
		{
			get;
			set;
		}
		#endregion
	}

	public sealed class AppendResult
	{
		#region constructors
		public AppendResult(AppendStatus status, string editionId, string detail, int framesFlushed)
		{
			Status = status;
			EditionId = editionId ?? "";
			Detail = detail ?? "";
			FramesFlushed = framesFlushed;
		}
		#endregion

		#region properties
		public AppendStatus Status
		{
			get;
		}

		public string EditionId
		{
			get;
		}

		public string Detail
		{
			get;
		}

		public int FramesFlushed
		{
			get;
		}
		#endregion
	}

	public sealed class StoredRecord
	{
		#region properties
		public string StoryId
		{
			get;
			set;
		} = "";

		public long Offset
		{
			get;
			set;
		}

		public RetentionClass Retention
		{
			get;
			set;
		}

		public Code Body
		{
			get;
			set;
		} = new Code(0);

		public string IdempotencyKey
		{
			get;
			set;
		} = "";

		public string BodyHash
		{
			get;
			set;
		} = "";

		public string? DeclaresStoryId
		{
			get;
			set;
		}

		public string? ContentId
		{
			get;
			set;
		}

		public EffectKind Effect
		{
			get;
			set;
		}

		public string EffectArgument
		{
			get;
			set;
		} = "";

		public IReadOnlyList<KeyHash> Supersedes
		{
			get;
			set;
		} = Array.Empty<KeyHash>();
		#endregion
	}

	public sealed class StoryPrefix
	{
		#region properties
		public string StoryId
		{
			get;
			set;
		} = "";

		public int Generation
		{
			get;
			set;
		}

		public int RecordCount
		{
			get;
			set;
		}

		public long HighWater
		{
			get;
			set;
		}

		public long FileBytes
		{
			get;
			set;
		}

		public string FileHash
		{
			get;
			set;
		} = "";

		/// <summary>
		/// "lab" for a lab generation. "shipped-v1" for an unchanged BRPSTORY file.
		/// </summary>
		public string Encoding
		{
			get;
			set;
		} = "lab";
		#endregion
	}

	public sealed class ContentEntry
	{
		#region properties
		public string ContentId
		{
			get;
			set;
		} = "";

		public string Disposition
		{
			get;
			set;
		} = "";

		public long ByteLength
		{
			get;
			set;
		}

		public string Hash
		{
			get;
			set;
		} = "";
		#endregion
	}

	public sealed class EditionView
	{
		#region properties
		public string Id
		{
			get;
			set;
		} = "";

		public string ParentId
		{
			get;
			set;
		} = "";

		public string CommitKey
		{
			get;
			set;
		} = "";

		public string BodyHash
		{
			get;
			set;
		} = "";

		public IReadOnlyList<StoryPrefix> Stories
		{
			get;
			set;
		} = Array.Empty<StoryPrefix>();

		public IReadOnlyList<ContentEntry> Content
		{
			get;
			set;
		} = Array.Empty<ContentEntry>();

		public IReadOnlyList<StoredRecord> Records
		{
			get;
			set;
		} = Array.Empty<StoredRecord>();
		#endregion
	}

	public sealed class VerificationReport
	{
		#region properties
		public CheckResult Integrity
		{
			get;
			set;
		}

		public CheckResult Completeness
		{
			get;
			set;
		}

		public CheckResult RetentionCompliance
		{
			get;
			set;
		}

		public CheckResult ApplicationReconstructed
		{
			get;
			set;
		}

		public bool IsPublished
		{
			get;
			set;
		}

		public List<string> Findings
		{
			get;
			set;
		} = new List<string>();
		#endregion
	}

	public sealed class EditionPin
	{
		#region constructors
		internal EditionPin(string editionId, string directory)
		{
			EditionId = editionId;
			Directory = directory;
		}
		#endregion

		#region properties
		public string EditionId
		{
			get;
		}

		public string Directory
		{
			get;
		}
		#endregion
	}
}
