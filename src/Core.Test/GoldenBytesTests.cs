using BigRedProf.Data.Core;
using BigRedProf.Stories.Data;
using BigRedProf.Stories.Disk;
using BigRedProf.Stories.Logging.Data;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;

namespace BigRedProf.Stories.Core.Test
{
	/// <summary>
	/// Reads src/Golden/golden.txt, captured from stories before BitStream.
	/// The bytes in that file are the oracle for story entries, catalog files,
	/// pack-rat encodings, logging models, and backup checkpoints.
	/// </summary>
	public class GoldenBytesTests
	{
		#region constants
		private const string StoryHeaderHex = "42525053544f52590100000000000000";
		#endregion

		#region tests
		[Fact]
		public void StoredFormatsShouldMatchTheGoldenBytes()
		{
			string path = Path.Combine(AppContext.BaseDirectory, "Golden", "golden.txt");
			string text = File.ReadAllText(path);
			List<Sample> samples = Parse(text);
			Dictionary<string, Sample> byName = Index(samples);

			Assert.Equal(10, samples.Count);
			Assert.Contains("story-byte-aligned", byName.Keys);
			Assert.Contains("story-mid-byte", byName.Keys);
			Assert.Contains("story-zero-bit", byName.Keys);
			Assert.Contains("story-back-to-back", byName.Keys);
			Assert.Contains("catalog-content", byName.Keys);
			Assert.Contains("log-entry-property", byName.Keys);
			Assert.Contains("log-entry", byName.Keys);
			Assert.Contains("log-entry-null-event", byName.Keys);
			Assert.Contains("backup-checkpoint-0", byName.Keys);
			Assert.Contains("backup-checkpoint-4", byName.Keys);

			PiedPiper piedPiper = CreatePiedPiper();
			foreach (Sample sample in samples)
				Replay(piedPiper, sample, byName);
		}
		#endregion

		#region replay
		private static void Replay(PiedPiper piedPiper, Sample sample, IReadOnlyDictionary<string, Sample> byName)
		{
			if (sample.Kind == "story-entry")
				ReplayStoryEntry(piedPiper, sample);
			else if (sample.Kind == "catalog-file")
				ReplayCatalog(piedPiper, sample, byName);
			else if (sample.Kind == "log-property")
				ReplayLogProperty(piedPiper, sample);
			else if (sample.Kind == "log-entry")
				ReplayLogEntry(piedPiper, sample, byName);
			else if (sample.Kind == "backup-checkpoint")
				ReplayCheckpoint(piedPiper, sample);
			else
				throw new InvalidOperationException("Unknown golden kind '" + sample.Kind + "'.");
		}

