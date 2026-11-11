using System.Text;

namespace BigRedProf.Stories.LibrarySemanticsLab
{
	/// <summary>
	/// Names for the crash points the lab can arm. A hit throws
	/// <see cref="CrashInjectedException"/> and the library instance is dead:
	/// the test disposes it and opens the directory again, which is the restart.
	/// </summary>
	public static class FaultPoint
	{
		#region constants
		public const string DuringFrameWrite = "during-frame-write";
		public const string AfterDataBeforeMarker = "after-data-before-marker";
		public const string AfterFrameBeforePublish = "after-frame-before-publish";
		public const string AfterManifestBeforePointer = "after-manifest-before-pointer";
		public const string AfterPublishBeforeAck = "after-publish-before-ack";
		public const string AfterManifestCaptured = "after-manifest-captured";
		public const string BeforeDestinationPointer = "before-destination-pointer";
		public const string AfterDestinationPointer = "after-destination-pointer";
		#endregion

		#region functions
		public static string AfterRecord(int index)
		{
			return "after-record:" + index.ToString();
		}
		#endregion
	}

	/// <summary>
	/// Deterministic faults. <see cref="Before"/> runs even when the point is not
	/// armed, so a test can compact in the middle of a copy.
	/// </summary>
	public sealed class FaultInjector
	{
		#region properties
		public string? ArmedPoint
		{
			get;
			set;
		}

		public bool TearWrite
		{
			get;
			set;
		}

		public Action<string>? Before
		{
			get;
			set;
		}
		#endregion

		#region methods
		public void Hit(string point)
		{
			if (Before != null)
				Before(point);

			if (ArmedPoint == point)
				throw new CrashInjectedException(point);
		}
		#endregion
	}

	public sealed class CrashInjectedException : Exception
	{
		#region constructors
		public CrashInjectedException(string point)
			: base("Injected crash at '" + point + "'. The process stops here; reopen the library to see what survived.")
		{
			Point = point;
		}
		#endregion

		#region properties
		public string Point
		{
			get;
		}
		#endregion
	}
}
