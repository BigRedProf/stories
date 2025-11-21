using BigRedProf.Data.Core;

namespace BigRedProf.Stories.StoriesCli
{
	public sealed class InspectCommand : Command
	{
		public override int Run(BaseCommandLineOptions baseOpts)
		{
			var o = (InspectOptions)baseOpts;

			long size = new FileInfo(o.TapePath).Length;
			long frames = 0;

			IPiedPiper pied = new PiedPiper();
			pied.RegisterCorePackRats();
			var codeRat = pied.GetPackRat<Code>(CoreSchema.Code);

			using var stream = new FileStream(o.TapePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			using var r = new CodeReader(stream);
			try
			{
				while (true)
				{
					_ = codeRat.UnpackModel(r);
					frames++;
				}
			}
			catch (EndOfStreamException) { }

			Console.WriteLine($"Tape: {o.TapePath}");
			Console.WriteLine($"Size: {size} bytes");
			Console.WriteLine($"Frames: {frames}");
			return 0;
		}

		protected override void OnCancelKeyPress() { }
	}
}
