using BigRedProf.Data;
using BigRedProf.Stories.Memory;
using BigRedProf.Stories.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using System.Web;

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
		private readonly long? _tellLimit;
		private readonly PackRat<ListOfStoryThings> _listOfStoryThingsPackRat;
		private long _currentBatchOffset;
		private long _currentBatchLength;
		private MemoryStoryteller _currentBatchStoryteller;
		#endregion

		#region constructors
		public ApiStoryteller(Uri baseUri, StoryId storyId, IPiedPiper piedPiper, long bookmark, long? tellLimit)
		{
			Debug.Assert(baseUri != null);
			Debug.Assert(storyId != null);
			Debug.Assert(piedPiper != null);

			_baseUri = baseUri;
			_storyId = storyId;
			_piedPiper = piedPiper;
			_bookmark = bookmark;
			_tellLimit = tellLimit;

			_listOfStoryThingsPackRat = _piedPiper.GetPackRat<ListOfStoryThings>(StoriesSchemaId.ListOfStoryThings);
			_currentBatchOffset = 0;
			_currentBatchLength = 0;
			_currentBatchStoryteller = new MemoryStoryteller(new StoryThing[0]);
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
			if (await _currentBatchStoryteller.HasSomethingForMeAsync())
				return true;

            HttpClient client = new HttpClient();
            Uri uri = new Uri(_baseUri, $"v1/{HttpUtility.UrlEncode(_storyId)}/Storyteller/HasSomethingForMe/{_bookmark}");

            bool hasSomethingForMe = await client.GetFromJsonAsync<bool>(uri);

            return hasSomethingForMe;
        }

        public void SetBookmark(long bookmark)
		{
			if(bookmark < 0)
				throw new ArgumentOutOfRangeException(nameof(bookmark), "Bookmark must be zero or greater.");

			_bookmark = bookmark;
		}

		public StoryThing TellMeSomething()
		{
			return TellMeSomethingAsync().Result;
		}

		public async Task<StoryThing> TellMeSomethingAsync()
		{
			StoryThing thing;

			// First, check if it's in our current batch of story things.
			if(_bookmark >= _currentBatchOffset && _bookmark < _currentBatchOffset + _currentBatchLength)
			{
				_currentBatchStoryteller.SetBookmark(_bookmark - _currentBatchOffset);
				thing = await _currentBatchStoryteller.TellMeSomethingAsync();
				++_bookmark;
				
				return thing;
			}

			// If not, retrieve it from the stories service.
			HttpClient client = new HttpClient();
			Uri uri = GetTellMeSomethingUri(_bookmark);

			byte[] byteArray = await client.GetByteArrayAsync(uri);

			ListOfStoryThings listOfStoryThings = new ListOfStoryThings();
			MemoryStream memoryStream = new MemoryStream(byteArray);
			using (CodeReader reader = new CodeReader(memoryStream))
			{
				 listOfStoryThings = _listOfStoryThingsPackRat.UnpackModel(reader);
			}

			if (listOfStoryThings.StoryThings.Count < 1)
				throw new InvalidOperationException("Less than 1 story thing returned.");

			_currentBatchOffset = _bookmark;
			_currentBatchLength = listOfStoryThings.StoryThings.Count;
			_currentBatchStoryteller = new MemoryStoryteller(listOfStoryThings.StoryThings);

			thing = await _currentBatchStoryteller.TellMeSomethingAsync();
			++_bookmark;

			return thing;
		}
		#endregion

		#region private methods
		private Uri GetTellMeSomethingUri(long bookmark)
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append($"v1/{HttpUtility.UrlEncode(_storyId)}/Storyteller/TellMeSomething/{_bookmark}");
			if(_tellLimit.HasValue)
				stringBuilder.Append($"?limit={_tellLimit.Value}");

			return new Uri(_baseUri, stringBuilder.ToString());
		}
		#endregion
	}
}
