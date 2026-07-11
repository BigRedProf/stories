using BigRedProf.Data.Core;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BigRedProf.Stories.Api.Internal
{
	internal sealed class TextTrailModelBinder : IModelBinder
	{
		#region IModelBinder methods
		public Task BindModelAsync(ModelBindingContext bindingContext)
		{
			if (bindingContext == null)
				throw new ArgumentNullException(nameof(bindingContext));

			ValueProviderResult valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
			if (valueProviderResult == ValueProviderResult.None)
			{
				bindingContext.Result = ModelBindingResult.Failed();
				return Task.CompletedTask;
			}

			string? value = valueProviderResult.FirstValue;
			if (string.IsNullOrWhiteSpace(value))
			{
				bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "Story ID must not be null or whitespace.");
				bindingContext.Result = ModelBindingResult.Failed();
				return Task.CompletedTask;
			}

			try
			{
				TextTrail storyId = TextTrailSerializer.ParseRouteValue(value);
				bindingContext.Result = ModelBindingResult.Success(storyId);
			}
			catch (Exception ex)
			{
				bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, ex.Message);
				bindingContext.Result = ModelBindingResult.Failed();
			}

			return Task.CompletedTask;
		}
		#endregion
	}
}
