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

		public static TextTrail ParseRouteValue(string storyId)
		{
			if (string.IsNullOrWhiteSpace(storyId))
				throw new ArgumentException("Story ID must not be null or whitespace.", nameof(storyId));

			string textRepresentation = Uri.UnescapeDataString(storyId);
			return ParseTextRepresentation(textRepresentation);
		}

		public static string ToRouteValue(TextTrail storyId)
		{
			if (storyId == null)
				throw new ArgumentNullException(nameof(storyId));

			return Uri.EscapeDataString(storyId.ToString());
		}

		public static string ToMultihashString(TextTrail storyId)
		{
			if (storyId == null)
				throw new ArgumentNullException(nameof(storyId));

			Multihash multihash = storyId.GetMultihash(MultihashAlgorithm.Sha256);
			return multihash.ToMultibaseString();
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
