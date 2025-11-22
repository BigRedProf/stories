namespace BigRedProf.Stories.StoriesCli.Test._TestHelpers
{
	/// <summary>
	/// Creates a temporary working directory for the entire test collection.
	/// Ensures deletion even if tests fail.
	/// </summary>
	public sealed class TempDirectoryFixture : IDisposable
	{
		public string RootPath { get; }

		public TempDirectoryFixture()
		{
			string guid = Guid.NewGuid().ToString("N");
			RootPath = Path.Combine(Path.GetTempPath(), "BigRedProfTest_" + guid);

			Directory.CreateDirectory(RootPath);
		}

		public void Dispose()
		{
			try
			{
				if (Directory.Exists(RootPath))
					Directory.Delete(RootPath, true);
			}
			catch
			{
				// Never throw here—test teardown must not fail.
			}
		}
	}
}
