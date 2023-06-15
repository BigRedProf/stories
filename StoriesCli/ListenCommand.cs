using BigRedProf.Data;
using BigRedProf.Stories.Internal;
using BigRedProf.Stories.Internal.ApiClient;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace BigRedProf.Stories.StoriesCli
{
    public class ListenCommand : Command
	{
		#region fields
		private IPiedPiper? _piedPiper;
		private IStoryListener? _storyListener;
		private ThingFormat _thingFormat;
		private ModelFormat _modelFormat;
		#endregion

		#region Command methods
		public override int Run(CommandLineOptions options)
		{
			_piedPiper = new PiedPiper();
			_piedPiper.RegisterDefaultPackRats();
			if(options.ModelAssemblies != null)
			{
				foreach (string assemblyPath in options.ModelAssemblies)
				{
					Assembly modelAssembly = Assembly.LoadFrom(assemblyPath);
					_piedPiper.RegisterPackRats(modelAssembly);
				}
			}

			_thingFormat = options.ThingFormat ?? ThingFormat.RawCode;
			_modelFormat = options.ModelFormat ?? ModelFormat.ToString;

			long bookmark = options.Bookmark == null ? 0 : options.Bookmark.Value;

			ApiClient apiClient = new ApiClient(options.BaseUri!, _piedPiper);
			_storyListener = apiClient.GetStoryListener(options.Story!, bookmark);
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
			Console.Write(e.Offset);
			Console.Write(": ");
			
			string formattedThing = FormatThing(e.Thing);
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
					ModelWithSchema modelWithSchema = _piedPiper.DecodeModelWithSchema(thing);
					object model = modelWithSchema.Model;
					formattedThing = FormatModel(model);
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
			StringBuilder stringBuilder = new StringBuilder();

			stringBuilder.Append(model.GetType().Name);
			stringBuilder.Append('(');

			IList<FieldInfo> fields = model.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
			for(int i = 0; i < fields.Count; ++i)
			{
				if(i != 0)
					stringBuilder.Append(", ");

				FieldInfo field = fields[i];
				stringBuilder.Append(field.Name);
				stringBuilder.Append('=');
				stringBuilder.Append(field.GetValue(model));
			}

			IList<PropertyInfo> properties = model.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
			for(int i = 0; i < properties.Count; ++i)
			{
				if (i != 0)
					stringBuilder.Append(", ");

				PropertyInfo property = properties[i];
				stringBuilder.Append(property.Name);
				stringBuilder.Append('=');
				stringBuilder.Append(property.GetValue(model));
			}

			stringBuilder.Append(")");

			return stringBuilder.ToString();
		}
		#endregion
	}
}
