using System;
using CommandLine;

namespace BigRedProf.Stories.StoriesCli
{
	public class CommandLineOptions
	{
		[Value(0, MetaName = "Command", Required = true, HelpText = "The stories command to run.")]
		public string? Command { get; set; }

		[Option('u', "BaseUri", Required = true, HelpText = "The base URI of the stories service.")]
		public Uri? BaseUri { get; set; }

		[Option('s', "story", Required = true, HelpText = "The story identifier.")]
		public string? Story { get; set; }

		[Option('b', "bookmark", Required = false, HelpText = "The first bookmark to start listening at.")]
		public long? Bookmark { get; set; }
	}
}
