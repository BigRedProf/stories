using BigRedProf.Data.Core;
using BigRedProf.Stories.Internal.ApiClient;
using BigRedProf.Stories.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace BigRedProf.Stories.PowerShell
{
	/// <summary>
	/// Reads a range of things from a story and returns them as objects.
	/// </summary>
	/// <remarks>
	/// This RETURNS. The existing CLI's `listen` verb ends in an unbounded sleep loop,
	/// so every read is a tail you have to interrupt; every question worth asking of a
	/// story ("everything this agent did", "offsets 200-250") wants to finish. Tailing
	/// gets its own verb rather than a switch on this one.
	///
	/// Nothing here knows what a digihouse event is, and nothing here should. Stories
	/// stores opinion-free codes; the caller names the schema and supplies the pack
	/// rats. See stories#5.
	/// </remarks>
	[Cmdlet(VerbsCommon.Get, "Story")]
	[OutputType(typeof(StoryThingInfo))]
	public sealed class GetStoryCommand : PSCmdlet
	{
		#region constants
		// The service fetches in batches; this is the batch size, not a result limit.
		private const long DefaultBatchSize = 1000;
		#endregion

		#region parameters
		/// <summary>
		/// The story to read, e.g. bigredprof/digihouse/catalog.
		/// </summary>
		[Parameter(Mandatory = true, Position = 0)]
		public string StoryId { get; set; } = default!;

		/// <summary>
		/// The base URI of the stories service.
		/// </summary>
		[Parameter(Mandatory = true)]
		public Uri BaseUri { get; set; } = default!;

		/// <summary>
		/// The offset to start reading from.
		/// </summary>
		[Parameter]
		public long Bookmark { get; set; }

		/// <summary>
		/// Stop after this many things. Zero, the default, reads to the end of the story.
		/// </summary>
		[Parameter]
		public long First { get; set; }

		/// <summary>
		/// The schema every thing in this story is packed as.
		/// </summary>
		/// <remarks>
		/// Apps that wrap each thing in an envelope of their own record things this way.
		/// Omit it and things come back as raw codes, which is the honest default: only
		/// the caller knows what its own story contains.
		/// </remarks>
		[Parameter]
		public string? ThingSchemaId { get; set; }

		/// <summary>
		/// Assemblies carrying the pack rats, tokenizers, and trait definitions needed
		/// to decode the things in this story.
		/// </summary>
		[Parameter]
		public string[]? ModelAssembly { get; set; }
		#endregion

		#region PSCmdlet methods
		protected override void ProcessRecord()
		{
			IPiedPiper piedPiper = BuildPiedPiper();

			TextTrail storyId;
			try
			{
				storyId = TextTrailSerializer.ParseTextRepresentation(StoryId);
			}
			catch (Exception exception)
			{
				ThrowTerminatingError(new ErrorRecord(
					exception, "InvalidStoryId", ErrorCategory.InvalidArgument, StoryId));
				return;
			}

			ApiClient apiClient = new ApiClient(
				BaseUri, piedPiper, NullLogger<ApiClient>.Instance, null);
			IStoryteller storyteller = apiClient.GetStoryteller(storyId, Bookmark, DefaultBatchSize);

			long told = 0;
			while (!Stopping)
			{
				if (First > 0 && told >= First)
					break;

				// Escapes whatever synchronization context the host is running under.
				// IStoryteller's synchronous members block on the async ones internally,
				// which is the classic way to deadlock inside a PowerShell runspace.
				if (!RunWithoutContext(() => storyteller.HasSomethingForMeAsync()))
					break;

				StoryThing thing = RunWithoutContext(() => storyteller.TellMeSomethingAsync());

				WriteObject(new StoryThingInfo()
				{
					Offset = thing.Offset,
					Thing = thing.Thing,
					Model = DecodeModel(piedPiper, thing.Thing)
				});

				++told;
			}
		}
		#endregion

		#region private methods
		private IPiedPiper BuildPiedPiper()
		{
			IPiedPiper piedPiper = new PiedPiper();
			piedPiper.RegisterCorePackRats();
			piedPiper.RegisterPackRats(typeof(StoryThing).Assembly);

			if (ModelAssembly == null)
				return piedPiper;

			foreach (string path in ModelAssembly)
			{
				// Resolved against the caller's location, not the process's. A cmdlet's
				// working directory is not PowerShell's current directory, so a relative
				// path would otherwise land somewhere the user never chose.
				string fullPath = GetUnresolvedProviderPathFromPSPath(path);
				if (!File.Exists(fullPath))
				{
					ThrowTerminatingError(new ErrorRecord(
						new FileNotFoundException($"Model assembly not found: {fullPath}", fullPath),
						"ModelAssemblyNotFound", ErrorCategory.ObjectNotFound, path));
				}

				AssemblyLoadContext context = InstallDependencyResolver();
				AddProbeDirectory(Path.GetDirectoryName(fullPath));

				Assembly assembly = context.LoadFromAssemblyPath(fullPath);
				piedPiper.RegisterPackRats(assembly);

				// Tokenized models pack under their tokenizer id, so an assembly that
				// uses one is undecodable without this.
				piedPiper.RegisterTokenizers(assembly);
			}

			return piedPiper;
		}

		private object? DecodeModel(IPiedPiper piedPiper, Code thing)
		{
			if (string.IsNullOrEmpty(ThingSchemaId))
				return null;

			try
			{
				// The weakly typed path, because the schema is named at run time and no
				// generated pack rat is a PackRat<object>.
				using (CodeReader reader = new CodeReader(new MemoryStream(thing.ToByteArray())))
				{
					return piedPiper.UnpackModel(reader, ThingSchemaId!);
				}
			}
			catch (Exception exception)
			{
				// A thing this host cannot decode is normal -- a story written by a newer
				// build carries schemas this assembly has never heard of. Report it and
				// keep reading; the raw code still comes back on the object.
				WriteWarning($"Could not decode the thing at offset: {exception.Message}");
				return null;
			}
		}

		private static T RunWithoutContext<T>(Func<Task<T>> function)
		{
			return Task.Run(function).GetAwaiter().GetResult();
		}

		/// <summary>
		/// Teaches the load context where to find the dependencies of a model assembly
		/// we hand it at run time, and returns that context.
		/// </summary>
		/// <remarks>
		/// This is the thing a binary module cannot skip, and it fails in a misleading
		/// place if you do.
		///
		/// PowerShell loads a binary module into the DEFAULT load context, and the
		/// default context probes the host's directory -- pwsh's -- not the module's. So
		/// a model assembly loaded here asks for BigRedProf.Data.Core, nothing probes the
		/// folder where that assembly actually sits, and the failure surfaces as
		/// ReflectionTypeLoadException from GetTypes() during pack rat registration,
		/// naming the exact VERSION the model assembly was compiled against. That version
		/// is a red herring: loading by path ignores version, so a newer Data.Core
		/// happily satisfies a model assembly built against an older one -- which is the
		/// normal case, since a story outlives the build that wrote it.
		/// </remarks>
		private static AssemblyLoadContext InstallDependencyResolver()
		{
			lock (_probeLock)
			{
				if (_resolverContext != null)
					return _resolverContext;

				AssemblyLoadContext context =
					AssemblyLoadContext.GetLoadContext(typeof(GetStoryCommand).Assembly)
					?? AssemblyLoadContext.Default;

				context.Resolving += ResolveFromProbeDirectories;
				_resolverContext = context;

				// The module's own folder, where its dependencies were laid down beside it.
				_probeDirectories.Add(Path.GetDirectoryName(typeof(GetStoryCommand).Assembly.Location)!);

				return context;
			}
		}

		private static void AddProbeDirectory(string? directory)
		{
			if (string.IsNullOrEmpty(directory))
				return;

			lock (_probeLock)
			{
				_probeDirectories.Add(directory!);
			}
		}

		private static Assembly? ResolveFromProbeDirectories(AssemblyLoadContext context, AssemblyName name)
		{
			List<string> directories;
			lock (_probeLock)
			{
				directories = new List<string>(_probeDirectories);
			}

			foreach (string directory in directories)
			{
				string candidate = Path.Combine(directory, name.Name + ".dll");
				if (File.Exists(candidate))
					return context.LoadFromAssemblyPath(candidate);
			}

			return null;
		}
		#endregion

		#region static fields
		private static readonly object _probeLock = new object();
		private static readonly HashSet<string> _probeDirectories =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private static AssemblyLoadContext? _resolverContext;
		#endregion
	}
}
