using BigRedProf.Data.Core;

namespace BigRedProf.Stories.LibrarySemanticsLab
{
	internal static class ContentFiles
	{
		#region functions
		public static string IdFor(Code code)
		{
			if (code == null)
				throw new ArgumentNullException(nameof(code));

			return Hashing.Sha256(Encode(code));
		}

		public static void Write(string directory, Code code)
		{
			if (code == null)
				throw new ArgumentNullException(nameof(code));

			Directory.CreateDirectory(directory);
			byte[] encoded = Encode(code);
			string path = Path.Combine(directory, IdFor(code) + ".bin");
			using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
			{
				stream.Write(encoded, 0, encoded.Length);
				stream.Flush(true);
			}
		}

		public static Code Read(string path)
		{
			byte[] encoded = File.ReadAllBytes(path);
			return Decode(encoded);
		}

		public static byte[] Encode(Code code)
		{
			byte[] body = code.ToByteArray();
			byte[] encoded = new byte[8 + body.Length];
			WriteInt(encoded, 0, code.Length);
			WriteInt(encoded, 4, body.Length);
			Buffer.BlockCopy(body, 0, encoded, 8, body.Length);
			return encoded;
		}
		#endregion

		#region private functions
		private static Code Decode(byte[] encoded)
		{
			if (encoded.Length < 8)
				throw new InvalidDataException("A content file is too short.");

			int bitLength = ReadInt(encoded, 0);
			int byteLength = ReadInt(encoded, 4);
			if (byteLength < 0 || encoded.Length != 8 + byteLength)
				throw new InvalidDataException("A content file length does not match its header.");

			if (bitLength == 0)
				return new Code(0);

			byte[] body = new byte[byteLength];
			Buffer.BlockCopy(encoded, 8, body, 0, byteLength);
			return new Code(body, bitLength);
		}

		private static void WriteInt(byte[] buffer, int offset, int value)
		{
			buffer[offset] = (byte)value;
			buffer[offset + 1] = (byte)(value >> 8);
			buffer[offset + 2] = (byte)(value >> 16);
			buffer[offset + 3] = (byte)(value >> 24);
		}

		private static int ReadInt(byte[] buffer, int offset)
		{
			return buffer[offset]
				| (buffer[offset + 1] << 8)
				| (buffer[offset + 2] << 16)
				| (buffer[offset + 3] << 24);
		}
		#endregion
	}
}
