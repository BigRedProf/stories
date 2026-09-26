using BigRedProf.Data.Core;
using BigRedProf.Stories.Data;
using System;
using System.Collections.Generic;
using System.IO;

namespace BigRedProf.Stories.Disk
{
	/// <summary>
	/// The append-only log for one story.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A story file is durable across process restarts. Each record is one frame,
	/// written and flushed on its own, so a crash loses at most the frame that was
	/// in flight. A reader never returns a partial or checksum-mismatched trailing
	/// frame, and the writer truncates that torn tail before the next append.
	/// Appending after the tear would hide every later record behind it.
	/// </para>
	/// <para>
	/// Version 1 layout of <c>{storyIdHash}.story</c>:
	/// </para>
	/// <code>
	/// Header, 16 bytes:
	///   0..7    ASCII "BRPSTORY"
	///   8..11   uint32 little-endian version = 1
	///   12..15  uint32 little-endian reserved = 0
	/// Frame, repeated:
	///   0..3         uint32 little-endian payload length
	///   4..4+N-1     payload
	///   4+N..4+N+3   uint32 little-endian IEEE CRC-32 of the length field and the payload
	/// </code>
	/// <para>
	/// The payload is <see cref="IPiedPiper.SaveCodeToByteArray"/> of the
	/// <see cref="StoryThing"/> packed with <see cref="StoriesSchemaId.StoryThing"/>.
	/// <see cref="StoryThing.Offset"/> is the zero-based index assigned when the
	/// record was appended. The story id hash is
	/// <see cref="TextTrailSerializer.ToMultihashString"/>, which is filename-safe
	/// on Windows and Linux.
	/// </para>
	/// <para>
	/// One writer at a time is enforced with <c>{storyIdHash}.story.lock</c> opened
	/// at <see cref="FileShare.None"/>. A shared lock on the story file itself is
	/// not enough: on Linux that lock does not exclude a second writer.
	/// </para>
	/// </remarks>
	internal sealed class FileStoryLog : IDisposable
	{
		#region constants
		internal const int HeaderLength = 16;
		internal const uint Version = 1;
		internal const int MaxPayloadLength = 64 * 1024 * 1024;
		private const string MagicText = "BRPSTORY";
		#endregion

		#region fields
		private readonly string _filePath;
		private readonly IPiedPiper _piedPiper;
		private readonly FileStream _lockStream;
		private readonly FileStream _fileStream;
		private readonly object _writeLock;
		private long _nextOffset;
		private bool _headerWritten;
		private bool _disposed;
		#endregion

		#region constructors
		public FileStoryLog(string directoryPath, TextTrail storyId, IPiedPiper piedPiper)
		{
			if (storyId == null)
				throw new ArgumentNullException(nameof(storyId));

			if (piedPiper == null)
				throw new ArgumentNullException(nameof(piedPiper));

			string fullDirectory = FileStoryPaths.GetFullDirectoryPath(directoryPath);
			Directory.CreateDirectory(fullDirectory);

			_filePath = FileStoryPaths.GetStoryFilePath(fullDirectory, storyId);
			_piedPiper = piedPiper;
			_writeLock = new object();

			string lockPath = FileStoryPaths.GetLockFilePath(fullDirectory, storyId);
			try
			{
				_lockStream = new FileStream(
					lockPath,
					FileMode.OpenOrCreate,
					FileAccess.ReadWrite,
					FileShare.None,
					bufferSize: 1,
					FileOptions.None
				);
			}
			catch (IOException exception)
			{
				throw new IOException(
					$"Story '{TextTrailSerializer.ToMultihashString(storyId)}' already has a writer in '{fullDirectory}'. A file-backed story allows one writer at a time.",
					exception
				);
			}

			try
			{
				// Readers open this same file with FileShare.ReadWrite. FileShare.Read
				// lets them. The exclusive lock above, not this share mode, is what
				// rejects a second writer on both Windows and Linux.
				_fileStream = new FileStream(
					_filePath,
					FileMode.OpenOrCreate,
					FileAccess.ReadWrite,
					FileShare.Read,
					bufferSize: 4096,
					FileOptions.None
				);
				RecoverTornTail();
			}
			catch
			{
				_fileStream?.Dispose();
				_lockStream.Dispose();
				throw;
			}
		}
		#endregion

		#region methods
		public void Append(Code[] things)
		{
			if (things == null)
				throw new ArgumentNullException(nameof(things));

			for (int i = 0; i < things.Length; i++)
			{
				if (things[i] == null)
					throw new ArgumentException("Things must not contain null.", nameof(things));
			}

			lock (_writeLock)
			{
				if (_disposed)
					throw new ObjectDisposedException(nameof(FileStoryLog));

				if (things.Length == 0)
					return;

				byte[][] frames = new byte[things.Length][];
				for (int i = 0; i < things.Length; i++)
				{
					StoryThing storyThing = new StoryThing()
					{
						Offset = _nextOffset + i,
						Thing = things[i]
					};
					frames[i] = EncodeFrame(storyThing);
				}

				if (!_headerWritten)
					WriteHeader();

				for (int i = 0; i < frames.Length; i++)
				{
					_fileStream.Write(frames[i], 0, frames[i].Length);
					// Flush each frame to disk before starting the next. A crash then
					// loses only the frame that was mid-write, and the checksum makes
					// that torn tail obvious to the next reader.
					_fileStream.Flush(true);
					_nextOffset++;
				}
			}
		}

