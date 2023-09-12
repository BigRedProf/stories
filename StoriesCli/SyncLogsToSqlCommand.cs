using BigRedProf.Data;
using BigRedProf.Stories.Internal;
using BigRedProf.Stories.Internal.ApiClient;
using BigRedProf.Stories.Models;
using BigRedProf.Stories.Logging.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;

namespace BigRedProf.Stories.StoriesCli
{
	public class SyncLogsToSqlCommand : Command
	{
		private IPiedPiper? _piedPiper;
		private IStoryListener? _storyListener;

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
				EnsureDatabaseTables(connection, commandLineOptions.Story);

				// Setup the story listener just like ListenCommand
				_piedPiper = new PiedPiper();
				_piedPiper.RegisterDefaultPackRats();
				_piedPiper.RegisterPackRats(typeof(StoryThing).Assembly);
				_piedPiper.RegisterPackRats(typeof(LogEntry).Assembly);

				ApiClient apiClient = new ApiClient(options.BaseUri, _piedPiper);
				long nextOffset = GetNextOffset(options.Story, connection);
				_storyListener = apiClient.GetStoryListener(options.Story, nextOffset, 1000, TimeSpan.FromSeconds(5));
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

        private void EnsureDatabaseTables(SqlConnection connection, string storyId)
        {
            // Check if tables exist and create if not.
            // Simplified for brevity; consider using Dapper or Entity Framework for real-world scenarios.
            var createLogEntryTable = @"
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LogEntry')
        BEGIN
            CREATE TABLE LogEntry (
                LogName NVARCHAR(255),
                EventId INT,
                EventName NVARCHAR(255) NULL,
                Level INT,
                Message NVARCHAR(MAX),
                StoryID NVARCHAR(255)
            )
        END";

            var createBookmarkTable = @"
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Bookmark')
        BEGIN
            CREATE TABLE Bookmark (
                StoryID NVARCHAR(255) PRIMARY KEY,
                NextOffset BIGINT
            )
        END";

            using (var command = new SqlCommand(createLogEntryTable, connection))
            {
                command.ExecuteNonQuery();
            }

            using (var command = new SqlCommand(createBookmarkTable, connection))
            {
                command.ExecuteNonQuery();
            }

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
			Console.WriteLine($"** SaveToDatabase, offset={thing.Offset}");

			var logEntry = _piedPiper?.DecodeModelWithSchema(thing.Thing).Model as LogEntry;
			if (logEntry != null)
			{
				using (var transaction = connection.BeginTransaction())
				{
					var insertLogEntry = @"
						INSERT INTO LogEntry (LogName, EventId, EventName, Level, Message, StoryID)
						VALUES (@LogName, @EventId, @EventName, @Level, @Message, @StoryID)";

					var updateBookmark = @"
						UPDATE Bookmark SET NextOffset = NextOffset + 1
						WHERE StoryID = @StoryID AND NextOffset = @ExpectedOffset";

					using (var command = new SqlCommand(insertLogEntry, connection, transaction))
					{
						command.Parameters.AddWithValue("@LogName", logEntry.LogName);
						command.Parameters.AddWithValue("@EventId", logEntry.EventId);
						command.Parameters.AddWithValue("@EventName", logEntry.EventName != null ? logEntry.EventName : DBNull.Value);
						command.Parameters.AddWithValue("@Level", logEntry.Level);
						command.Parameters.AddWithValue("@Message", logEntry.Message);
						command.Parameters.AddWithValue("@StoryID", storyId);
						command.ExecuteNonQuery();
					}

					using (var command = new SqlCommand(updateBookmark, connection, transaction))
					{
						command.Parameters.AddWithValue("@StoryID", storyId);
						command.Parameters.AddWithValue("@ExpectedOffset", thing.Offset);
						if (command.ExecuteNonQuery() == 0)
						{
                            Console.WriteLine($"** rollback");
                            transaction.Rollback();
							return;
						}
					}

                    Console.WriteLine($"** commit");
                    transaction.Commit();
				}
			}
		}

        private long GetNextOffset(string storyId, SqlConnection connection)
        {
            string sql = "SELECT NextOffset FROM Bookmark WHERE StoryID = @StoryId";

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
                    throw new Exception($"No offset found for StoryID {storyId}.");
                }
            }
        }

    }
}
