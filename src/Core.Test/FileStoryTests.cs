using BigRedProf.Data.Core;
using BigRedProf.Stories.Data;
using BigRedProf.Stories.Disk;
using BigRedProf.Stories.Memory;
using System.Text;

namespace BigRedProf.Stories.Core.Test
{
	public class FileStoryTests
	{
		#region fields
		private static readonly Code FirstThing = new Code("10110011");
		private static readonly Code SecondThing = new Code("00");
		private static readonly Code ThirdThing = new Code("10101");
		#endregion

		#region replay tests
		[Fact]
		public void Append_ThenReplayFromAFreshInstance_ShouldReturnTheSameThings()
		{
			string directory = CreateCleanDirectory("fresh-instance");
			try
			{
				TextTrail storyId = new TextTrail("catalog", "content");
				using (FileStoryManager writer = new FileStoryManager(directory, CreatePiedPiper()))
				{
					writer.GetScribe(storyId).RecordSomething(FirstThing, SecondThing);
				}

				using (FileStoryManager reader = new FileStoryManager(directory, CreatePiedPiper()))
				{
					FileStoryteller storyteller = reader.GetStoryteller(storyId);
					StoryThing first = storyteller.TellMeSomething();
					StoryThing second = storyteller.TellMeSomething();

					Assert.Equal(0L, first.Offset);
					Assert.Equal(FirstThing, first.Thing);
					Assert.Equal(1L, second.Offset);
					Assert.Equal(SecondThing, second.Thing);
					Assert.False(storyteller.HasSomethingForMe);
				}
			}
			finally
			{
				DeleteDirectory(directory);
			}
		}

		[Fact]
		public void RecordSomething_ShouldPreserveOrderAcrossBatches()
		{
			string directory = CreateCleanDirectory("order");
			try
			{
				TextTrail storyId = new TextTrail("catalog", "content");
				using (FileStoryManager manager = new FileStoryManager(directory, CreatePiedPiper()))
				{
					FileScribe scribe = manager.GetScribe(storyId);
					scribe.RecordSomething(FirstThing, SecondThing);
					scribe.RecordSomething(ThirdThing);

					FileStoryteller storyteller = manager.GetStoryteller(storyId);
					StoryThing first = storyteller.TellMeSomething();
					Assert.Equal(0L, first.Offset);
					Assert.Equal(FirstThing, first.Thing);
					storyteller.SetBookmark(1);
					StoryThing second = storyteller.TellMeSomething();
					StoryThing third = storyteller.TellMeSomething();

					Assert.Equal(1L, second.Offset);
					Assert.Equal(SecondThing, second.Thing);
					Assert.Equal(2L, third.Offset);
					Assert.Equal(ThirdThing, third.Thing);
					Assert.False(storyteller.HasSomethingForMe);
				}
			}
			finally
			{
				DeleteDirectory(directory);
			}
		}

		[Fact]
		public void Storyteller_ShouldSeeRecordsAppendedAfterItWasCreated()
		{
			string directory = CreateCleanDirectory("live-read");
			try
			{
				TextTrail storyId = new TextTrail("catalog", "content");
				using (FileStoryManager manager = new FileStoryManager(directory, CreatePiedPiper()))
				{
					FileStoryteller storyteller = manager.GetStoryteller(storyId);
					Assert.False(storyteller.HasSomethingForMe);

					manager.GetScribe(storyId).RecordSomething(FirstThing);

					Assert.True(storyteller.HasSomethingForMe);
					Assert.Equal(FirstThing, storyteller.TellMeSomething().Thing);
				}
			}
			finally
			{
				DeleteDirectory(directory);
			}
		}

		[Fact]
		public void EmptyStory_ShouldHaveNothingToTell()
		{
			string directory = CreateCleanDirectory("empty");
			try
			{
				using (FileStoryManager manager = new FileStoryManager(directory, CreatePiedPiper()))
				{
					FileStoryteller storyteller = manager.GetStoryteller(new TextTrail("catalog", "content"));

					Assert.False(storyteller.HasSomethingForMe);
					Assert.Throws<InvalidOperationException>(() => storyteller.TellMeSomething());
				}
			}
			finally
			{
				DeleteDirectory(directory);
			}
		}
		#endregion

