using BigRedProf.Data;
using System.Diagnostics;
using System.Net;
using System.Web;

namespace BigRedProf.Stories.Internal.ApiClient
{
	internal class ApiScribe : IScribe
	{
		#region fields
		private Uri _baseUri;
		private StoryId _storyId;
		private IPiedPiper _piedPiper;
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
		}
		#endregion

		#region IScribe methods
		public void RecordSomething(Code something)
		{
			Task task = RecordSomethingAsync(something);
			task.Wait();
		}

        public async Task RecordSomethingAsync(Code something)
		{
			Uri uri = new Uri(_baseUri, $"{HttpUtility.UrlEncode(_storyId)}/Scribe/RecordSomething");

			PackRat<Code> packRate = _piedPiper.GetPackRat<Code>(SchemaId.Code);
			MemoryStream memoryStream = new MemoryStream();
			using(CodeWriter writer = new CodeWriter(memoryStream)) 
			{
				packRate.PackModel(writer, something);
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
