namespace BigRedProf.Stories.LibrarySemanticsLab.Tests
{
	internal static class LabPaths
	{
		public static string Create(string name)
		{
			string path = Path.Combine(Path.GetTempPath(), "brp-library-semantics-lab", name);
			if (Directory.Exists(path))
				Directory.Delete(path, true);

			Directory.CreateDirectory(path);
			return path;
		}

		public static void Delete(string path)
		{
			if (Directory.Exists(path))
				Directory.Delete(path, true);
		}
	}
}
