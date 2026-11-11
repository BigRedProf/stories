using System.Security.Cryptography;
using System.Text;

namespace BigRedProf.Stories.LibrarySemanticsLab
{
	internal static class Hashing
	{
		#region functions
		public static string Sha256(byte[] data)
		{
			if (data == null)
				throw new ArgumentNullException(nameof(data));

			return ToHex(SHA256.HashData(data));
		}

		public static string Sha256(string text)
		{
			if (text == null)
				throw new ArgumentNullException(nameof(text));

			return Sha256(Encoding.UTF8.GetBytes(text));
		}

		public static string FilePrefix(string path, long count)
		{
			if (count < 0)
				throw new ArgumentOutOfRangeException(nameof(count));

			using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				if (stream.Length < count)
					throw new InvalidDataException("'" + path + "' is shorter than the edition says it is.");

				byte[] data = new byte[count];
				ReadExactly(stream, data, (int)count);
				return Sha256(data);
			}
		}

		public static string ToHex(byte[] data)
		{
			StringBuilder builder = new StringBuilder(data.Length * 2);
			for (int i = 0; i < data.Length; i++)
				builder.Append(data[i].ToString("x2"));

			return builder.ToString();
		}
		#endregion

		#region private functions
		private static void ReadExactly(Stream stream, byte[] buffer, int count)
		{
			int offset = 0;
			while (offset < count)
			{
				int read = stream.Read(buffer, offset, count - offset);
				if (read == 0)
					throw new EndOfStreamException("The file ended before the edition prefix was read.");

				offset += read;
			}
		}
		#endregion
	}

	internal static class Crc32
	{
		#region static fields
		private const uint Polynomial = 0xEDB88320u;
		private static readonly uint[] Table = CreateTable();
		#endregion

		#region functions
		public static uint Compute(byte[] data)
		{
			if (data == null)
				throw new ArgumentNullException(nameof(data));

			uint crc = 0xFFFFFFFFu;
			for (int i = 0; i < data.Length; i++)
				crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);

			return crc ^ 0xFFFFFFFFu;
		}
		#endregion

		#region private functions
		private static uint[] CreateTable()
		{
			uint[] table = new uint[256];
			for (uint i = 0; i < 256; i++)
			{
				uint value = i;
				for (int bit = 0; bit < 8; bit++)
				{
					if ((value & 1u) == 1u)
						value = (value >> 1) ^ Polynomial;
					else
						value >>= 1;
				}

				table[i] = value;
			}

			return table;
		}
		#endregion
	}
}
