using System;
using System.ComponentModel.DataAnnotations;
using CommandLine;
using Microsoft.Extensions.Logging;

namespace BigRedProf.Stories.StoriesCli
{
	public class CommandLineOptions
	{
		[Value(0, MetaName = "Command", Required = true, HelpText = "The stories command to run.")]
		public string? Command { get; set; }

		[Option("logLevel", Required = false, HelpText = "The SignalR log level")]
		public LogLevel? LogLevel { get; set; }

		[Option('u', "BaseUri", Required = true, HelpText = "The base URI of the stories service.")]
		public Uri? BaseUri { get; set; }

		[Option('s', "story", Required = true, HelpText = "The story identifier.")]
		public string? Story { get; set; }

		[Option('b', "bookmark", Required = false, HelpText = "The first bookmark to start listening at.")]
		public long? Bookmark { get; set; }

		[Option("modelAssemblies", Required = false, Separator = ',', HelpText = "The path to a model assembly.")]
		public IEnumerable<string>? ModelAssemblies { get; set; }

		[Option("thingFormat", Required = false, HelpText = "How to format things. Choices are: RawCode or ModelWithSchema.")]
		public ThingFormat? ThingFormat { get; set; }

		[Option("modelFormat", Required = false, HelpText = "How to format models. Choices are: ToString or Reflection.")]
		public ModelFormat? ModelFormat { get; set; }
	}
}
