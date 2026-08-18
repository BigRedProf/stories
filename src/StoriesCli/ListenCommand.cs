using BigRedProf.Data.Core;
using BigRedProf.Stories.Internal;
using BigRedProf.Stories.Internal.ApiClient;
using BigRedProf.Stories.Logging.Models;
using BigRedProf.Stories.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace BigRedProf.Stories.StoriesCli
{
    public class ListenCommand : Command
	{
		#region constants
		private const int MaxReflectionDepth = 12;
		#endregion

		#region fields
		private readonly ILogger<ApiClient> _apiClientLogger;
		private IPiedPiper? _piedPiper;
		private IStoryListener? _storyListener;
		private ThingFormat _thingFormat;
		private string? _thingSchemaId;
		private ModelFormat _modelFormat;
		#endregion

		#region constructors
		public ListenCommand(ILogger<ApiClient> apiClientLogger)
		{
			_apiClientLogger = apiClientLogger;
		}
		#endregion

		#region Command methods
		public override int Run(BaseCommandLineOptions commandLineOptions)
		{
			ListenOptions options = (ListenOptions)commandLineOptions;

			_piedPiper = new PiedPiper();
			_piedPiper.RegisterCorePackRats();
			_piedPiper.RegisterPackRats(typeof(StoryThing).Assembly);
			_piedPiper.RegisterPackRats(typeof(LogEntry).Assembly);
			if(options.ModelAssemblies != null)
			{
				foreach (string assemblyPath in options.ModelAssemblies)
				{
					Assembly modelAssembly = Assembly.LoadFrom(assemblyPath);
					_piedPiper.RegisterPackRats(modelAssembly);
					// Tokenized models pack under their tokenizer id, so a model assembly
					// that uses one is undecodable without this.
					_piedPiper.RegisterTokenizers(modelAssembly);
				}
			}

			_thingFormat = options.ThingFormat ?? ThingFormat.RawCode;
			_thingSchemaId = options.ThingSchemaId;
			_modelFormat = options.ModelFormat ?? ModelFormat.ToString;

			if (_thingFormat == ThingFormat.Model && string.IsNullOrEmpty(_thingSchemaId))
			{
				Console.Error.WriteLine("--thingSchemaId is required when --thingFormat is Model.");
				return 1;
			}

			long bookmark = options.Bookmark == null ? 0 : options.Bookmark.Value;

			Action<ILoggingBuilder>? signalRLoggingBuilderCallback = null;
			if(options.LogLevel != null)
			{
				signalRLoggingBuilderCallback = (ILoggingBuilder loggingBuilder) =>
				{
					loggingBuilder.SetMinimumLevel(options.LogLevel.Value);
					loggingBuilder.AddFilter("Microsoft.AspNetCore.SignalR", options.LogLevel.Value);
					loggingBuilder.AddFilter("Microsoft.AspNetCore.Http.Connections", options.LogLevel.Value);
					loggingBuilder.AddConsole();
				};
			}
			ApiClient apiClient = new ApiClient(options.BaseUri!, _piedPiper, _apiClientLogger, signalRLoggingBuilderCallback);
			TextTrail storyId = TextTrailSerializer.ParseTextRepresentation(options.StoryId!);
			_storyListener = apiClient.GetStoryListener(
				1000,
				TimeSpan.FromSeconds(5),
				storyId,
				bookmark
			);
			_storyListener.SomethingHappenedAsync += StoryListener_SomethingHappenedAsync;
			_storyListener.StartListening();

			while (true)
				Thread.Sleep(TimeSpan.FromSeconds(3));
		}

		protected override void OnCancelKeyPress()
		{
			if (_storyListener == null)
				return;

			_storyListener.SomethingHappenedAsync -= StoryListener_SomethingHappenedAsync;
			_storyListener.StopListening();
		}
		#endregion

		#region event handlers
		private Task StoryListener_SomethingHappenedAsync(object? sender, Events.SomethingHappenedEventArgs e)
		{
			Console.Write(e.Thing.Offset);
			Console.Write(": ");
			
			string formattedThing = FormatThing(e.Thing.Thing);
			Console.WriteLine(formattedThing);

			return Task.CompletedTask;
		}
		#endregion

		#region private methods
		private string FormatThing(Code thing)
		{
			Debug.Assert(_piedPiper != null);

			string formattedThing;
			switch (_thingFormat)
			{
				case ThingFormat.RawCode:
					formattedThing = thing.ToString();
					break;
				case ThingFormat.ModelWithSchema:
					ModelWithSchema modelWithSchema = _piedPiper.DecodeModel<ModelWithSchema>(thing, CoreSchema.ModelWithSchema);
					object model = modelWithSchema.Model;
					formattedThing = FormatModel(model);
					break;
				case ThingFormat.Model:
					Debug.Assert(!string.IsNullOrEmpty(_thingSchemaId));
					// Not DecodeModel<object>: that resolves the pack rat as PackRat<object>
					// and no generated pack rat is one. The weakly typed path is the only
					// one that can decode a schema named at run time.
					object thingModel;
					using (CodeReader codeReader = new CodeReader(new MemoryStream(thing.ToByteArray())))
					{
						thingModel = _piedPiper.UnpackModel(codeReader, _thingSchemaId);
					}
					formattedThing = FormatModel(thingModel);
					break;
				default:
					throw new NotImplementedException($"Thing format {_thingFormat} is not implemented.");
			}

			return formattedThing;
		}

		private string FormatModel(object model)
		{
			string formattedModel;
			switch(_modelFormat)
			{
				case ModelFormat.ToString:
					formattedModel = model.ToString() ?? "null";
					break;
				case ModelFormat.Reflection:
					formattedModel = FormatModelUsingReflection(model);
					break;
				default:
					throw new NotImplementedException($"Model format {_modelFormat} is not implemented.");
			}

			Debug.Assert(formattedModel != null);
			return formattedModel;
		}

		private string FormatModelUsingReflection(object model)
		{
			return FormatValueUsingReflection(model, MaxReflectionDepth);
		}

		private string FormatValueUsingReflection(object? value, int depthRemaining)
		{
			if (value == null)
				return "(null)";

			if (value.GetType().IsPrimitive)
				return value.ToString() ?? "(null)";

			if (value is string || value is Guid || value is decimal)
				return value.ToString() ?? "(null)";

			// A type that says how to print itself gets to. Without this, reflecting into
			// Code (which exposes itself through its own members) recurses forever.
			if (OverridesToString(value.GetType()))
				return value.ToString() ?? "(null)";

			if (depthRemaining == 0)
				return "...";

			if (value is System.Collections.IEnumerable enumerable)
				return FormatEnumerableUsingReflection(enumerable, depthRemaining);

			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append(value.GetType().Name);
			stringBuilder.Append('(');

			bool isFirstMember = true;

			foreach (FieldInfo field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
			{
				if (!isFirstMember)
					stringBuilder.Append(", ");
				isFirstMember = false;

				stringBuilder.Append(field.Name);
				stringBuilder.Append('=');
				stringBuilder.Append(FormatValueUsingReflection(field.GetValue(value), depthRemaining - 1));
			}

			// Indexers are properties too, and asking one for its value without arguments throws.
			foreach (PropertyInfo property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
			{
				if (!property.CanRead || property.GetIndexParameters().Length != 0)
					continue;

				if (!isFirstMember)
					stringBuilder.Append(", ");
				isFirstMember = false;

				stringBuilder.Append(property.Name);
				stringBuilder.Append('=');
				stringBuilder.Append(FormatValueUsingReflection(property.GetValue(value), depthRemaining - 1));
			}

			stringBuilder.Append(')');

			return stringBuilder.ToString();
		}

		private string FormatEnumerableUsingReflection(System.Collections.IEnumerable enumerable, int depthRemaining)
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append('[');

			bool isFirstItem = true;
			foreach (object? item in enumerable)
			{
				if (!isFirstItem)
					stringBuilder.Append(", ");
				isFirstItem = false;

				stringBuilder.Append(FormatValueUsingReflection(item, depthRemaining - 1));
			}

			stringBuilder.Append(']');

			return stringBuilder.ToString();
		}

		private static bool OverridesToString(Type type)
		{
			MethodInfo? toString = type.GetMethod(nameof(ToString), Type.EmptyTypes);
			return toString != null && toString.DeclaringType != typeof(object);
		}
		#endregion
	}
}
