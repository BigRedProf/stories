using BigRedProf.Data.Core;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BigRedProf.Stories.Api.Internal
{
	internal sealed class TextTrailModelBinderProvider : IModelBinderProvider
	{
		#region IModelBinderProvider methods
		public IModelBinder? GetBinder(ModelBinderProviderContext context)
		{
			if (context == null)
				throw new ArgumentNullException(nameof(context));

			IModelBinder? binder = null;
			if (context.Metadata.ModelType == typeof(TextTrail))
				binder = new TextTrailModelBinder();

			return binder;
		}
		#endregion
	}
}
