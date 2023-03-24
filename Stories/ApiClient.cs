﻿using BigRedProf.Data;
using BigRedProf.Stories.Internal.ApiClient;

namespace BigRedProf.Stories
{
	public class ApiClient
	{
		#region fields
		private Uri _baseUri;
		private IPiedPiper _piedPiper;
		#endregion

		#region constructors
		public ApiClient(Uri baseUri, IPiedPiper piedPiper)
		{
			if(baseUri == null)
				throw new ArgumentNullException(nameof(baseUri));

			if(piedPiper == null)
				throw new ArgumentNullException(nameof(piedPiper));

			_baseUri = baseUri;
			_piedPiper = piedPiper;
		}
		#endregion

		#region methods
		public IScribe GetScribe(StoryId storyId)
		{
			if (storyId == null) 
				throw new ArgumentNullException(nameof(storyId));

			return new ApiScribe(_baseUri, storyId, _piedPiper);
		}

		public IStoryteller GetStoryteller(StoryId storyId, long bookmark)
		{
			if (storyId == null)
				throw new ArgumentNullException(nameof(storyId));

			return new ApiStoryteller(_baseUri, storyId, _piedPiper, bookmark);
		}
		#endregion
	}
}