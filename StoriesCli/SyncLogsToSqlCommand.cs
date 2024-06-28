using BigRedProf.Data;
using BigRedProf.Stories.Internal;
using BigRedProf.Stories.Internal.ApiClient;
using BigRedProf.Stories.Models;
using BigRedProf.Stories.Logging.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

namespace BigRedProf.Stories.StoriesCli
{
    public class SyncLogsToSqlCommand : Command
    {
        private readonly ILogger<SyncLogsToSqlCommand> _logger;
        private readonly ILogger<ApiClient> _apiClientLogger;
        private IPiedPiper? _piedPiper;
        private IStoryListener? _storyListener;

        public SyncLogsToSqlCommand(ILogger<SyncLogsToSqlCommand> logger, ILogger<ApiClient> apiClientLogger)
        {
            _logger = logger;
            _apiClientLogger = apiClientLogger;
        }

        public override int Run(BaseCommandLineOptions commandLineOptions)
        {
            SyncLogsToSqlOptions options = (SyncLogsToSqlOptions)commandLineOptions;

            // Set up SQL connection
            var connectionString = $"Server={options.SqlServer};Database={options.SqlDatabase ?? "."};"
                + $"User Id={options.SqlUsername};Password={options.SqlPassword};"
                + $"TrustServerCertificate={options.TrustServerCertificate}"
            ;
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Ensure tables are created
                EnsureDatabaseSchema(connection, commandLineOptions.Story);

                // Setup the story listener just like ListenCommand
                _piedPiper = new PiedPiper();
                _piedPiper.RegisterCorePackRats();
                _piedPiper.RegisterPackRats(typeof(StoryThing).Assembly);
                _piedPiper.RegisterPackRats(typeof(LogEntry).Assembly);

                ApiClient apiClient = new ApiClient(options.BaseUri, _piedPiper, _apiClientLogger, null);
                long nextOffset = GetNextOffset(options.Story, connection);
                _storyListener = apiClient.GetStoryListener(1000, TimeSpan.FromSeconds(5), options.Story, nextOffset);
                _storyListener.SomethingHappenedAsync += (sender, e) =>
                {
                    SaveToDatabase(connection, e.Thing, options.Story);
                    return Task.CompletedTask;
                };
                _storyListener.StartListening();

                while (true)
                    Thread.Sleep(TimeSpan.FromSeconds(3));
            }
        }

        protected override void OnCancelKeyPress()
        {
            _storyListener?.StopListening();
        }

        private void EnsureDatabaseSchema(SqlConnection connection, string storyId)
        {
            // Ensure database schema
            ExecuteNonQueryCommand(connection, "CreateBookmarkTable.sql");
            ExecuteNonQueryCommand(connection, "CreateLogEntryTable.sql");
            ExecuteNonQueryCommand(connection, "CreateLogEntryPropertyTable.sql");
            ExecuteNonQueryCommand(connection, "CreateGetLogEntriesWithPropertiesProc.sql");

            // Check if a row with the given StoryId exists in the Bookmark table
            string checkRowSql = "SELECT COUNT(*) FROM Bookmark WHERE StoryId = @StoryId";
            using (SqlCommand cmd = new SqlCommand(checkRowSql, connection))
            {
                cmd.Parameters.AddWithValue("@StoryId", storyId);

                int count = (int)cmd.ExecuteScalar();
                if (count == 0)
                {
                    // Insert a new row with NextOffset = 0
                    string insertRowSql = "INSERT INTO Bookmark (StoryId, NextOffset) VALUES (@StoryId, 0)";
                    using (SqlCommand insertCmd = new SqlCommand(insertRowSql, connection))
                    {
                        insertCmd.Parameters.AddWithValue("@StoryId", storyId);
                        insertCmd.ExecuteNonQuery();
                    }
                }
            }
        }