		private static void ReplayStoryEntry(PiedPiper piedPiper, Sample sample)
		{
			Code thing = CodeFromBits(sample.Bits, sample.BitLength);
			if (sample.Name == "story-byte-aligned")
			{
				Assert.NotEqual(0, thing.Length);
				Assert.Equal(0, thing.Length % 8);
				Assert.Equal(0, sample.StoryThingBitLength % 8);
			}

			if (sample.Name == "story-mid-byte")
			{
				Assert.NotEqual(0, thing.Length % 8);
				Assert.NotEqual(0, sample.StoryThingBitLength % 8);
				Assert.NotEqual(0, sample.TapeCodeBitLength % 8);
			}

			if (sample.Name == "story-zero-bit")
			{
				Assert.Equal(0, thing.Length);
				Assert.NotEqual(0, sample.TapeCodeBitLength % 8);
			}

			if (sample.Name == "story-back-to-back")
			{
				Assert.Contains("WRITE -", sample.Operations);
				Assert.Equal(sample.Bits, ConcatenateOperations(sample.Operations));
			}

			StoryThing storyThing = new StoryThing()
			{
				Offset = 0,
				Thing = thing
			};
			AssertRoundTrip<StoryThing>(
				piedPiper,
				storyThing,
				StoriesSchemaId.StoryThing,
				sample.StoryThingBitLength,
				sample.StoryThingHex,
				unpacked =>
				{
					Assert.Equal(0L, unpacked.Offset);
					Assert.Equal(thing, unpacked.Thing);
				}
			);

			Code packed = piedPiper.PackModel<StoryThing>(storyThing, StoriesSchemaId.StoryThing);
			byte[] payload = piedPiper.SaveCodeToByteArray(packed);
			Assert.Equal(ParseHex(sample.PayloadHex), payload);
			Assert.Equal(packed, piedPiper.LoadCodeFromByteArray(ParseHex(sample.PayloadHex)));

			AssertStoryFile(piedPiper, sample, new Code[] { thing });

			ListOfThings listOfThings = new ListOfThings()
			{
				Things = new List<Code>() { thing }
			};
			AssertRoundTrip<ListOfThings>(
				piedPiper,
				listOfThings,
				StoriesSchemaId.ListOfThings,
				sample.ListOfThingsBitLength,
				sample.ListOfThingsHex,
				unpacked =>
				{
					Assert.Single(unpacked.Things);
					Assert.Equal(thing, unpacked.Things[0]);
				}
			);

			ListOfStoryThings listOfStoryThings = new ListOfStoryThings()
			{
				StoryThings = new List<StoryThing>() { storyThing }
			};
			AssertRoundTrip<ListOfStoryThings>(
				piedPiper,
				listOfStoryThings,
				StoriesSchemaId.ListOfStoryThings,
				sample.ListOfStoryThingsBitLength,
				sample.ListOfStoryThingsHex,
				unpacked =>
				{
					Assert.Single(unpacked.StoryThings);
					Assert.Equal(0L, unpacked.StoryThings[0].Offset);
					Assert.Equal(thing, unpacked.StoryThings[0].Thing);
				}
			);

			AssertRoundTrip<Code>(
				piedPiper,
				thing,
				CoreSchema.Code,
				sample.TapeCodeBitLength,
				sample.TapeCodeHex,
				unpacked => Assert.Equal(thing, unpacked)
			);
		}

		private static void ReplayCatalog(
			PiedPiper piedPiper,
			Sample sample,
			IReadOnlyDictionary<string, Sample> byName
		)
		{
			string[] names = new string[]
			{
				"story-byte-aligned",
				"story-mid-byte",
				"story-zero-bit",
				"story-back-to-back"
			};
			Assert.Equal(names.Length, sample.Count);
			Assert.Equal(names.Length, sample.Items.Count);

			Code[] things = new Code[names.Length];
			List<StoryThing> storyThings = new List<StoryThing>();
			for (int i = 0; i < names.Length; i++)
			{
				Sample entry = byName[names[i]];
				Assert.Equal(entry.Bits, sample.Items[i].Bits);
				Assert.Equal(entry.BitLength, sample.Items[i].BitLength);
				things[i] = CodeFromBits(sample.Items[i].Bits, sample.Items[i].BitLength);
				storyThings.Add(new StoryThing()
				{
					Offset = i,
					Thing = things[i]
				});
			}

			AssertStoryFile(piedPiper, sample, things);

			ListOfThings listOfThings = new ListOfThings()
			{
				Things = new List<Code>(things)
			};
			AssertRoundTrip<ListOfThings>(
				piedPiper,
				listOfThings,
				StoriesSchemaId.ListOfThings,
				sample.ListOfThingsBitLength,
				sample.ListOfThingsHex,
				unpacked =>
				{
					Assert.Equal(things.Length, unpacked.Things.Count);
					for (int i = 0; i < things.Length; i++)
						Assert.Equal(things[i], unpacked.Things[i]);
				}
			);

			ListOfStoryThings listOfStoryThings = new ListOfStoryThings()
			{
				StoryThings = storyThings
			};
			AssertRoundTrip<ListOfStoryThings>(
				piedPiper,
				listOfStoryThings,
				StoriesSchemaId.ListOfStoryThings,
				sample.ListOfStoryThingsBitLength,
				sample.ListOfStoryThingsHex,
				unpacked =>
				{
					Assert.Equal(storyThings.Count, unpacked.StoryThings.Count);
					for (int i = 0; i < storyThings.Count; i++)
					{
						Assert.Equal(storyThings[i].Offset, unpacked.StoryThings[i].Offset);
						Assert.Equal(storyThings[i].Thing, unpacked.StoryThings[i].Thing);
					}
				}
			);
		}

