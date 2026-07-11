using BigRedProf.Data.Core;
using BigRedProf.Stories;
using BigRedProf.Stories.Memory;
using BigRedProf.Stories.Models;
using System;

namespace BigRedProf.Stories.StoriesCli.Test
{
	public class TextTrailStoryTests
	{
		#region TextTrailSerializer tests
		[Fact]
		public void ParseTextRepresentation_ShouldCreateTextTrailFromSlashSeparatedString()
		{
			TextTrail textTrail = TextTrailSerializer.ParseTextRepresentation("one/two/three");

			Assert.Equal(new string[] { "one", "two", "three" }, textTrail.Segments);
		}

		[Fact]
		public void ParseTextRepresentation_ShouldPreserveEscapedSeparators()
		{
			TextTrail textTrail = TextTrailSerializer.ParseTextRepresentation("one//two/three");

			Assert.Equal(new string[] { "one/two", "three" }, textTrail.Segments);
		}

		[Fact]
		public void ParseTextRepresentation_ShouldThrowIfStoryIdIsBlank()
		{
			Assert.Throws<ArgumentException>(() =>
			{
				TextTrailSerializer.ParseTextRepresentation(" ");
			});
		}

		[Fact]
		public void ToRouteValue_ShouldRoundTripStoryId()
		{
			TextTrail expectedStoryId = new TextTrail("human friendly", "slashes / ok", "symbols !@#$%^&*()");

			string routeValue = TextTrailSerializer.ToRouteValue(expectedStoryId);
			TextTrail actualStoryId = TextTrailSerializer.ParseRouteValue(routeValue);

			Assert.DoesNotContain("/", routeValue);
			Assert.Contains("%2F", routeValue);
			Assert.Equal(expectedStoryId.Segments, actualStoryId.Segments);
		}

		[Fact]
		public void ToMultihashString_ShouldReturnSameValueForEquivalentStoryIds()
		{
			TextTrail firstStoryId = new TextTrail("one", "two");
			TextTrail secondStoryId = new TextTrail("one", "two");

			string firstHash = TextTrailSerializer.ToMultihashString(firstStoryId);
			string secondHash = TextTrailSerializer.ToMultihashString(secondStoryId);

			Assert.Equal(firstHash, secondHash);
		}
		#endregion

		#region MemoryStoryManager tests
		[Fact]
		public void MemoryStoryManager_ShouldFindStoryForEquivalentTextTrail()
		{
			MemoryStoryManager storyManager = new MemoryStoryManager();
			TextTrail writerTrail = new TextTrail("one", "two");
			TextTrail readerTrail = new TextTrail("one", "two");
			Code expectedCode = new Code("10110011");

			IScribe scribe = storyManager.GetScribe(writerTrail);
			scribe.RecordSomething(expectedCode);

			IStoryteller storyteller = storyManager.GetStoryteller(readerTrail);
			StoryThing storyThing = storyteller.TellMeSomething();

			Assert.Equal(0, storyThing.Offset);
			Assert.Equal(expectedCode, storyThing.Thing);
		}

		[Fact]
		public void MemoryStoryManager_ShouldKeepDifferentTextTrailsSeparate()
		{
			MemoryStoryManager storyManager = new MemoryStoryManager();
			TextTrail writerTrail = new TextTrail("one", "two");
			TextTrail otherTrail = new TextTrail("one", "three");

			IScribe scribe = storyManager.GetScribe(writerTrail);
			scribe.RecordSomething(new Code("10110011"));

			IStoryteller storyteller = storyManager.GetStoryteller(otherTrail);

			Assert.False(storyteller.HasSomethingForMe);
		}
		#endregion
	}
}
