using BigRedProf.Data.Core;

namespace BitLayout11
{
	public static class Program
	{
		public static int Main()
		{
			Write("aligned", new Code("10110011"));
			Write("mid", new Code("10101"));
			Write("empty", new Code(0));
			Write("trailing", new Code("1"));
			return 0;
		}

		private static void Write(string name, Code code)
		{
			byte[] bytes = code.ToByteArray();
			Console.Write(name);
			Console.Write('\t');
			Console.Write(code.Length.ToString());
			Console.Write('\t');
			for (int i = 0; i < bytes.Length; i++)
				Console.Write(bytes[i].ToString("x2"));

			Console.WriteLine();
		}
	}
}
