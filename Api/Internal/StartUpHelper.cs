using BigRedProf.Data;
using BigRedProf.Stories.Api.Hubs;
using BigRedProf.Stories.Memory;
using Microsoft.AspNetCore.ResponseCompression;
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

		public static void ConfigureSignalR(IApplicationBuilder app)
		{
			app.UseRouting();

			app.UseEndpoints(
				endpoints =>
				{
					endpoints.MapHub<StoryListenerHub>("/_StorylistenerHub");
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

			services.AddSingleton<StoryListenerManager>();
		}

		public static void AddCorsService(IServiceCollection services) 
		{
			// TODO: review
			services.AddCors(
				options =>
				{
					options.AddPolicy(
						name: CorsPolicyName,
						policy =>
						{
							policy
								.AllowAnyHeader()
								.AllowAnyMethod()
								.AllowCredentials()
								.SetIsOriginAllowed(_ => true)
							;
						}
					);
				}
			);			
		}

		public static void AddSignalRService(IServiceCollection services)
		{
			services
				.AddSignalR()
				.AddMessagePackProtocol()
				.AddHubOptions<StoryListenerHub>(
					options =>
					{
						// make this somewhat long as Digihouse Unity load can be very slow and
						// runs on lone browser thread
						options.KeepAliveInterval = TimeSpan.FromMinutes(5);
					}
				)
			;
			
			services.AddResponseCompression(
				opts =>
				{
					opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
						new string[] 
						{
							"application/octet-stream"
						}
					);
				}
			);
		}

		public static void UseCors(WebApplication app)
		{
			app.UseCors(CorsPolicyName);
		}

		public static void UseResponseCompressionForSignalR(WebApplication app)
		{
			app.UseResponseCompression();
		}
		#endregion
	}
}
