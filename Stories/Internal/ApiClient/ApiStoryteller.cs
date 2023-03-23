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
				HttpClient client = new HttpClient();
				Uri uri = new Uri(_baseUri, $"{_storyId}/Storyteller/HasSomethingForMe");

				bool hasSomethingForMe = client.GetFromJsonAsync<bool>(uri).Result;

				return hasSomethingForMe;
			}
		}
		#endregion

		#region IStoryteller methods
		public void SetBookmark(long bookmark)
		{
			if(bookmark < 0)
				throw new ArgumentOutOfRangeException(nameof(bookmark), "Bookmark must be zero or greater.");

			_bookmark = bookmark;
		}

		public Code TellMeSomething()
		{
			HttpClient client = new HttpClient();
			Uri uri = new Uri(_baseUri, $"{_storyId}/Storyteller/TellMeSomething/{_bookmark}");

			byte[] byteArray = client.GetByteArrayAsync(uri).Result;

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
