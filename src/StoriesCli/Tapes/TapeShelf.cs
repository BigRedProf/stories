using BigRedProf.Data.Tape;
using BigRedProf.Data.Tape.Libraries;
using System.Globalization;
using System.Text.Json;

namespace BigRedProf.Stories.StoriesCli.Tapes
{
	/// <summary>
	/// Where generations of tapes live on disk: <c>{root}/{generation}/manifest.json</c> beside
	/// <c>{root}/{generation}/tapes/</c>.
	/// </summary>
	/// <remarks>
	/// A generation is an ISO week, so the first backup of each week starts a fresh, complete
	/// copy and a restore needs only the latest generation. Old generations are deleted by
	/// whoever owns the disk; nothing here deletes anything.
	/// </remarks>
	public sealed class TapeShelf
	{
		#region constants
		private const string ManifestFileName = "manifest.json";
		private const string TapesDirectoryName = "tapes";
		#endregion

		#region static fields
		private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions()
		{
			WriteIndented = true,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		};
		#endregion

		#region fields
		private readonly string _root;
		#endregion

		#region constructors
		public TapeShelf(string root)
		{
			if (string.IsNullOrWhiteSpace(root))
				throw new ArgumentException("A tape root is required.", nameof(root));

			_root = root;
		}
		#endregion

		#region functions
		/// <summary>The generation <paramref name="whenUtc"/> falls in: its ISO week, like <c>2026-W39</c>.</summary>
		public static string GenerationFor(DateTime whenUtc)
		{
			int year = ISOWeek.GetYear(whenUtc);
			int week = ISOWeek.GetWeekOfYear(whenUtc);

			return string.Format(CultureInfo.InvariantCulture, "{0:D4}-W{1:D2}", year, week);
		}
		#endregion

		#region methods
		/// <summary>The newest generation with a manifest, or null when there are none.</summary>
		public string? FindLatestGeneration()
		{
			string? latest = null;
			if (Directory.Exists(_root))
			{
				// ISO week names sort in time order as plain strings.
				latest = Directory.GetDirectories(_root)
					.Where(directory => File.Exists(Path.Combine(directory, ManifestFileName)))
					.Select(directory => Path.GetFileName(directory))
					.OrderBy(name => name, StringComparer.Ordinal)
					.LastOrDefault();
			}

			return latest;
		}

		/// <summary>The generation's manifest, or a new, empty one when it has none yet.</summary>
		public TapeManifest LoadManifest(string generation)
		{
			string path = GetManifestPath(generation);
			TapeManifest manifest;
			if (File.Exists(path))
			{
				manifest = JsonSerializer.Deserialize<TapeManifest>(File.ReadAllText(path), JsonOptions)
					?? throw new TapeException($"The manifest at {path} is empty.");
				manifest.Stories = new SortedDictionary<string, StoryTapes>(manifest.Stories, StringComparer.Ordinal);
			}
			else
			{
				manifest = new TapeManifest() { Generation = generation };
			}

			return manifest;
		}

		/// <summary>
		/// Commits <paramref name="manifest"/>: written beside the old one, then moved over it, so
		/// the manifest on disk is always a whole one.
		/// </summary>
		public void SaveManifest(TapeManifest manifest)
		{
			ArgumentNullException.ThrowIfNull(manifest);

			string path = GetManifestPath(manifest.Generation);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			string temporaryPath = path + ".tmp";
			File.WriteAllText(temporaryPath, JsonSerializer.Serialize(manifest, JsonOptions));
			File.Move(temporaryPath, path, true);
		}

		/// <summary>The tape library the generation's segments live in.</summary>
		public TapeLibrary OpenLibrary(string generation)
		{
			string directory = Path.Combine(GetGenerationPath(generation), TapesDirectoryName);
			Directory.CreateDirectory(directory);

			return new DiskLibrary(directory);
		}
		#endregion

		#region private methods
		private string GetGenerationPath(string generation)
		{
			if (string.IsNullOrWhiteSpace(generation) || generation.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
				throw new ArgumentException($"'{generation}' is not a generation name.", nameof(generation));

			return Path.Combine(_root, generation);
		}

		private string GetManifestPath(string generation)
		{
			return Path.Combine(GetGenerationPath(generation), ManifestFileName);
		}
		#endregion
	}
}
