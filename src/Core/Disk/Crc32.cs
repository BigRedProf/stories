using System;

namespace BigRedProf.Stories.Disk
{
	/// <summary>
	/// IEEE CRC-32 (ISO-HDLC), the checksum on each story frame.
	/// </summary>
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

			return Compute(data, 0, data.Length);
		}

		public static uint Compute(byte[] data, int offset, int count)
		{
			if (data == null)
				throw new ArgumentNullException(nameof(data));

			if (offset < 0 || count < 0 || offset + count > data.Length)
				throw new ArgumentOutOfRangeException(nameof(count));

			uint crc = 0xFFFFFFFFu;
			int end = offset + count;
			for (int i = offset; i < end; i++)
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
					if ((value & 1) == 1)
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
