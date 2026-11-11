using BigRedProf.Stories.LibrarySemanticsLab;

namespace BigRedProf.Stories.LibrarySemanticsLab.CrashHost
{
	public static class Program
	{
		public static int Main(string[] args)
		{
			if (args.Length == 0)
				return Usage();

			string command = args[0];
			if (command == "kill-append")
				return KillAppend(args);

			if (command == "hold-lock")
				return HoldLock(args);

			if (command == "race-append")
				return RaceAppend(args);

			if (command == "pidfile")
				return PidFile(args);

			return Usage();
		}

		private static int KillAppend(string[] args)
		{
			if (args.Length < 5)
				return Usage();

			string root = args[1];
			BatchFormat format = Library.ParseFormat(args[2]);
			string point = args[3];
			int records = int.Parse(args[4]);
			bool tear = args.Length > 5 && args[5] == "--tear";
			Library library = File.Exists(Path.Combine(root, "library.txt"))
				? Library.Open(root)
				: Library.Create(root, format);
			FaultInjector faults = new FaultInjector();
			faults.ArmedPoint = point;
			faults.KillProcess = true;
			faults.TearWrite = tear;
			RecordDraft[] drafts = new RecordDraft[records];
			for (int i = 0; i < records; i++)
				drafts[i] = new RecordDraft(RetentionClass.Stone, LabCode.FromText("record-" + i.ToString()));

			StoryAppend story = new StoryAppend("durability", library.HighWater("durability"), drafts);
			AppendRequest request = new AppendRequest(
				"durability-batch",
				library.CurrentEditionId,
				new StoryAppend[] { story }
			);
			library.Append(request, faults);
			library.Dispose();
			return 0;
		}

		private static int HoldLock(string[] args)
		{
			if (args.Length < 3)
				return Usage();

			Library library = Library.Open(args[1]);
			File.WriteAllText(args[2], "ready");
			Thread.Sleep(Timeout.Infinite);
			library.Dispose();
			return 0;
		}

		private static int RaceAppend(string[] args)
		{
			if (args.Length < 7)
				return Usage();

			string root = args[1];
			string expectedEdition = args[2];
			string key = args[3];
			string body = args[4];
			string resultFile = args[5];
			string goFile = args[6];
			DateTime deadline = DateTime.UtcNow.AddSeconds(15);
			while (!File.Exists(goFile))
			{
				if (DateTime.UtcNow > deadline)
				{
					File.WriteAllText(resultFile, "TimedOut");
					return 2;
				}

				Thread.Sleep(20);
			}

			try
			{
				using (Library library = Library.Open(root))
				{
					RecordDraft draft = new RecordDraft(RetentionClass.Stone, LabCode.FromText(body));
					StoryAppend story = new StoryAppend("race", 0, new RecordDraft[] { draft });
					AppendRequest request = new AppendRequest(key, expectedEdition, new StoryAppend[] { story });
					AppendResult result = library.Append(request, null);
					File.WriteAllText(resultFile, result.Status.ToString());
				}
			}
			catch (Exception exception)
			{
				File.WriteAllText(resultFile, "Error " + exception.GetType().Name + " " + exception.Message);
			}

			return 0;
		}

		private static int PidFile(string[] args)
		{
			if (args.Length < 3)
				return Usage();

			File.WriteAllText(args[1], Environment.ProcessId.ToString());
			File.WriteAllText(args[2], "ready");
			Thread.Sleep(Timeout.Infinite);
			return 0;
		}

		private static int Usage()
		{
			Console.Error.WriteLine("kill-append <root> <format> <point> <records> [--tear]");
			Console.Error.WriteLine("hold-lock <root> <readyFile>");
			Console.Error.WriteLine("race-append <root> <expectedEdition> <key> <body> <resultFile> <goFile>");
			Console.Error.WriteLine("pidfile <lockFile> <readyFile>");
			return 2;
		}
	}
}