		private static void ReplayLogProperty(PiedPiper piedPiper, Sample sample)
		{
			LogEntryProperty property = new LogEntryProperty()
			{
				Name = sample.NameText,
				Value = sample.Value
			};
			AssertRoundTrip<LogEntryProperty>(
				piedPiper,
				property,
				StoriesLoggingSchemaId.LogEntryProperty,
				sample.EntryBitLength,
				sample.EntryHex,
				unpacked =>
				{
					Assert.Equal(property.Name, unpacked.Name);
					Assert.Equal(property.Value, unpacked.Value);
				}
			);
		}

		private static void ReplayLogEntry(
			PiedPiper piedPiper,
			Sample sample,
			IReadOnlyDictionary<string, Sample> byName
		)
		{
			LogEntry entry = new LogEntry()
			{
				LogName = sample.LogName,
				EventId = sample.EventId,
				EventName = sample.EventName,
				Level = sample.Level,
				Message = sample.Message,
				Properties = new List<LogEntryProperty>()
			};
			foreach (Property property in sample.Properties)
			{
				entry.Properties.Add(new LogEntryProperty()
				{
					Name = property.Name,
					Value = property.Value
				});
			}

			if (sample.Name == "log-entry")
			{
				Sample propertySample = byName["log-entry-property"];
				Assert.Equal(propertySample.NameText, entry.Properties[0].Name);
				Assert.Equal(propertySample.Value, entry.Properties[0].Value);
			}

			if (sample.Name == "log-entry-null-event")
			{
				Assert.Null(entry.EventName);
				Assert.Empty(entry.Properties);
			}

			AssertRoundTrip<LogEntry>(
				piedPiper,
				entry,
				StoriesLoggingSchemaId.LogEntry,
				sample.EntryBitLength,
				sample.EntryHex,
				unpacked => AssertSameLogEntry(entry, unpacked)
			);

			Datum datum = piedPiper.PackDatum(entry, StoriesLoggingSchemaId.LogEntry);
			AssertRoundTrip<Datum>(
				piedPiper,
				datum,
				CoreSchema.Datum,
				sample.DatumBitLength,
				sample.DatumHex,
				unpacked =>
				{
					Assert.Equal(new Guid(StoriesLoggingSchemaId.LogEntry), unpacked.SchemaId);
					AssertSameLogEntry(entry, unpacked.Unpack<LogEntry>(piedPiper));
				}
			);

			Code datumCode = piedPiper.PackModel<Datum>(datum, CoreSchema.Datum);
			StoryThing storyThing = new StoryThing()
			{
				Offset = 0,
				Thing = datumCode
			};
			AssertRoundTrip<StoryThing>(
				piedPiper,
				storyThing,
				StoriesSchemaId.StoryThing,
				sample.StoryThingBitLength,
				sample.StoryThingHex,
				unpacked =>
				{
					Assert.Equal(0L, unpacked.Offset);
					Assert.Equal(datumCode, unpacked.Thing);
				}
			);

			Code packedStoryThing = piedPiper.PackModel<StoryThing>(storyThing, StoriesSchemaId.StoryThing);
			Assert.Equal(ParseHex(sample.PayloadHex), piedPiper.SaveCodeToByteArray(packedStoryThing));
			Assert.Equal(packedStoryThing, piedPiper.LoadCodeFromByteArray(ParseHex(sample.PayloadHex)));
			AssertStoryFile(piedPiper, sample, new Code[] { datumCode });
		}

