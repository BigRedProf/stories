using BigRedProf.Data.Core;
using System.Text;

namespace BigRedProf.Stories.LibrarySemanticsLab
{
	/// <summary>
	/// Lab text stored as a <see cref="Code"/>. A trailing 1 bit keeps the code off
	/// a byte boundary so a restore that rounded to bytes would fail the comparison.
	/// </summary>
	public static class LabCode
	{
		#region functions
		public static Code FromText(string text)
		{
			if (text == null)
				throw new ArgumentNullException(nameof(text));

			byte[] data = Encoding.UTF8.GetBytes(text);
			StringBuilder bits = new StringBuilder((data.Length * 8) + 1);
			for (int i = 0; i < data.Length; i++)
			{
				byte value = data[i];
				for (int bit = 0; bit < 8; bit++)
				{
					int extracted = (value >> bit) & 1;
					bits.Append(extracted == 1 ? '1' : '0');
				}
			}

			bits.Append('1');
			return new Code(bits.ToString());
		}

		public static string ToText(Code code)
		{
			if (code == null)
				throw new ArgumentNullException(nameof(code));

			if (code.Length < 1 || ((code.Length - 1) % 8) != 0)
				throw new InvalidDataException("The code is not a lab text code.");

			int marker = code[code.Length - 1];
			if (marker != 1)
				throw new InvalidDataException("The code is not a lab text code.");

			int payloadBits = code.Length - 1;
			byte[] data = new byte[payloadBits / 8];
			for (int i = 0; i < payloadBits; i++)
			{
				int bit = code[i];
				if (bit == 1)
					data[i / 8] |= (byte)(1 << (i % 8));
			}

			return Encoding.UTF8.GetString(data);
		}

		public static string Describe(Code code)
		{
			if (code == null)
				throw new ArgumentNullException(nameof(code));

			return code.Length.ToString() + ":" + Hashing.ToHex(code.ToByteArray());
		}
		#endregion
	}
}
