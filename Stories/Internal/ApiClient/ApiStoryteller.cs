using BigRedProf.Data;
using System.Diagnostics;
using System.Net.Http.Json;

namespace BigRedProf.Stories.Internal.ApiClient
{
	internal class ApiStoryteller : IStoryteller
	{
		#region IStoryteller events
#pragma warning disable CS0067 // The event 'ApiStoryteller.GotSomethingForYou' is never used
		public event EventHandler? GotSomethingForYou;
#pragma warning restore CS0067 // The event 'ApiStoryteller.GotSomethingForYou' is never used
		#endregion

		#region fields
		private Uri _baseUri;
		private StoryId _storyId;
		private IPiedPiper _piedPiper;
		private long _bookmark;
		#endregion

		#region constructors
		public ApiStoryteller(Uri baseUri, StoryId storyId, IPiedPiper piedPiper, long bookmark)
		{
			Debug.Assert(baseUri != null);
			Debug.Assert(storyId != null);
			Debug.Assert(piedPiper != null);

			_baseUri = baseUri;
			_storyId = storyId;
			_piedPiper = piedPiper;
			_bookmark = bookmark;
		}
		#endregion

		#region IStoryteller properties
		public long Bookmark
		{
			get
			{
				return _bookmark;
			}
		}

		public bool HasSomethingForMe
		{
			get
			{
				return HasSomethingForMeAsync().Result;
			}
		}
        #endregion

        #region IStoryteller methods
        public async Task<bool> HasSomethingForMeAsync()
        {
            HttpClient client = new HttpClient();
            Uri uri = new Uri(_baseUri, $"{_storyId}/Storyteller/HasSomethingForMe/{_bookmark}");

            bool hasSomethingForMe = await client.GetFromJsonAsync<bool>(uri);

            return hasSomethingForMe;
        }

        public void SetBookmark(long bookmark)
		{
			if(bookmark < 0)
				throw new ArgumentOutOfRangeException(nameof(bookmark), "Bookmark must be zero or greater.");

			_bookmark = bookmark;
		}

		public Code TellMeSomething()
		{
			return TellMeSomethingAsync().Result;
		}

		public async Task<Code> TellMeSomethingAsync()
		{
			HttpClient client = new HttpClient();
			Uri uri = new Uri(_baseUri, $"{_storyId}/Storyteller/TellMeSomething/{_bookmark}");

			byte[] byteArray = await client.GetByteArrayAsync(uri);

			Code code;
			PackRat<Code> packRat = _piedPiper.GetPackRat<Code>(SchemaId.Code);
			MemoryStream memoryStream = new MemoryStream(byteArray);
			using (CodeReader reader = new CodeReader(memoryStream))
			{
				 code = packRat.UnpackModel(reader);
			}

			return code;
		}
		#endregion
	}
}
