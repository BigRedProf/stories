using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using System;

namespace BigRedProf.Stories.Logging
{
	public static class StoryLoggerExtensions
	{
		#region functions
		public static ILoggingBuilder AddStoryLogger(this ILoggingBuilder builder)
		{
			builder.AddConfiguration();

			builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, StoryLoggerProvider>());

			LoggerProviderOptions.RegisterProviderOptions<StoryLoggerConfiguration, StoryLoggerProvider>(builder.Services);

			return builder;
		}

		public static ILoggingBuilder AddStoryLogger(this ILoggingBuilder builder, Action<StoryLoggerConfiguration> configure)
		{
			builder.AddStoryLogger();
			builder.Services.Configure(configure);

			return builder;
		}
		#endregion
	}
}
