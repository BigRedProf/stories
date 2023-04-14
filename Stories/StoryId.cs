using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BigRedProf.Stories
{
	public class StoryId
	{
		#region fields
		private string _id;
		#endregion

		#region private constructors
		private StoryId(string id)
		{
			_id = ValidateAndNormalizeStoryId(id);
		}
        #endregion

        #region object methods
        public override bool Equals(object? obj)
        {
			StoryId? other = obj as StoryId;
			if(other == null) 
				return false;

            return _id.Equals(other._id);
        }

        public override int GetHashCode()
        {
			return _id.GetHashCode();
        }

        public override string ToString()
		{
			return _id;
		}
		#endregion

		#region private methods
		private static string ValidateAndNormalizeStoryId(string id)
		{
			if (id == null)
				throw new ArgumentNullException(nameof(id));

			if (id.Length > 128)
				throw new FormatException("A story identifier must be 128-characters or less.");

			id = id.ToLowerInvariant();

			foreach(char c in id)
			{
				if (!(c >= 'a' && c <= 'z' || c == '-' || c == '/'))
				{
					throw new FormatException(
						"A story identifier can only use the characters [a-z] and dashes (-) and slashes (/)."
					);
				}
			}

			return id;
		}
		#endregion

		#region operator overloads
		public static implicit operator StoryId(string id)
		{
			return new StoryId(id);
		}

		public static implicit operator string(StoryId storyId)
		{
			return storyId._id;
		}
		#endregion
	}
}
