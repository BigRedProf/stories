using BigRedProf.Data;
using BigRedProf.Stories.Memory;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace BigRedProf.Stories.Api.Internal
{
	internal static class StartUpHelper
	{
		#region constants
		private const string CorsPolicyName = "corsPolicy";
		#endregion

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
			MemoryStoryManager memoryStoryManager = new MemoryStoryManager();
			services.AddSingleton<MemoryStoryManager>(memoryStoryManager);
		}

		public static void AddCorsService(IServiceCollection services) 
		{
			string[] origins = new string[]
			{
				"https://localhost:7290",
				"https://mike.bigredprof.net:7290"
			};
			services.AddCors(
				options =>
				{
					options.AddPolicy(
						name: CorsPolicyName,
						policy =>
						{
							policy.WithOrigins(origins)
								.AllowAnyHeader()
								.AllowAnyMethod()
								.AllowCredentials();
						}
					);
				}
			);			
		}

		public static void UseCors(WebApplication app)
		{
			app.UseCors(CorsPolicyName);
		}
		#endregion
	}
}