		private static void ReplayCheckpoint(PiedPiper piedPiper, Sample sample)
		{
			if (sample.Name == "backup-checkpoint-0")
				Assert.Equal(0L, sample.Offset);

			if (sample.Name == "backup-checkpoint-4")
				Assert.Equal(4L, sample.Offset);

			AssertRoundTrip<long>(
				piedPiper,
				sample.Offset,
				CoreSchema.Int64,
				sample.CheckpointBitLength,
				sample.CheckpointHex,
				unpacked => Assert.Equal(sample.Offset, unpacked)
			);
		}

		private static void AssertRoundTrip<T>(
			PiedPiper piedPiper,
			T model,
			string schemaId,
			int bitLength,
			string hex,
			Action<T> assertUnpacked
		)
		{
			PackRat<T> packRat = piedPiper.GetPackRat<T>(schemaId);
			Code packed = piedPiper.PackModel<T>(model, schemaId);
			Assert.Equal(bitLength, packed.Length);

			byte[] bytes = ParseHex(hex);
			Assert.Equal(bytes, packed.ToByteArray());
			Assert.Equal(bytes, WriteWire(packRat, model));

			T fromCode = piedPiper.UnpackModel<T>(CodeFromHex(hex, bitLength), schemaId);
			T fromWire = ReadWire(packRat, bytes);
			assertUnpacked(fromCode);
			assertUnpacked(fromWire);
		}

		private static void AssertStoryFile(PiedPiper piedPiper, Sample sample, Code[] things)
		{
			Assert.StartsWith(StoryHeaderHex, sample.FileHex, StringComparison.Ordinal);

			TextTrail storyId = TextTrailSerializer.ParseTextRepresentation(sample.StoryId);
			string expectedName = TextTrailSerializer.ToMultihashString(storyId) + ".story";
			Assert.Equal(expectedName, sample.FileName);

			byte[] written = WriteStoryFile(piedPiper, storyId, things, sample.Name + "-write");
			Assert.Equal(ParseHex(sample.FileHex), written);

			List<StoryThing> readBack = ReadStoryFile(piedPiper, storyId, sample.FileName, ParseHex(sample.FileHex), sample.Name + "-read");
			Assert.Equal(things.Length, readBack.Count);
			for (int i = 0; i < things.Length; i++)
			{
				Assert.Equal(i, readBack[i].Offset);
				Assert.Equal(things[i], readBack[i].Thing);
			}
		}

		private static void AssertSameLogEntry(LogEntry expected, LogEntry actual)
		{
			Assert.Equal(expected.LogName, actual.LogName);
			Assert.Equal(expected.EventId, actual.EventId);
			Assert.Equal(expected.EventName, actual.EventName);
			Assert.Equal(expected.Level, actual.Level);
			Assert.Equal(expected.Message, actual.Message);
			Assert.Equal(expected.Properties.Count, actual.Properties.Count);
			for (int i = 0; i < expected.Properties.Count; i++)
			{
				Assert.Equal(expected.Properties[i].Name, actual.Properties[i].Name);
				Assert.Equal(expected.Properties[i].Value, actual.Properties[i].Value);
			}
		}

		private static byte[] WriteStoryFile(PiedPiper piedPiper, TextTrail storyId, Code[] things, string name)
		{
			string root = CreateCleanDirectory(name);
			try
			{
				using (FileScribe scribe = new FileScribe(root, storyId, piedPiper))
				{
					scribe.RecordSomething(things);
				}

				string fileName = TextTrailSerializer.ToMultihashString(storyId) + ".story";
				return File.ReadAllBytes(Path.Combine(root, fileName));
			}
			finally
			{
				DeleteDirectory(root);
			}
		}

		private static List<StoryThing> ReadStoryFile(
			PiedPiper piedPiper,
			TextTrail storyId,
			string fileName,
			byte[] bytes,
			string name
		)
		{
			string root = CreateCleanDirectory(name);
			try
			{
				File.WriteAllBytes(Path.Combine(root, fileName), bytes);
				FileStoryteller storyteller = new FileStoryteller(root, storyId, piedPiper);
				List<StoryThing> things = new List<StoryThing>();
				while (storyteller.HasSomethingForMe)
					things.Add(storyteller.TellMeSomething());

				return things;
			}
			finally
			{
				DeleteDirectory(root);
			}
		}

