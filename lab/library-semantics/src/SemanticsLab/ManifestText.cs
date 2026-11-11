using System.Globalization;
using System.Text;

namespace BigRedProf.Stories.LibrarySemanticsLab
{
	internal sealed class ParsedManifest
	{
		#region properties
		public string Text
		{
			get;
			set;
		} = "";

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

		public List<StoryPrefix> Stories
		{
			get;
			set;
		} = new List<StoryPrefix>();

		public List<ContentEntry> Content
		{
			get;
			set;
		} = new List<ContentEntry>();
		#endregion
	}

	internal static class ManifestText
	{
		#region constants
		public const string Marker = "BRP-LAB-EDITION-1";
		#endregion

		#region functions
		public static string EmptyText()
		{
			return Marker + "\nparent\t-\nkey\t-\nbody\t-\n";
		}

		public static string EmptyId()
		{
			return Hashing.Sha256(EmptyText());
		}

		public static ParsedManifest Build(
			string parentId,
			string commitKey,
			string bodyHash,
			IReadOnlyList<StoryPrefix> stories,
			IReadOnlyList<ContentEntry> content
		)
		{
			StringBuilder builder = new StringBuilder();
			builder.Append(Marker);
			builder.Append('\n');
			builder.Append("parent\t");
			builder.Append(parentId);
			builder.Append('\n');
			builder.Append("key\t");
			builder.Append(commitKey);
			builder.Append('\n');
			builder.Append("body\t");
			builder.Append(bodyHash);
			builder.Append('\n');

			List<StoryPrefix> storyOrder = stories.OrderBy(item => item.StoryId, StringComparer.Ordinal).ToList();
			for (int i = 0; i < storyOrder.Count; i++)
			{
				StoryPrefix story = storyOrder[i];
				builder.Append("story\t");
				builder.Append(story.StoryId);
				builder.Append('\t');
				builder.Append(story.Generation.ToString(CultureInfo.InvariantCulture));
				builder.Append('\t');
				builder.Append(story.RecordCount.ToString(CultureInfo.InvariantCulture));
				builder.Append('\t');
				builder.Append(story.HighWater.ToString(CultureInfo.InvariantCulture));
				builder.Append('\t');
				builder.Append(story.FileBytes.ToString(CultureInfo.InvariantCulture));
				builder.Append('\t');
				builder.Append(story.FileHash);
				builder.Append('\n');
			}

			List<ContentEntry> contentOrder = content.OrderBy(item => item.ContentId, StringComparer.Ordinal).ToList();
			for (int i = 0; i < contentOrder.Count; i++)
			{
				ContentEntry entry = contentOrder[i];
				builder.Append("content\t");
				builder.Append(entry.ContentId);
				builder.Append('\t');
				builder.Append(entry.Disposition);
				builder.Append('\t');
				builder.Append(entry.ByteLength.ToString(CultureInfo.InvariantCulture));
				builder.Append('\t');
				builder.Append(entry.Hash);
				builder.Append('\n');
			}

			ParsedManifest parsed = Parse(builder.ToString());
			return parsed;
		}

		public static ParsedManifest Parse(string text)
		{
			if (text == null)
				throw new ArgumentNullException(nameof(text));

			string[] lines = text.Split('\n');
			if (lines.Length == 0 || lines[0] != Marker)
				throw new InvalidDataException("The edition manifest is not a lab edition.");

			ParsedManifest parsed = new ParsedManifest();
			parsed.Text = text;
			parsed.Id = Hashing.Sha256(text);
			for (int i = 1; i < lines.Length; i++)
			{
				if (lines[i].Length == 0)
					continue;

				string[] parts = lines[i].Split('\t');
				if (parts[0] == "parent" && parts.Length == 2)
					parsed.ParentId = parts[1];
				else if (parts[0] == "key" && parts.Length == 2)
					parsed.CommitKey = parts[1];
				else if (parts[0] == "body" && parts.Length == 2)
					parsed.BodyHash = parts[1];
				else if (parts[0] == "story" && parts.Length == 7)
				{
					StoryPrefix story = new StoryPrefix();
					story.StoryId = parts[1];
					story.Generation = int.Parse(parts[2], CultureInfo.InvariantCulture);
					story.RecordCount = int.Parse(parts[3], CultureInfo.InvariantCulture);
					story.HighWater = long.Parse(parts[4], CultureInfo.InvariantCulture);
					story.FileBytes = long.Parse(parts[5], CultureInfo.InvariantCulture);
					story.FileHash = parts[6];
					parsed.Stories.Add(story);
				}
				else if (parts[0] == "content" && parts.Length == 5)
				{
					ContentEntry entry = new ContentEntry();
					entry.ContentId = parts[1];
					entry.Disposition = parts[2];
					entry.ByteLength = long.Parse(parts[3], CultureInfo.InvariantCulture);
					entry.Hash = parts[4];
					parsed.Content.Add(entry);
				}
				else
				{
					throw new InvalidDataException("The edition manifest has an unreadable line.");
				}
			}

			return parsed;
		}
		#endregion
	}
}
