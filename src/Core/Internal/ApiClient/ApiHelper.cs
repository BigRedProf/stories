using BigRedProf.Data.Core;
using BigRedProf.Stories.Models;
using Microsoft.Extensions.Logging;
using System.IO;
using System;

namespace BigRedProf.Stories.Internal.ApiClient
{
	internal class ApiHelper
	{
		#region fields
		private readonly ILogger<Stories.ApiClient> _logger;
		private readonly PackRat<StoryThing> _storyThingPackRat;
		#endregion

		#region constructors
		public ApiHelper(ILogger<Stories.ApiClient> logger, IPiedPiper piedPiper)
		{
			_logger = logger;

			_storyThingPackRat = piedPiper.GetPackRat<StoryThing>(StoriesSchemaId.StoryThing);
		}
		#endregion

		#region methods
		public StoryThing GetStoryThingFromByteArray(byte[] byteArray)
		{
			_logger.LogDebug("Enter ApiStoryListener.GetStoryThingFromByteArray. ByteArrayLen={ByteArrayLen}", byteArray.Length);
			string base64ByteArray = Convert.ToBase64String(byteArray);
			_logger.LogTrace("Base64ByteArray={Base64ByteArray}", base64ByteArray);

			StoryThing? thing = null;
			try
			{
				MemoryStream memoryStream = new MemoryStream(byteArray);
				using (CodeReader reader = new CodeReader(memoryStream))
				{
					_logger.LogDebug("Calling UnpackModel");
					thing = _storyThingPackRat.UnpackModel(reader);
					_logger.LogDebug("Called UnpackModel. Offset={Offset},Thing={Thing}", thing.Offset, thing.Thing);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to unpack model");
			}
			finally
			{
				_logger.LogDebug("finally");
			}
			_logger.LogDebug("Exit ApiStoryListener.GetStoryThingFromByteArray.");

			return thing!;
		}
		#endregion
	}
}
