using BigRedProf.Data;
using BigRedProf.Stories.Memory;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace BigRedProf.Stories.Api.Internal
{
	internal static class StartUpHelper
	{
		#region functions
		public static void ConfigureKestrel(IServiceCollection services)
		{
			services.Configure<KestrelServerOptions>(
				options =>
				{
					options.AllowSynchronousIO = true;
				}
			);
		}

		public static void InjectDependencies(IServiceCollection services)
		{
			IPiedPiper piedPiper = new PiedPiper();
			piedPiper.RegisterDefaultPackRats();
			services.AddSingleton<IPiedPiper>(piedPiper);

			// TODO: read environment variables or other configuration settings
			// to supported additional stores like kafka
			IList<Code> things = new List<Code>();
			IScribe scribe = new MemoryScribe(things);
			services.AddSingleton<IScribe>(scribe);

			IStoryteller storyteller = new MemoryStoryteller(things);
			services.AddSingleton(storyteller);
		}
		#endregion
	}
}
