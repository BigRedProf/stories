using BigRedProf.Data.Core;
using System;
using System.Collections.Generic;

namespace BigRedProf.Stories
{
	public static class TextTrailSerializer
	{
		#region functions
		public static TextTrail ParseTextRepresentation(string storyId)
		{
			if (string.IsNullOrWhiteSpace(storyId))
				throw new ArgumentException("Story ID must not be null or whitespace.", nameof(storyId));

			return TextTrail.FromStringRepresentation(storyId, '/');
		}

		public static string ToMultihashString(TextTrail storyId)
		{
			if (storyId == null)
				throw new ArgumentNullException(nameof(storyId));

			Multihash multihash = storyId.GetMultihash(MultihashAlgorithm.Sha256);
			return multihash.ToMultibaseString();
		}

		public static TextTrail ToInternalStoryId(TextTrail storyId)
		{
			if (storyId == null)
				throw new ArgumentNullException(nameof(storyId));

			string storyIdHash = ToMultihashString(storyId);
			return ToInternalStoryId(storyIdHash);
		}

		public static TextTrail ToInternalStoryId(string storyIdHash)
		{
			if (string.IsNullOrWhiteSpace(storyIdHash))
				throw new ArgumentException("Story ID hash must not be null or whitespace.", nameof(storyIdHash));

			return new TextTrail("internal", "story-id-hash", storyIdHash);
		}

		public static IEqualityComparer<TextTrail> CreateEqualityComparer()
		{
			return new TextTrailMultihashEqualityComparer();
		}
		#endregion

		#region private classes
		private sealed class TextTrailMultihashEqualityComparer : IEqualityComparer<TextTrail>
		{
			#region IEqualityComparer<TextTrail> methods
			public bool Equals(TextTrail? x, TextTrail? y)
			{
				bool result;
				if (x == null && y == null)
					result = true;
				else if (x == null || y == null)
					result = false;
				else
					result = ToMultihashString(x).Equals(ToMultihashString(y), StringComparison.Ordinal);

				return result;
			}

			public int GetHashCode(TextTrail obj)
			{
				if (obj == null)
					throw new ArgumentNullException(nameof(obj));

				return ToMultihashString(obj).GetHashCode();
			}
			#endregion
		}
		#endregion
	}
}
