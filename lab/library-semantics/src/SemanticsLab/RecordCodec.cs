using BigRedProf.Data.Core;
using System.Text;

namespace BigRedProf.Stories.LibrarySemanticsLab
{
	internal static class RecordCodec
	{
		#region functions
		public static byte[] EncodeBatch(string key, string bodyHash, IReadOnlyList<StoredRecord> records)
		{
			using (MemoryStream stream = new MemoryStream())
			{
				Write(stream, 1);
				WriteString(stream, key);
				WriteString(stream, bodyHash);
				WriteInt(stream, records.Count);
				for (int i = 0; i < records.Count; i++)
					WriteRecord(stream, records[i]);

				return stream.ToArray();
			}
		}

		public static byte[] EncodeOne(byte tag, string key, string bodyHash, StoredRecord record, int index, int count)
		{
			using (MemoryStream stream = new MemoryStream())
			{
				Write(stream, tag);
				if (tag == 3)
				{
					WriteInt(stream, index);
					WriteInt(stream, count);
				}

				WriteString(stream, key);
				WriteString(stream, bodyHash);
				WriteRecord(stream, record);
				return stream.ToArray();
			}
		}

		public static byte[] EncodeMarker(string key, string bodyHash, int count)
		{
			using (MemoryStream stream = new MemoryStream())
			{
				Write(stream, 4);
				WriteString(stream, key);
				WriteString(stream, bodyHash);
				WriteInt(stream, count);
				return stream.ToArray();
			}
		}

		public static DecodedFrame Decode(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			{
				int tag = ReadByte(stream);
				DecodedFrame frame = new DecodedFrame();
				frame.Tag = tag;
				if (tag == 1)
				{
					frame.Key = ReadString(stream);
					frame.BodyHash = ReadString(stream);
					int count = ReadInt(stream);
					StoredRecord[] records = new StoredRecord[count];
					for (int i = 0; i < count; i++)
						records[i] = ReadRecord(stream);

					frame.Records = records;
					return frame;
				}

				if (tag == 2 || tag == 3)
				{
					if (tag == 3)
					{
						frame.Index = ReadInt(stream);
						frame.Count = ReadInt(stream);
					}

					frame.Key = ReadString(stream);
					frame.BodyHash = ReadString(stream);
					StoredRecord record = ReadRecord(stream);
					frame.Records = new StoredRecord[] { record };
					return frame;
				}

				if (tag == 4)
				{
					frame.Key = ReadString(stream);
					frame.BodyHash = ReadString(stream);
					frame.Count = ReadInt(stream);
					frame.Records = Array.Empty<StoredRecord>();
					return frame;
				}

				throw new InvalidDataException("Unknown lab frame tag " + tag.ToString() + ".");
			}
		}

		public static string HashDrafts(IReadOnlyList<StoryAppend> stories)
		{
			StringBuilder builder = new StringBuilder();
			List<StoryAppend> ordered = stories.OrderBy(item => item.StoryId, StringComparer.Ordinal).ToList();
			for (int s = 0; s < ordered.Count; s++)
			{
				StoryAppend story = ordered[s];
				builder.Append(story.StoryId);
				builder.Append('\n');
				for (int i = 0; i < story.Records.Count; i++)
				{
					RecordDraft draft = story.Records[i];
					AppendDraft(builder, draft);
				}
			}

			return Hashing.Sha256(builder.ToString());
		}

		public static bool SameDraft(RecordDraft draft, StoredRecord stored)
		{
			if (draft.Retention != stored.Retention)
				return false;

			if (draft.Effect != stored.Effect)
				return false;

			if ((draft.EffectArgument ?? "") != (stored.EffectArgument ?? ""))
				return false;

			if ((draft.DeclaresStoryId ?? "") != (stored.DeclaresStoryId ?? ""))
				return false;

			if ((draft.ContentId ?? "") != (stored.ContentId ?? ""))
				return false;

			if (!draft.Body.Equals(stored.Body))
				return false;

			return SameSupersedes(draft.Supersedes, stored.Supersedes);
		}
		#endregion

		#region private functions
		private static void AppendDraft(StringBuilder builder, RecordDraft draft)
		{
			builder.Append((int)draft.Retention);
			builder.Append('|');
			builder.Append((int)draft.Effect);
			builder.Append('|');
			builder.Append(draft.EffectArgument ?? "");
			builder.Append('|');
			builder.Append(draft.DeclaresStoryId ?? "");
			builder.Append('|');
			builder.Append(draft.ContentId ?? "");
			builder.Append('|');
			builder.Append(LabCode.Describe(draft.Body));
			builder.Append('|');
			IReadOnlyList<KeyHash> superseded = draft.Supersedes ?? Array.Empty<KeyHash>();
			for (int i = 0; i < superseded.Count; i++)
			{
				builder.Append(superseded[i].Key);
				builder.Append('=');
				builder.Append(superseded[i].BodyHash);
				builder.Append(',');
			}

			builder.Append('\n');
		}

		private static bool SameSupersedes(IReadOnlyList<KeyHash> left, IReadOnlyList<KeyHash> right)
		{
			int leftCount = left == null ? 0 : left.Count;
			int rightCount = right == null ? 0 : right.Count;
			if (leftCount != rightCount)
				return false;

			for (int i = 0; i < leftCount; i++)
			{
				if (left![i].Key != right![i].Key || left[i].BodyHash != right[i].BodyHash)
					return false;
			}

			return true;
		}