		private static byte[] WriteWire<T>(PackRat<T> packRat, T model)
		{
			MemoryStream stream = new MemoryStream();
			using (StreamBitWriter writer = new StreamBitWriter(stream))
			{
				packRat.PackModel(writer, model);
			}

			return stream.ToArray();
		}

		private static T ReadWire<T>(PackRat<T> packRat, byte[] bytes)
		{
			using (StreamBitReader reader = new StreamBitReader(new MemoryStream(bytes)))
			{
				return packRat.UnpackModel(reader);
			}
		}

		private static PiedPiper CreatePiedPiper()
		{
			PiedPiper piedPiper = new PiedPiper();
			piedPiper.RegisterCorePackRats();
			piedPiper.RegisterPackRats(typeof(StoryThing).Assembly);
			piedPiper.RegisterPackRats(typeof(LogEntry).Assembly);
			return piedPiper;
		}

		private static Code CodeFromBits(string bits, int bitLength)
		{
			Code code = bits == "-" ? new Code(0) : new Code(bits);
			Assert.Equal(bitLength, code.Length);
			return code;
		}

		private static Code CodeFromHex(string hex, int bitLength)
		{
			if (bitLength == 0)
				return new Code(0);

			return new Code(ParseHex(hex), bitLength);
		}

		private static string ConcatenateOperations(IReadOnlyList<string> operations)
		{
			StringBuilder builder = new StringBuilder();
			foreach (string operation in operations)
			{
				Assert.StartsWith("WRITE ", operation, StringComparison.Ordinal);
				string written = operation.Substring("WRITE ".Length);
				if (written != "-")
					builder.Append(written);
			}

			string bits = builder.ToString();
			if (bits.Length == 0)
				return "-";

			return bits;
		}

		private static string CreateCleanDirectory(string name)
		{
			string path = Path.Combine(Path.GetTempPath(), "BigRedProf.Stories.Test", "GoldenBytes", name);
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
		#endregion

		#region parsing
		private static Dictionary<string, Sample> Index(List<Sample> samples)
		{
			Dictionary<string, Sample> byName = new Dictionary<string, Sample>();
			foreach (Sample sample in samples)
			{
				if (byName.ContainsKey(sample.Name))
					throw new InvalidOperationException("Duplicate golden sample '" + sample.Name + "'.");

				byName.Add(sample.Name, sample);
			}

			return byName;
		}

		private static List<Sample> Parse(string text)
		{
			List<Sample> samples = new List<Sample>();
			Sample? current = null;
			string[] lines = text.Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i].TrimEnd('\r');
				if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
					continue;

				if (line.StartsWith("SAMPLE ", StringComparison.Ordinal))
				{
					current = new Sample(line.Substring("SAMPLE ".Length));
					continue;
				}

				if (current == null)
					throw new InvalidOperationException("Line outside a sample: " + line);

				if (line == "END")
				{
					samples.Add(current);
					current = null;
					continue;
				}

				if (line.StartsWith("OP ", StringComparison.Ordinal))
				{
					current.Operations.Add(line.Substring("OP ".Length));
					continue;
				}

				if (line.StartsWith("ITEM ", StringComparison.Ordinal))
				{
					ParseItem(current, line.Substring("ITEM ".Length));
					continue;
				}

				if (line.StartsWith("PROPERTY ", StringComparison.Ordinal))
				{
					ParseProperty(current, line.Substring("PROPERTY ".Length));
					continue;
				}

				int space = line.IndexOf(' ');
				if (space < 0)
					throw new InvalidOperationException("Unknown golden line '" + line + "'.");

				string key = line.Substring(0, space);
				string rest = line.Substring(space + 1);
				Assign(current, key, rest);
			}

			if (current != null)
				throw new InvalidOperationException("Sample " + current.Name + " is missing END.");

			return samples;
		}