		public void Dispose()
		{
			lock (_writeLock)
			{
				if (_disposed)
					return;

				_disposed = true;
				_fileStream.Dispose();
				_lockStream.Dispose();
			}
		}
		#endregion

		#region functions
		public static List<StoryThing> Read(string filePath, IPiedPiper piedPiper)
		{
			if (piedPiper == null)
				throw new ArgumentNullException(nameof(piedPiper));

			if (string.IsNullOrWhiteSpace(filePath))
				throw new ArgumentException("Value cannot be null or whitespace.", nameof(filePath));

			if (!System.IO.File.Exists(filePath))
				return new List<StoryThing>();

			using (FileStream stream = new FileStream(
				filePath,
				FileMode.Open,
				FileAccess.Read,
				FileShare.ReadWrite
			))
			{
				ReadScan scan = ReadCommitted(stream, piedPiper, filePath);
				return scan.Things;
			}
		}
		#endregion

		#region private methods
		private void RecoverTornTail()
		{
			ReadScan scan = ReadCommitted(_fileStream, _piedPiper, _filePath);
			if (_fileStream.Length != scan.ValidEnd)
			{
				_fileStream.SetLength(scan.ValidEnd);
				_fileStream.Flush(true);
			}

			_fileStream.Position = scan.ValidEnd;
			_nextOffset = scan.Things.Count;
			_headerWritten = scan.ValidEnd >= HeaderLength;
		}

		private void WriteHeader()
		{
			byte[] header = CreateHeader();
			_fileStream.Position = 0;
			_fileStream.Write(header, 0, header.Length);
			_fileStream.Flush(true);
			_headerWritten = true;
		}

		private byte[] EncodeFrame(StoryThing storyThing)
		{
			Code packed = _piedPiper.PackModel<StoryThing>(storyThing, StoriesSchemaId.StoryThing);
			byte[] payload = _piedPiper.SaveCodeToByteArray(packed);
			if (payload.Length == 0 || payload.Length > MaxPayloadLength)
			{
				throw new InvalidOperationException(
					$"Story thing at offset {storyThing.Offset} packed to {payload.Length} bytes, which is outside the frame limit of {MaxPayloadLength}."
				);
			}

			byte[] frame = new byte[4 + payload.Length + 4];
			WriteUInt32LittleEndian(frame, 0, (uint)payload.Length);
			Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
			uint crc = Crc32.Compute(frame, 0, 4 + payload.Length);
			WriteUInt32LittleEndian(frame, 4 + payload.Length, crc);
			return frame;
		}
		#endregion

		#region private functions
		private static ReadScan ReadCommitted(Stream stream, IPiedPiper piedPiper, string filePath)
		{
			long fileLength = stream.Length;
			List<StoryThing> things = new List<StoryThing>();
			if (fileLength == 0)
				return new ReadScan(things, 0);

			stream.Position = 0;
			if (fileLength < HeaderLength)
			{
				byte[] prefix = new byte[fileLength];
				ReadExactly(stream, prefix, prefix.Length);
				if (!IsHeaderPrefix(prefix))
				{
					throw new InvalidDataException(
						$"'{filePath}' is not a file-backed story (missing the BRPSTORY header)."
					);
				}

				// A crash while writing the header. No frame can exist yet.
				return new ReadScan(things, 0);
			}

			byte[] header = new byte[HeaderLength];
			ReadExactly(stream, header, HeaderLength);
			ValidateHeader(header, filePath);

			long validEnd = HeaderLength;
			while (stream.Position < fileLength)
			{
				long frameStart = stream.Position;
				StoryThing? thing = TryReadFrame(stream, fileLength, piedPiper, things.Count, filePath);
				if (thing == null)
					break;

				things.Add(thing);
				validEnd = stream.Position;
				System.Diagnostics.Debug.Assert(validEnd > frameStart, "A committed frame must advance the file.");
			}

			return new ReadScan(things, validEnd);
		}

