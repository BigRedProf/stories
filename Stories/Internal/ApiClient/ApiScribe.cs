using BigRedProf.Data;
using BigRedProf.Stories.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace BigRedProf.Stories.Internal.ApiClient
{
	internal class ApiScribe : IScribe
	{
		#region fields
		private Uri _baseUri;
		private StoryId _storyId;
		private IPiedPiper _piedPiper;
		private PackRat<ListOfThings> _listOfThingsPackRat;
		#endregion

		#region constructors
		public ApiScribe(Uri baseUri, StoryId storyId, IPiedPiper piedPiper)
		{
			Debug.Assert(baseUri != null);
			Debug.Assert(storyId != null);
			Debug.Assert(piedPiper != null);

			_baseUri = baseUri;
			_storyId = storyId;
			_piedPiper = piedPiper;

			_listOfThingsPackRat = _piedPiper.GetPackRat<ListOfThings>(StoriesSchemaId.ListOfThings);
		}
		#endregion

		#region IScribe methods
		public void RecordSomething(params Code[] things)
		{
			Task task = RecordSomethingAsync(things);
			task.Wait();
		}

        public async Task RecordSomethingAsync(params Code[] things)
		{
			Uri uri = new Uri(_baseUri, $"v1/{HttpUtility.UrlEncode(_storyId)}/Scribe/RecordSomething");

			ListOfThings listOfThings = new ListOfThings()
			{
				Things = things
			};

			MemoryStream memoryStream = new MemoryStream();
			using(CodeWriter writer = new CodeWriter(memoryStream)) 
			{
				_listOfThingsPackRat.PackModel(writer, listOfThings);
			}
			HttpContent content = new ByteArrayContent(memoryStream.ToArray());

			HttpClient client = new HttpClient();
			using (HttpResponseMessage message = await client.PostAsync(uri, content))
			{
				if (message.StatusCode != HttpStatusCode.OK)
					throw new HttpRequestException($"{message.StatusCode}: {message.Content.ToString()}");
			}
		}
		#endregion
	}
}