		private static void ParseItem(Sample sample, string rest)
		{
			int space = rest.IndexOf(' ');
			if (space < 0)
				throw new InvalidOperationException("Unknown golden item '" + rest + "'.");

			int index = int.Parse(rest.Substring(0, space), CultureInfo.InvariantCulture);
			string tail = rest.Substring(space + 1);
			int tailSpace = tail.IndexOf(' ');
			if (tailSpace < 0)
				throw new InvalidOperationException("Unknown golden item '" + rest + "'.");

			string key = tail.Substring(0, tailSpace);
			string value = tail.Substring(tailSpace + 1);
			while (sample.Items.Count <= index)
				sample.Items.Add(new Item());

			if (key == "BITS")
				sample.Items[index].Bits = value;
			else if (key == "BIT_LENGTH")
				sample.Items[index].BitLength = int.Parse(value, CultureInfo.InvariantCulture);
			else
				throw new InvalidOperationException("Unknown golden item '" + rest + "'.");
		}

		private static void ParseProperty(Sample sample, string rest)
		{
			int space = rest.IndexOf(' ');
			if (space < 0)
				throw new InvalidOperationException("Unknown golden property '" + rest + "'.");

			sample.Properties.Add(new Property(rest.Substring(0, space), rest.Substring(space + 1)));
		}

		private static void Assign(Sample sample, string key, string value)
		{
			if (key == "KIND")
				sample.Kind = value;
			else if (key == "BITS")
				sample.Bits = value;
			else if (key == "BIT_LENGTH")
				sample.BitLength = int.Parse(value, CultureInfo.InvariantCulture);
			else if (key == "STORY_THING_BIT_LENGTH")
				sample.StoryThingBitLength = int.Parse(value, CultureInfo.InvariantCulture);
			else if (key == "STORY_THING_HEX")
				sample.StoryThingHex = value;
			else if (key == "PAYLOAD_HEX")
				sample.PayloadHex = value;
			else if (key == "STORY_ID")
				sample.StoryId = value;
			else if (key == "FILE_NAME")
				sample.FileName = value;
			else if (key == "FILE_HEX")
				sample.FileHex = value;
			else if (key == "LIST_OF_THINGS_BIT_LENGTH")
				sample.ListOfThingsBitLength = int.Parse(value, CultureInfo.InvariantCulture);
			else if (key == "LIST_OF_THINGS_HEX")
				sample.ListOfThingsHex = value;
			else if (key == "LIST_OF_STORY_THINGS_BIT_LENGTH")
				sample.ListOfStoryThingsBitLength = int.Parse(value, CultureInfo.InvariantCulture);
			else if (key == "LIST_OF_STORY_THINGS_HEX")
				sample.ListOfStoryThingsHex = value;
			else if (key == "TAPE_CODE_BIT_LENGTH")
				sample.TapeCodeBitLength = int.Parse(value, CultureInfo.InvariantCulture);
			else if (key == "TAPE_CODE_HEX")
				sample.TapeCodeHex = value;
			else if (key == "COUNT")
				sample.Count = int.Parse(value, CultureInfo.InvariantCulture);
			else if (key == "NAME")
				sample.NameText = value;
			else if (key == "VALUE")
				sample.Value = value;
			else if (key == "ENTRY_BIT_LENGTH")
				sample.EntryBitLength = int.Parse(value, CultureInfo.InvariantCulture);
			else if (key == "ENTRY_HEX")
				sample.EntryHex = value;
			else if (key == "LOG_NAME")
				sample.LogName = value;
			else if (key == "EVENT_ID")
				sample.EventId = int.Parse(value, CultureInfo.InvariantCulture);
			else if (key == "EVENT_NAME")
				sample.EventName = value == "-" ? null : value;
			else if (key == "LEVEL")
				sample.Level = Enum.Parse<LogLevel>(value);
			else if (key == "MESSAGE")
				sample.Message = value;
			else if (key == "DATUM_BIT_LENGTH")
				sample.DatumBitLength = int.Parse(value, CultureInfo.InvariantCulture);
			else if (key == "DATUM_HEX")
				sample.DatumHex = value;
			else if (key == "OFFSET")
				sample.Offset = long.Parse(value, CultureInfo.InvariantCulture);
			else if (key == "CHECKPOINT_BIT_LENGTH")
				sample.CheckpointBitLength = int.Parse(value, CultureInfo.InvariantCulture);
			else if (key == "CHECKPOINT_HEX")
				sample.CheckpointHex = value;
			else
				throw new InvalidOperationException("Unknown golden line '" + key + "'.");
		}

