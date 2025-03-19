using System;
using System.ComponentModel.DataAnnotations;
using CommandLine;
using Microsoft.Extensions.Logging;

namespace BigRedProf.Stories.StoriesCli
{
    [Verb("listen", HelpText = "Listen to things from the story server.")]
    public class ListenOptions : BaseCommandLineOptions
	{
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
