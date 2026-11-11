namespace BigRedProf.Stories.LibrarySemanticsLab
{
	internal sealed class LogImage
	{
		#region properties
		public BatchFormat Format
		{
			get;
			set;
		}

		public List<StoredRecord> Committed
		{
			get;
			set;
		} = new List<StoredRecord>();

		public List<StoredRecord> Physical
		{
			get;
			set;
		} = new List<StoredRecord>();

		public long ValidLength
		{
			get;
			set;
		}

		public bool TornTail
		{
			get;
			set;
		}
		#endregion
	}

	/// <summary>
	/// One story generation file. Three framings share the record codec:
	/// per-frame (each flushed record is committed), commit-marker (records
	/// stay invisible until the marker frame), and one length-prefixed frame
	/// for the whole batch.
	/// </summary>
	internal static class StoryLog
	{
		#region constants
		internal const int HeaderLength = 16;
		#endregion

		#region functions
		public static void CreateEmpty(string path, BatchFormat format)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			using (FileStream stream = OpenWrite(path, FileMode.Create))
			{
				byte[] header = Header(format);
				stream.Write(header, 0, header.Length);
				stream.Flush(true);
			}
		}

		public static LogImage Read(string path)
		{
			if (!File.Exists(path))
				throw new FileNotFoundException("Missing story generation.", path);

			using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				return ReadStream(stream, path);
			}
		}

		public static int AppendBatch(
			string path,
			BatchFormat format,
			string key,
			string bodyHash,
			IReadOnlyList<StoredRecord> records,
			FaultInjector? faults
		)
		{
			if (records.Count == 0)
				return 0;

			using (FileStream stream = OpenWrite(path, FileMode.Open))
			{
				LogImage image = ReadStream(stream, path);
				if (image.Format != format)
					throw new InvalidDataException("The story file format does not match the library.");

				if (image.TornTail)
				{
					stream.SetLength(image.ValidLength);
					stream.Flush(true);
				}

				if (format == BatchFormat.CommitMarker)
					TruncateUnmarked(stream, path, image.ValidLength);

				stream.Position = stream.Length;
				int frames = 0;
				if (format == BatchFormat.LengthPrefixed)
				{
					byte[] payload = RecordCodec.EncodeBatch(key, bodyHash, records);
					WriteFrame(stream, payload, faults, tearWholeFrame: true);
					frames = 1;
				}
				else if (format == BatchFormat.PerFrame)
				{
					for (int i = 0; i < records.Count; i++)
					{
						byte[] payload = RecordCodec.EncodeOne(2, key, bodyHash, records[i], i, records.Count);
						WriteFrame(stream, payload, faults, tearWholeFrame: false);
						frames++;
						if (faults != null)
							faults.Hit(FaultPoint.AfterRecord(i));
					}
				}
				else
				{
					for (int i = 0; i < records.Count; i++)
					{
						byte[] payload = RecordCodec.EncodeOne(3, key, bodyHash, records[i], i, records.Count);
						WriteFrame(stream, payload, faults, tearWholeFrame: false);
						frames++;
					}

					if (faults != null)
						faults.Hit(FaultPoint.AfterDataBeforeMarker);

					byte[] marker = RecordCodec.EncodeMarker(key, bodyHash, records.Count);
					WriteFrame(stream, marker, faults, tearWholeFrame: false);
					frames++;
				}

				return frames;
			}
		}

		public static void Rewrite(string path, BatchFormat format, IReadOnlyList<StoredRecord> records)
		{
			string temporary = path + ".rewrite";
			CreateEmpty(temporary, format);
			if (records.Count > 0)
			{
				string key = records[records.Count - 1].IdempotencyKey;
				string hash = records[records.Count - 1].BodyHash;
				AppendBatch(temporary, format, key, hash, records, null);
			}

			File.Move(temporary, path, true);
		}

		public static LogImage Trim(string path, BatchFormat format)
		{
			using (FileStream stream = OpenWrite(path, FileMode.Open))
			{
				LogImage image = ReadStream(stream, path);
				if (image.Format != format)
					throw new InvalidDataException("The story file format does not match the library.");

				if (image.TornTail)
				{
					stream.SetLength(image.ValidLength);
					stream.Flush(true);
				}

				if (format == BatchFormat.CommitMarker)
					TruncateUnmarked(stream, path, image.TornTail ? image.ValidLength : stream.Length);
			}

			return Read(path);
		}

		public static void Truncate(string path, long length)
		{
			using (FileStream stream = OpenWrite(path, FileMode.Open))
			{
				if (length > stream.Length)
					throw new InvalidOperationException("Cannot truncate a story forward.");

				stream.SetLength(length);
				stream.Flush(true);
			}
		}

		public static long FileLength(string path)
		{
			return new FileInfo(path).Length;
		}
		#endregion

		#region private functions
		private static FileStream OpenWrite(string path, FileMode mode)
		{
			return new FileStream(path, mode, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.None);
		}

		private static byte[] Header(BatchFormat format)
		{
			byte[] header = new byte[HeaderLength];
			string magic = Magic(format);
			for (int i = 0; i < magic.Length; i++)
				header[i] = (byte)magic[i];

			header[8] = 1;
			return header;
		}

		private static string Magic(BatchFormat format)
		{
			if (format == BatchFormat.PerFrame)
				return "BRPLABPF";

			if (format == BatchFormat.CommitMarker)
				return "BRPLABCM";

			return "BRPLABLP";
		}

		private static BatchFormat FormatOf(byte[] header, string path)
		{
			string text = EncodingOf(header);
			if (text == "BRPLABPF")
				return BatchFormat.PerFrame;

			if (text == "BRPLABCM")
				return BatchFormat.CommitMarker;

			if (text == "BRPLABLP")
				return BatchFormat.LengthPrefixed;

			throw new InvalidDataException("'" + path + "' is not a lab story generation.");
		}

		private static string EncodingOf(byte[] header)
		{
			char[] chars = new char[8];
			for (int i = 0; i < 8; i++)
				chars[i] = (char)header[i];

			return new string(chars);
		}

		private static LogImage ReadStream(Stream stream, string path)
		{
			if (stream.Length < HeaderLength)
				throw new InvalidDataException("'" + path + "' does not have a lab header.");

			stream.Position = 0;
			byte[] header = new byte[HeaderLength];
			ReadExactly(stream, header);
			BatchFormat format = FormatOf(header, path);
			if (header[8] != 1)
				throw new InvalidDataException("'" + path + "' uses an unsupported lab version.");

			List<RecordCodec.DecodedFrame> frames = new List<RecordCodec.DecodedFrame>();
			long valid = HeaderLength;
			bool torn = false;
			while (stream.Position < stream.Length)
			{
				long start = stream.Position;
				byte[]? payload = TryReadFrame(stream, stream.Length, path, start);
				if (payload == null)
				{
					torn = start < stream.Length;
					break;
				}

				frames.Add(RecordCodec.Decode(payload));
				valid = stream.Position;
			}

			LogImage image = new LogImage();
			image.Format = format;
			image.ValidLength = valid;
			image.TornTail = torn;
			image.Committed = CommitFrames(frames, format, path);
			image.Physical = new List<StoredRecord>();
			for (int i = 0; i < frames.Count; i++)
			{
				if (frames[i].Tag == 4)
					continue;

				for (int r = 0; r < frames[i].Records.Count; r++)
					image.Physical.Add(frames[i].Records[r]);
			}

			return image;
		}

		private static List<StoredRecord> CommitFrames(
			List<RecordCodec.DecodedFrame> frames,
			BatchFormat format,
			string path
		)
		{
			List<StoredRecord> committed = new List<StoredRecord>();
			if (format == BatchFormat.CommitMarker)
			{
				List<StoredRecord> pending = new List<StoredRecord>();
				string pendingKey = "";
				for (int i = 0; i < frames.Count; i++)
				{
					RecordCodec.DecodedFrame frame = frames[i];
					if (frame.Tag == 3)
					{
						pendingKey = frame.Key;
						for (int r = 0; r < frame.Records.Count; r++)
							pending.Add(frame.Records[r]);
					}
					else if (frame.Tag == 4)
					{
						if (pending.Count != frame.Count || pendingKey != frame.Key)
							throw new InvalidDataException("'" + path + "' has a commit marker that does not match the preceding records.");

						committed.AddRange(pending);
						pending.Clear();
						pendingKey = "";
					}
					else
					{
						throw new InvalidDataException("'" + path + "' has a frame that is not part of the commit-marker format.");
					}
				}

				return committed;
			}

			for (int i = 0; i < frames.Count; i++)
			{
				RecordCodec.DecodedFrame frame = frames[i];
				if (format == BatchFormat.LengthPrefixed && frame.Tag != 1)
					throw new InvalidDataException("'" + path + "' has a frame that is not a length-prefixed batch.");

				if (format == BatchFormat.PerFrame && frame.Tag != 2)
					throw new InvalidDataException("'" + path + "' has a frame that is not a per-frame record.");

				for (int r = 0; r < frame.Records.Count; r++)
					committed.Add(frame.Records[r]);
			}

			return committed;
		}

		private static void TruncateUnmarked(FileStream stream, string path, long validLength)
		{
			stream.Position = 0;
			long cut = HeaderLength;
			stream.Position = HeaderLength;
			List<RecordCodec.DecodedFrame> pending = new List<RecordCodec.DecodedFrame>();
			while (stream.Position < validLength)
			{
				long start = stream.Position;
				byte[]? payload = TryReadFrame(stream, validLength, path, start);
				if (payload == null)
					break;

				RecordCodec.DecodedFrame frame = RecordCodec.Decode(payload);
				if (frame.Tag == 4)
				{
					cut = stream.Position;
					pending.Clear();
				}
				else
				{
					pending.Add(frame);
				}
			}

			if (cut < stream.Length)
			{
				stream.SetLength(cut);
				stream.Flush(true);
			}
		}

		private static byte[]? TryReadFrame(Stream stream, long limit, string path, long start)
		{
			if (limit - start < 4)
				return null;

			byte[] lengthBytes = new byte[4];
			if (!TryRead(stream, lengthBytes))
				return null;

			uint payloadLength = (uint)(lengthBytes[0] | (lengthBytes[1] << 8) | (lengthBytes[2] << 16) | (lengthBytes[3] << 24));
			if (payloadLength == 0 || payloadLength > 64 * 1024 * 1024)
			{
				if (start + 4 + payloadLength >= limit)
					return null;

				throw new InvalidDataException("'" + path + "' is corrupt at offset " + start.ToString() + ".");
			}

			long frameLength = 4L + payloadLength + 4L;
			if (start + frameLength > limit)
				return null;

			byte[] payload = new byte[payloadLength];
			if (!TryRead(stream, payload))
				return null;

			byte[] crcBytes = new byte[4];
			if (!TryRead(stream, crcBytes))
				return null;

			uint actual = Crc32.Compute(payload);
			uint expected = (uint)(crcBytes[0] | (crcBytes[1] << 8) | (crcBytes[2] << 16) | (crcBytes[3] << 24));
			if (actual != expected)
			{
				if (stream.Position >= limit)
					return null;

				throw new InvalidDataException("'" + path + "' is corrupt at offset " + start.ToString() + ": the frame checksum does not match.");
			}

			return payload;
		}

		private static void WriteFrame(FileStream stream, byte[] payload, FaultInjector? faults, bool tearWholeFrame)
		{
			byte[] frame = new byte[4 + payload.Length + 4];
			frame[0] = (byte)payload.Length;
			frame[1] = (byte)(payload.Length >> 8);
			frame[2] = (byte)(payload.Length >> 16);
			frame[3] = (byte)(payload.Length >> 24);
			Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
			uint crc = Crc32.Compute(payload);
			int crcAt = 4 + payload.Length;
			frame[crcAt] = (byte)crc;
			frame[crcAt + 1] = (byte)(crc >> 8);
			frame[crcAt + 2] = (byte)(crc >> 16);
			frame[crcAt + 3] = (byte)(crc >> 24);

			bool tear = tearWholeFrame && faults != null && faults.TearWrite;
			if (tear)
			{
				int partial = Math.Max(1, frame.Length / 2);
				stream.Write(frame, 0, partial);
				stream.Flush(true);
				throw new CrashInjectedException(FaultPoint.DuringFrameWrite);
			}

			if (tearWholeFrame && faults != null)
				faults.Hit(FaultPoint.DuringFrameWrite);

			stream.Write(frame, 0, frame.Length);
			stream.Flush(true);
		}

		private static void ReadExactly(Stream stream, byte[] buffer)
		{
			if (!TryRead(stream, buffer))
				throw new EndOfStreamException("The story file ended inside the header.");
		}

		private static bool TryRead(Stream stream, byte[] buffer)
		{
			int offset = 0;
			while (offset < buffer.Length)
			{
				int read = stream.Read(buffer, offset, buffer.Length - offset);
				if (read == 0)
					return false;

				offset += read;
			}

			return true;
		}
		#endregion
	}
}