		private static byte[] ParseHex(string hex)
		{
			if (hex == "-")
				return Array.Empty<byte>();

			if ((hex.Length % 2) != 0)
				throw new InvalidOperationException("Hex length is odd.");

			byte[] bytes = new byte[hex.Length / 2];
			for (int i = 0; i < bytes.Length; i++)
				bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

			return bytes;
		}
		#endregion

		#region nested types
		private sealed class Sample
		{
			public Sample(string name)
			{
				Name = name;
				Kind = "";
				Bits = "";
				StoryThingHex = "";
				PayloadHex = "";
				StoryId = "";
				FileName = "";
				FileHex = "";
				ListOfThingsHex = "";
				ListOfStoryThingsHex = "";
				TapeCodeHex = "";
				NameText = "";
				Value = "";
				EntryHex = "";
				LogName = "";
				Message = "";
				DatumHex = "";
				CheckpointHex = "";
				Operations = new List<string>();
				Items = new List<Item>();
				Properties = new List<Property>();
			}

			public string Name
			{
				get;
			}

			public string Kind
			{
				get;
				set;
			}

			public string Bits
			{
				get;
				set;
			}

			public int BitLength
			{
				get;
				set;
			}

			public int StoryThingBitLength
			{
				get;
				set;
			}

			public string StoryThingHex
			{
				get;
				set;
			}

			public string PayloadHex
			{
				get;
				set;
			}

			public string StoryId
			{
				get;
				set;
			}

			public string FileName
			{
				get;
				set;
			}

			public string FileHex
			{
				get;
				set;
			}

			public int ListOfThingsBitLength
			{
				get;
				set;
			}

			public string ListOfThingsHex
			{
				get;
				set;
			}

			public int ListOfStoryThingsBitLength
			{
				get;
				set;
			}

			public string ListOfStoryThingsHex
			{
				get;
				set;
			}

			public int TapeCodeBitLength
			{
				get;
				set;
			}

			public string TapeCodeHex
			{
				get;
				set;
			}

			public int Count
			{
				get;
				set;
			}

			public string NameText
			{
				get;
				set;
			}

			public string Value
			{
				get;
				set;
			}

			public int EntryBitLength
			{
				get;
				set;
			}

			public string EntryHex
			{
				get;
				set;
			}

			public string LogName
			{
				get;
				set;
			}

			public int EventId
			{
				get;
				set;
			}

			public string? EventName
			{
				get;
				set;
			}

			public LogLevel Level
			{
				get;
				set;
			}

			public string Message
			{
				get;
				set;
			}

			public int DatumBitLength
			{
				get;
				set;
			}

			public string DatumHex
			{
				get;
				set;
			}

			public long Offset
			{
				get;
				set;
			}

			public int CheckpointBitLength
			{
				get;
				set;
			}

			public string CheckpointHex
			{
				get;
				set;
			}

			public List<string> Operations
			{
				get;
			}

			public List<Item> Items
			{
				get;
			}

			public List<Property> Properties
			{
				get;
			}
		}

		private sealed class Item
		{
			public Item()
			{
				Bits = "";
			}

			public string Bits
			{
				get;
				set;
			}

			public int BitLength
			{
				get;
				set;
			}
		}

		private sealed class Property
		{
			public Property(string name, string value)
			{
				Name = name;
				Value = value;
			}

			public string Name
			{
				get;
			}

			public string Value
			{
				get;
			}
		}
		#endregion
	}
}
