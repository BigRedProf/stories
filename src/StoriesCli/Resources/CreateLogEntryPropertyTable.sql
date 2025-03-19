-- Create LogEntryProperty table if it does not exist
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LogEntryProperty')
BEGIN
    CREATE TABLE LogEntryProperty (
        StoryId NVARCHAR(128) NOT NULL,
        Offset BIGINT NOT NULL,
        Name NVARCHAR(255) NOT NULL,
        Value NVARCHAR(MAX),
        PRIMARY KEY (StoryId, Offset, Name),
        FOREIGN KEY (StoryId, Offset) REFERENCES LogEntry(StoryId, Offset)
    )
END