		private static StoryThing? TryReadFrame(
			Stream stream,
			long fileLength,
			IPiedPiper piedPiper,
			long expectedOffset,
			string filePath
		)
		{
			long frameStart = stream.Position;
			long remaining = fileLength - frameStart;
			if (remaining < 4)
				return null;

			byte[] lengthBytes = new byte[4];
			if (!TryReadExactly(stream, lengthBytes, 4))
				return null;

			uint payloadLength = ReadUInt32(lengthBytes, 0);
			long frameLength = 4L + payloadLength + 4L;
			// Not enough bytes left for the declared frame: a crash tore the tail.
			// Stop here so every earlier frame is still returned.
			if (frameLength > remaining)
				return null;

			if (payloadLength == 0)
			{
				// A zero-length frame is never written. At the end of the file it is
				// a torn tail; earlier in the file it would hide every record after it.
				if (frameStart + frameLength == fileLength)
					return null;

				throw new InvalidDataException(
					$"'{filePath}' is corrupt at offset {frameStart}: the frame declares no payload."
				);
			}

			if (payloadLength > MaxPayloadLength)
			{
				throw new InvalidDataException(
					$"'{filePath}' is corrupt at offset {frameStart}: the frame declares {payloadLength} payload bytes."
				);
			}

			byte[] payload = new byte[payloadLength];
			if (!TryReadExactly(stream, payload, (int)payloadLength))
				return null;

			byte[] crcBytes = new byte[4];
			if (!TryReadExactly(stream, crcBytes, 4))
				return null;

			byte[] covered = new byte[4 + payload.Length];
			Buffer.BlockCopy(lengthBytes, 0, covered, 0, 4);
			Buffer.BlockCopy(payload, 0, covered, 4, payload.Length);
			uint actualCrc = Crc32.Compute(covered);
			uint expectedCrc = ReadUInt32(crcBytes, 0);
			if (actualCrc != expectedCrc)
			{
				// A crash can tear the last frame into something that is the declared
				// size but no longer checksums. That tail is discarded. The same
				// failure with bytes after it means an earlier record is damaged.
				if (stream.Position == fileLength)
					return null;

				throw new InvalidDataException(
					$"'{filePath}' is corrupt at offset {frameStart}: the frame checksum does not match."
				);
			}

			Code packed = piedPiper.LoadCodeFromByteArray(payload);
			StoryThing storyThing = piedPiper.UnpackModel<StoryThing>(packed, StoriesSchemaId.StoryThing);
			if (storyThing == null || storyThing.Thing == null || storyThing.Offset != expectedOffset)
			{
				throw new InvalidDataException(
					$"'{filePath}' is corrupt at offset {frameStart}: expected story offset {expectedOffset}."
				);
			}

			return storyThing;
		}

		private static void ValidateHeader(byte[] header, string filePath)
		{
			for (int i = 0; i < MagicText.Length; i++)
			{
				if (header[i] != (byte)MagicText[i])
				{
					throw new InvalidDataException(
						$"'{filePath}' is not a file-backed story (missing the BRPSTORY header)."
					);
				}
			}

			uint version = ReadUInt32(header, 8);
			if (version != Version)
			{
				throw new InvalidDataException(
					$"'{filePath}' uses story file version {version}, which this library cannot read."
				);
			}

			uint reserved = ReadUInt32(header, 12);
			if (reserved != 0)
			{
				throw new InvalidDataException(
					$"'{filePath}' has unsupported header flags 0x{reserved:X8}."
				);
			}
		}

		private static bool IsHeaderPrefix(byte[] prefix)
		{
			byte[] header = CreateHeader();
			if (prefix.Length > header.Length)
				return false;

			for (int i = 0; i < prefix.Length; i++)
			{
				if (prefix[i] != header[i])
					return false;
			}

			return true;
		}

		private static byte[] CreateHeader()
		{
			byte[] header = new byte[HeaderLength];
			for (int i = 0; i < MagicText.Length; i++)
				header[i] = (byte)MagicText[i];

			WriteUInt32LittleEndian(header, 8, Version);
			return header;
		}

		private static void ReadExactly(Stream stream, byte[] buffer, int count)
		{
			if (!TryReadExactly(stream, buffer, count))
				throw new EndOfStreamException("The story file ended before a complete header or frame could be read.");
		}

		private static bool TryReadExactly(Stream stream, byte[] buffer, int count)
		{
			int offset = 0;
			while (offset < count)
			{
				int read = stream.Read(buffer, offset, count - offset);
				if (read == 0)
					return false;

				offset += read;
			}

			return true;
		}

		private static uint ReadUInt32(byte[] buffer, int offset)
		{
			return (uint)(buffer[offset]
				| (buffer[offset + 1] << 8)
				| (buffer[offset + 2] << 16)
				| (buffer[offset + 3] << 24));
		}

		private static void WriteUInt32LittleEndian(byte[] buffer, int offset, uint value)
		{
			buffer[offset] = (byte)value;
			buffer[offset + 1] = (byte)(value >> 8);
			buffer[offset + 2] = (byte)(value >> 16);
			buffer[offset + 3] = (byte)(value >> 24);
		}
		#endregion

		#region private types
		private sealed class ReadScan
		{
			#region constructors
			public ReadScan(List<StoryThing> things, long validEnd)
			{
				Things = things;
				ValidEnd = validEnd;
			}
			#endregion

			#region properties
			public List<StoryThing> Things
			{
				get;
			}

			public long ValidEnd
			{
				get;
			}
			#endregion
		}
		#endregion
	}
}
