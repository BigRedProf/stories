using BigRedProf.Data.Core;
using BigRedProf.Stories.Data;
using Microsoft.Extensions.Logging;

namespace BigRedProf.Stories.StoriesCli
{
	public sealed class RestoreCommand : Command
	{
		#region Command methods
		public override int Run(BaseCommandLineOptions baseOpts)
		{
			// Never implemented. restore-all replays a whole generation of tapes, which is the
			// restore anybody needs (digihouse#397).
			Console.Error.WriteLine("'restore' is not implemented. Use 'restore-all'.");

			return 1;
		}

		protected override void OnCancelKeyPress() 
		{
		}
		#endregion
	}
}