        private void SaveToDatabase(SqlConnection connection, StoryThing thing, string storyId)
        {
            ModelWithSchema? modelWithSchema = _piedPiper?.DecodeModel<ModelWithSchema>(thing.Thing, CoreSchema.ModelWithSchema);
            if (modelWithSchema == null)
                return;

            LogEntry? logEntry = modelWithSchema.Model as LogEntry;
            if (logEntry != null)
            {
                long offset = thing.Offset;
                if (!DateTime.TryParse(GetPropertyValue(logEntry, "__Timestamp__"), out DateTime timestamp))
                    timestamp = DateTime.Parse("1976-06-23");
                string originalFormat = GetPropertyValue(logEntry, "{OriginalFormat}");

                using (var transaction = connection.BeginTransaction())
                {
                    var insertLogEntry = @"
						INSERT INTO LogEntry (StoryId, Offset, Timestamp, OriginalFormat, LogName, EventId, EventName, Level, Message)
						VALUES (@StoryId, @Offset, @Timestamp, @OriginalFormat, @LogName, @EventId, @EventName, @Level, @Message)";

                    var updateBookmark = @"
						UPDATE Bookmark SET NextOffset = NextOffset + 1
						WHERE StoryId = @StoryId AND NextOffset = @ExpectedOffset";

                    using (var command = new SqlCommand(insertLogEntry, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@StoryId", storyId);
                        command.Parameters.AddWithValue("@Offset", offset);
                        command.Parameters.AddWithValue("@Timestamp", timestamp);
                        command.Parameters.AddWithValue("@OriginalFormat", originalFormat);
                        command.Parameters.AddWithValue("@LogName", logEntry.LogName);
                        command.Parameters.AddWithValue("@EventId", logEntry.EventId);
                        command.Parameters.AddWithValue("@EventName", logEntry.EventName != null ? logEntry.EventName : DBNull.Value);
                        command.Parameters.AddWithValue("@Level", logEntry.Level);
                        command.Parameters.AddWithValue("@Message", logEntry.Message);
                        command.ExecuteNonQuery();
                    }

                    // Insert LogEntryProperty
                    var insertLogEntryProperty = @"
                INSERT INTO LogEntryProperty (StoryId, Offset, Name, Value)
                VALUES (@StoryId, @Offset, @Name, @Value)";

                    // Assuming logEntry.Properties is the dictionary containing your properties
                    foreach (LogEntryProperty logEntryProperty in logEntry.Properties)
                    {
                        using (var command = new SqlCommand(insertLogEntryProperty, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@StoryId", storyId);
                            command.Parameters.AddWithValue("@Offset", thing.Offset);
                            command.Parameters.AddWithValue("@Name", logEntryProperty.Name);
                            command.Parameters.AddWithValue("@Value", logEntryProperty.Value);
                            command.ExecuteNonQuery();
                        }
                    }

                    using (var command = new SqlCommand(updateBookmark, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@StoryId", storyId);
                        command.Parameters.AddWithValue("@ExpectedOffset", thing.Offset);
                        if (command.ExecuteNonQuery() == 0)
                        {
                            transaction.Rollback();
                            return;
                        }
                    }

                    transaction.Commit();
                }
            }
        }

        private long GetNextOffset(string storyId, SqlConnection connection)
        {
            string sql = "SELECT NextOffset FROM Bookmark WHERE StoryId = @StoryId";

            using (SqlCommand cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@StoryId", storyId);

                object result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    return (long)result;
                }
                else
                {
                    // Handle the scenario where the storyId doesn't have an associated offset.
                    // This could potentially throw an error, or you might decide on a default behavior.
                    throw new Exception($"No offset found for Story ID {storyId}.");
                }
            }
        }

        private string ReadResourceFile(string resourceFileName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"BigRedProf.Stories.StoriesCli.Resources.{resourceFileName}";

            using (Stream stream = assembly.GetManifestResourceStream(resourceName)!)
            using (StreamReader reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        private void ExecuteNonQueryCommand(SqlConnection connection, string resourceFileName)
        {
            string sql = ReadResourceFile(resourceFileName);
            using (SqlCommand command = new SqlCommand(sql, connection))
            {
                command.ExecuteNonQuery();
            }
        }

        private string GetPropertyValue(LogEntry logEntry, string propertyName)
        {
            foreach(LogEntryProperty property in logEntry.Properties)
            {
                if (property.Name == propertyName)
                    return property.Value;
            }

            return string.Empty;
        }
    }
}
