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

		/// <summary>
		/// Determines whether a story ID hash is well-formed, meaning it parses as the
		/// multibase multihash string that <see cref="ToMultihashString(TextTrail)"/> produces.
		/// </summary>
		/// <param name="storyIdHash">The story ID hash to check. May be null.</param>
		/// <returns>True if the hash is well-formed, otherwise false.</returns>
		public static bool IsValidStoryIdHash(string? storyIdHash)
		{
			bool isValid;
			if (string.IsNullOrWhiteSpace(storyIdHash))
				isValid = false;
			else
				isValid = Multihash.TryParse(storyIdHash, out Multihash _);

			return isValid;
		}

		public static TextTrail ToInternalStoryId(string storyIdHash)
		{
			if (string.IsNullOrWhiteSpace(storyIdHash))
				throw new ArgumentException("Story ID hash must not be null or whitespace.", nameof(storyIdHash));

			// Anything at all used to be accepted here and wrapped blindly in an internal
			// trail. A client sending a raw story ID instead of its hash therefore got a
			// brand new "island" story: writes were accepted and durably recorded against an
			// ID nobody else would ever read from, so the client looked healthy while its
			// events went nowhere. Failing loudly is the whole point.
			if (!IsValidStoryIdHash(storyIdHash))
			{
				throw new ArgumentException(
					$"Story ID hash '{storyIdHash}' is not a multibase multihash string. A raw story ID " +
					"(for example \"my/story/id\") is not a hash -- pass ToMultihashString(storyId) instead.",
					nameof(storyIdHash)
				);
			}

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