		private static void WriteRecord(Stream stream, StoredRecord record)
		{
			WriteString(stream, record.IdempotencyKey ?? "");
			WriteString(stream, record.BodyHash ?? "");
			WriteLong(stream, record.Offset);
			Write(stream, (byte)record.Retention);
			Write(stream, (byte)record.Effect);
			WriteString(stream, record.EffectArgument ?? "");
			WriteString(stream, record.DeclaresStoryId ?? "");
			WriteString(stream, record.ContentId ?? "");
			byte[] codeBytes = record.Body.ToByteArray();
			WriteInt(stream, record.Body.Length);
			WriteInt(stream, codeBytes.Length);
			stream.Write(codeBytes, 0, codeBytes.Length);
			IReadOnlyList<KeyHash> superseded = record.Supersedes ?? Array.Empty<KeyHash>();
			WriteInt(stream, superseded.Count);
			for (int i = 0; i < superseded.Count; i++)
			{
				WriteString(stream, superseded[i].Key);
				WriteString(stream, superseded[i].BodyHash);
			}
		}

		private static StoredRecord ReadRecord(Stream stream)
		{
			StoredRecord record = new StoredRecord();
			record.IdempotencyKey = ReadString(stream);
			record.BodyHash = ReadString(stream);
			record.Offset = ReadLong(stream);
			record.Retention = (RetentionClass)ReadByte(stream);
			record.Effect = (EffectKind)ReadByte(stream);
			record.EffectArgument = ReadString(stream);
			string declares = ReadString(stream);
			record.DeclaresStoryId = declares.Length == 0 ? null : declares;
			string content = ReadString(stream);
			record.ContentId = content.Length == 0 ? null : content;
			int bitLength = ReadInt(stream);
			int byteLength = ReadInt(stream);
			byte[] codeBytes = new byte[byteLength];
			ReadExactly(stream, codeBytes);
			if (bitLength == 0)
				record.Body = new Code(0);
			else
				record.Body = new Code(codeBytes, bitLength);

			int supersededCount = ReadInt(stream);
			KeyHash[] superseded = new KeyHash[supersededCount];
			for (int i = 0; i < supersededCount; i++)
				superseded[i] = new KeyHash(ReadString(stream), ReadString(stream));

			record.Supersedes = superseded;
			return record;
		}

		private static void WriteString(Stream stream, string value)
		{
			byte[] data = Encoding.UTF8.GetBytes(value ?? "");
			if (data.Length > ushort.MaxValue)
				throw new InvalidOperationException("A lab string is longer than 65535 bytes.");

			WriteUShort(stream, (ushort)data.Length);
			stream.Write(data, 0, data.Length);
		}

		private static string ReadString(Stream stream)
		{
			int length = ReadUShort(stream);
			byte[] data = new byte[length];
			ReadExactly(stream, data);
			return Encoding.UTF8.GetString(data);
		}

		private static void Write(Stream stream, byte value)
		{
			stream.WriteByte(value);
		}

		private static int ReadByte(Stream stream)
		{
			int value = stream.ReadByte();
			if (value < 0)
				throw new EndOfStreamException("The frame ended early.");

			return value;
		}

		private static void WriteUShort(Stream stream, ushort value)
		{
			stream.WriteByte((byte)value);
			stream.WriteByte((byte)(value >> 8));
		}

		private static int ReadUShort(Stream stream)
		{
			int low = ReadByte(stream);
			int high = ReadByte(stream);
			return low | (high << 8);
		}

		private static void WriteInt(Stream stream, int value)
		{
			WriteUInt(stream, (uint)value);
		}

		private static int ReadInt(Stream stream)
		{
			return (int)ReadUInt(stream);
		}

		private static void WriteUInt(Stream stream, uint value)
		{
			stream.WriteByte((byte)value);
			stream.WriteByte((byte)(value >> 8));
			stream.WriteByte((byte)(value >> 16));
			stream.WriteByte((byte)(value >> 24));
		}

		private static uint ReadUInt(Stream stream)
		{
			int b0 = ReadByte(stream);
			int b1 = ReadByte(stream);
			int b2 = ReadByte(stream);
			int b3 = ReadByte(stream);
			return (uint)(b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
		}

		private static void WriteLong(Stream stream, long value)
		{
			ulong bits = (ulong)value;
			WriteUInt(stream, (uint)bits);
			WriteUInt(stream, (uint)(bits >> 32));
		}

		private static long ReadLong(Stream stream)
		{
			ulong low = ReadUInt(stream);
			ulong high = ReadUInt(stream);
			return (long)(low | (high << 32));
		}

		private static void ReadExactly(Stream stream, byte[] buffer)
		{
			int offset = 0;
			while (offset < buffer.Length)
			{
				int read = stream.Read(buffer, offset, buffer.Length - offset);
				if (read == 0)
					throw new EndOfStreamException("The frame ended early.");

				offset += read;
			}
		}
		#endregion

		#region private types
		internal sealed class DecodedFrame
		{
			#region properties
			public int Tag
			{
				get;
				set;
			}

			public string Key
			{
				get;
				set;
			} = "";

			public string BodyHash
			{
				get;
				set;
			} = "";

			public int Index
			{
				get;
				set;
			}

			public int Count
			{
				get;
				set;
			}

			public IReadOnlyList<StoredRecord> Records
			{
				get;
				set;
			} = Array.Empty<StoredRecord>();
			#endregion
		}
		#endregion
	}
}
