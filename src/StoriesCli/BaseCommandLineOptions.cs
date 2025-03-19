using System;
using System.ComponentModel.DataAnnotations;
using CommandLine;
using Microsoft.Extensions.Logging;

namespace BigRedProf.Stories.StoriesCli
{
	public class BaseCommandLineOptions
	{
		[Value(0, MetaName = "Command", Required = false, HelpText = "The stories command to run.")]
		public string? Command { get; set; }

		[Option("logLevel", Required = false, HelpText = "The SignalR log level")]
		public LogLevel? LogLevel { get; set; }

		[Option('u', "baseUri", Required = false, HelpText = "The base URI of the stories service.")]
		public Uri BaseUri { get; set; } = default!;

		[Option('s', "story", Required = false, HelpText = "The story identifier.")]
		public string Story { get; set; } = default!;
	}
}