		#region durability tests
		[Fact]
		public void TruncatedTrailingBytes_ShouldNotHideEarlierRecords()
		{
			string directory = CreateCleanDirectory("truncated-bytes");
			try
			{
				TextTrail storyId = new TextTrail("catalog", "content");
				using (FileStoryManager manager = new FileStoryManager(directory, CreatePiedPiper()))
				{
					manager.GetScribe(storyId).RecordSomething(FirstThing, SecondThing);
				}

				string storyFile = GetOnlyStoryFile(directory);
				using (FileStream stream = new FileStream(storyFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
				{
					byte[] tornTail = new byte[] { 0x01, 0x02, 0x03 };
					stream.Write(tornTail, 0, tornTail.Length);
				}

				using (FileStoryManager reader = new FileStoryManager(directory, CreatePiedPiper()))
				{
					FileStoryteller storyteller = reader.GetStoryteller(storyId);
					Assert.Equal(FirstThing, storyteller.TellMeSomething().Thing);
					Assert.Equal(SecondThing, storyteller.TellMeSomething().Thing);
					Assert.False(storyteller.HasSomethingForMe);
				}
			}
			finally
			{
				DeleteDirectory(directory);
			}
		}

		[Fact]
		public void TruncatedLastByte_ShouldDropOnlyTheTrailingRecord()
		{
			string directory = CreateCleanDirectory("truncated-last-byte");
			try
			{
				TextTrail storyId = new TextTrail("catalog", "content");
				using (FileStoryManager manager = new FileStoryManager(directory, CreatePiedPiper()))
				{
					manager.GetScribe(storyId).RecordSomething(FirstThing, SecondThing);
				}

				string storyFile = GetOnlyStoryFile(directory);
				using (FileStream stream = new FileStream(storyFile, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
				{
					stream.SetLength(stream.Length - 1);
				}

				using (FileStoryManager reader = new FileStoryManager(directory, CreatePiedPiper()))
				{
					FileStoryteller storyteller = reader.GetStoryteller(storyId);
					StoryThing first = storyteller.TellMeSomething();

					Assert.Equal(0L, first.Offset);
					Assert.Equal(FirstThing, first.Thing);
					Assert.False(storyteller.HasSomethingForMe);
				}
			}
			finally
			{
				DeleteDirectory(directory);
			}
		}

		[Fact]
		public void ReopenedWriter_ShouldRepairATornTailAndAppend()
		{
			string directory = CreateCleanDirectory("repair-tail");
			try
			{
				TextTrail storyId = new TextTrail("catalog", "content");
				using (FileStoryManager manager = new FileStoryManager(directory, CreatePiedPiper()))
				{
					manager.GetScribe(storyId).RecordSomething(FirstThing, SecondThing);
				}

				string storyFile = GetOnlyStoryFile(directory);
				using (FileStream stream = new FileStream(storyFile, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
				{
					stream.SetLength(stream.Length - 1);
				}

				using (FileStoryManager writer = new FileStoryManager(directory, CreatePiedPiper()))
				{
					writer.GetScribe(storyId).RecordSomething(ThirdThing);
				}

				using (FileStoryManager reader = new FileStoryManager(directory, CreatePiedPiper()))
				{
					FileStoryteller storyteller = reader.GetStoryteller(storyId);
					StoryThing first = storyteller.TellMeSomething();
					StoryThing third = storyteller.TellMeSomething();

					Assert.Equal(0L, first.Offset);
					Assert.Equal(FirstThing, first.Thing);
					Assert.Equal(1L, third.Offset);
					Assert.Equal(ThirdThing, third.Thing);
					Assert.False(storyteller.HasSomethingForMe);
				}
			}
			finally
			{
				DeleteDirectory(directory);
			}
		}

		[Fact]
		public void OversizedLengthOnTheLastFrame_ShouldNotTruncateCommittedRecords()
		{
			AssertOversizedLengthIsNotTruncated(corruptLastFrame: true);
		}

		[Fact]
		public void OversizedLengthOnAnEarlierFrame_ShouldNotTruncateCommittedRecords()
		{
			AssertOversizedLengthIsNotTruncated(corruptLastFrame: false);
		}

		[Fact]
		public void CorruptEarlierFrame_ShouldThrowInsteadOfSkippingIt()
		{
			string directory = CreateCleanDirectory("corrupt-middle");
			try
			{
				TextTrail storyId = new TextTrail("catalog", "content");
				using (FileStoryManager manager = new FileStoryManager(directory, CreatePiedPiper()))
				{
					manager.GetScribe(storyId).RecordSomething(FirstThing, SecondThing);
				}

				string storyFile = GetOnlyStoryFile(directory);
				byte[] bytes = File.ReadAllBytes(storyFile);
				Assert.True(bytes.Length > FileStoryLog.HeaderLength + 4);
				bytes[FileStoryLog.HeaderLength + 4] ^= 0xFF;
				File.WriteAllBytes(storyFile, bytes);

				using (FileStoryManager reader = new FileStoryManager(directory, CreatePiedPiper()))
				{
					FileStoryteller storyteller = reader.GetStoryteller(storyId);
					Assert.Throws<InvalidDataException>(() => storyteller.TellMeSomething());
				}
			}
			finally
			{
				DeleteDirectory(directory);
			}
		}

		[Fact]
		public void StoryFile_ShouldStartWithTheVersion1Header()
		{
			string directory = CreateCleanDirectory("header");
			try
			{
				TextTrail storyId = new TextTrail("catalog", "content");
				using (FileStoryManager manager = new FileStoryManager(directory, CreatePiedPiper()))
				{
					manager.GetScribe(storyId).RecordSomething(FirstThing);
				}

				byte[] bytes = File.ReadAllBytes(GetOnlyStoryFile(directory));

				Assert.True(bytes.Length >= FileStoryLog.HeaderLength);
				Assert.Equal("BRPSTORY", Encoding.ASCII.GetString(bytes, 0, 8));
				Assert.Equal(FileStoryLog.Version, BitConverter.ToUInt32(bytes, 8));
				Assert.Equal(0u, BitConverter.ToUInt32(bytes, 12));
			}
			finally
			{
				DeleteDirectory(directory);
			}
		}
		#endregion

		#region manager tests
		[Fact]
		public void FileStoryManager_ShouldFindStoryForEquivalentTextTrail()
		{
			string directory = CreateCleanDirectory("equivalent-trail");
			try
			{
				using (FileStoryManager manager = new FileStoryManager(directory, CreatePiedPiper()))
				{
					TextTrail writerTrail = new TextTrail("one", "two");
					TextTrail readerTrail = new TextTrail("one", "two");
					FileScribe writer = manager.GetScribe(writerTrail);
					FileScribe sameWriter = manager.GetScribe(readerTrail);
					Assert.Same(writer, sameWriter);

					writer.RecordSomething(FirstThing);

					FileStoryteller storyteller = manager.GetStoryteller(readerTrail);
					StoryThing storyThing = storyteller.TellMeSomething();

					Assert.Equal(0L, storyThing.Offset);
					Assert.Equal(FirstThing, storyThing.Thing);
				}
			}
			finally
			{
				DeleteDirectory(directory);
			}
		}

		[Fact]
		public void FileStoryManager_ShouldKeepDifferentTextTrailsSeparate()
		{
			string directory = CreateCleanDirectory("separate-trails");
			try
			{
				using (FileStoryManager manager = new FileStoryManager(directory, CreatePiedPiper()))
				{
					manager.GetScribe(new TextTrail("one", "two")).RecordSomething(FirstThing);

					FileStoryteller storyteller = manager.GetStoryteller(new TextTrail("one", "three"));

					Assert.False(storyteller.HasSomethingForMe);
				}
			}
			finally
			{
				DeleteDirectory(directory);
			}
		}

		[Fact]
		public void FileStory_ShouldMatchMemoryScribeOffsetsAndOrder()
		{
			string directory = CreateCleanDirectory("memory-parity");
			try
			{
				Code[] things = new Code[] { FirstThing, SecondThing, ThirdThing, FirstThing };

				IList<StoryThing> memoryThings = new List<StoryThing>();
				MemoryScribe memoryScribe = new MemoryScribe(memoryThings);
				memoryScribe.RecordSomething(things);
				MemoryStoryteller memoryStoryteller = new MemoryStoryteller(memoryThings);

				TextTrail storyId = new TextTrail("catalog", "content");
				using (FileStoryManager manager = new FileStoryManager(directory, CreatePiedPiper()))
				{
					manager.GetScribe(storyId).RecordSomething(things);
					FileStoryteller fileStoryteller = manager.GetStoryteller(storyId);

					while (memoryStoryteller.HasSomethingForMe)
					{
						Assert.True(fileStoryteller.HasSomethingForMe);
						StoryThing expected = memoryStoryteller.TellMeSomething();
						StoryThing actual = fileStoryteller.TellMeSomething();

						Assert.Equal(expected.Offset, actual.Offset);
						Assert.Equal(expected.Thing, actual.Thing);
					}

					Assert.False(fileStoryteller.HasSomethingForMe);
				}
			}
			finally
			{
				DeleteDirectory(directory);
			}
		}

		[Fact]
		public void SecondWriter_ShouldBeRejectedUntilTheFirstIsDisposed()
		{
			string directory = CreateCleanDirectory("one-writer");
			try
			{
				TextTrail storyId = new TextTrail("catalog", "content");
				IPiedPiper piedPiper = CreatePiedPiper();
				using (FileScribe first = new FileScribe(directory, storyId, piedPiper))
				{
					first.RecordSomething(FirstThing);
					IOException exception = Assert.Throws<IOException>(() =>
					{
						new FileScribe(directory, storyId, piedPiper).Dispose();
					});
					Assert.Contains("already has a writer", exception.Message);
				}

				using (FileScribe second = new FileScribe(directory, storyId, piedPiper))
				{
					second.RecordSomething(SecondThing);
				}

				FileStoryteller storyteller = OpenStoryteller(directory, storyId);
				Assert.Equal(FirstThing, storyteller.TellMeSomething().Thing);
				Assert.Equal(SecondThing, storyteller.TellMeSomething().Thing);
			}
			finally
			{
				DeleteDirectory(directory);
			}
		}
		#endregion

		#region argument tests
		[Fact]
		public void Constructors_ShouldRejectNullOrBlankArguments()
		{
			string directory = CreateCleanDirectory("arguments");
			try
			{
				IPiedPiper piedPiper = CreatePiedPiper();
				TextTrail storyId = new TextTrail("catalog", "content");

				Assert.Throws<ArgumentException>(() => new FileStoryManager(" ", piedPiper));
				Assert.Throws<ArgumentNullException>(() => new FileStoryManager(directory, null!));
				Assert.Throws<ArgumentNullException>(() => new FileScribe(directory, null!, piedPiper));
				Assert.Throws<ArgumentNullException>(() => new FileScribe(directory, storyId, null!));
				Assert.Throws<ArgumentException>(() => new FileStoryteller(" ", storyId, piedPiper));
				Assert.Throws<ArgumentNullException>(() => new FileStoryteller(directory, storyId, null!));

				using (FileStoryManager manager = new FileStoryManager(directory, piedPiper))
				{
					Assert.Throws<ArgumentNullException>(() => manager.GetScribe(null!));
					Assert.Throws<ArgumentNullException>(() => manager.GetStoryteller(null!));
					Assert.Throws<ArgumentNullException>(() => manager.GetScribe(storyId).RecordSomething(null!));
				}
			}
			finally
			{
				DeleteDirectory(directory);
			}
		}

		[Fact]
		public void SetBookmark_ShouldRejectANegativeBookmark()
		{
			string directory = CreateCleanDirectory("bookmark");
			try
			{
				FileStoryteller storyteller = OpenStoryteller(directory, new TextTrail("catalog", "content"));
				Assert.Throws<ArgumentOutOfRangeException>(() => storyteller.SetBookmark(-1));
			}
			finally
			{
				DeleteDirectory(directory);
			}
		}
		#endregion

		#region checksum tests
		[Fact]
		public void Crc32_ShouldMatchTheIeeeCheckValue()
		{
			byte[] data = Encoding.ASCII.GetBytes("123456789");

			Assert.Equal(0xCBF43926u, Crc32.Compute(data));
		}
		#endregion

		#region private functions
		private static IPiedPiper CreatePiedPiper()
		{
			IPiedPiper piedPiper = new PiedPiper();
			piedPiper.RegisterCorePackRats();
			piedPiper.RegisterPackRats(typeof(StoryThing).Assembly);
			return piedPiper;
		}

		private static string CreateCleanDirectory(string name)
		{
			string path = Path.Combine(Path.GetTempPath(), "bigredprof-stories-file", name);
			if (Directory.Exists(path))
				Directory.Delete(path, true);

			Directory.CreateDirectory(path);
			return path;
		}

		private static void DeleteDirectory(string directory)
		{
			if (Directory.Exists(directory))
				Directory.Delete(directory, true);
		}

		private static void AssertOversizedLengthIsNotTruncated(bool corruptLastFrame)
		{
			string directoryName = corruptLastFrame ? "oversized-last" : "oversized-earlier";
			string directory = CreateCleanDirectory(directoryName);
			try
			{
				TextTrail storyId = new TextTrail("catalog", "content");
				using (FileStoryManager manager = new FileStoryManager(directory, CreatePiedPiper()))
				{
					manager.GetScribe(storyId).RecordSomething(FirstThing, SecondThing, ThirdThing);
				}

				string storyFile = GetOnlyStoryFile(directory);
				byte[] original = File.ReadAllBytes(storyFile);
				IList<int> lengthOffsets = FrameLengthOffsets(original);
				Assert.True(lengthOffsets.Count >= 2);

				int lengthOffset = corruptLastFrame ? lengthOffsets[lengthOffsets.Count - 1] : lengthOffsets[0];
				byte[] corrupted = new byte[original.Length];
				Buffer.BlockCopy(original, 0, corrupted, 0, original.Length);
				uint oversizedLength = (uint)FileStoryLog.MaxPayloadLength + 1;
				corrupted[lengthOffset] = (byte)oversizedLength;
				corrupted[lengthOffset + 1] = (byte)(oversizedLength >> 8);
				corrupted[lengthOffset + 2] = (byte)(oversizedLength >> 16);
				corrupted[lengthOffset + 3] = (byte)(oversizedLength >> 24);
				File.WriteAllBytes(storyFile, corrupted);

				FileStoryteller storyteller = new FileStoryteller(directory, storyId, CreatePiedPiper());
				Assert.Throws<InvalidDataException>(() => storyteller.TellMeSomething());
				Assert.Throws<InvalidDataException>(() => new FileScribe(directory, storyId, CreatePiedPiper()));

				Assert.Equal(corrupted, File.ReadAllBytes(storyFile));
			}
			finally
			{
				DeleteDirectory(directory);
			}
		}

		private static IList<int> FrameLengthOffsets(byte[] storyFile)
		{
			List<int> offsets = new List<int>();
			int position = FileStoryLog.HeaderLength;
			while (position + 8 <= storyFile.Length)
			{
				uint payloadLength = BitConverter.ToUInt32(storyFile, position);
				if (payloadLength == 0 || payloadLength > FileStoryLog.MaxPayloadLength)
					break;

				int frameLength = 4 + (int)payloadLength + 4;
				if (position + frameLength > storyFile.Length)
					break;

				offsets.Add(position);
				position += frameLength;
			}

			return offsets;
		}

		private static string GetOnlyStoryFile(string directory)
		{
			string[] files = Directory.GetFiles(directory, "*.story");
			Assert.Single(files);
			return files[0];
		}

		private static FileStoryteller OpenStoryteller(string directory, TextTrail storyId)
		{
			return new FileStoryteller(directory, storyId, CreatePiedPiper());
		}
		#endregion
	}
}
