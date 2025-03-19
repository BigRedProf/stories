-- Create LogEntry table if it does not exist
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LogEntry')
BEGIN
    CREATE TABLE LogEntry (
        StoryId NVARCHAR(128) NOT NULL,
        Offset BIGINT NOT NULL,
        Timestamp DATETIME2(7) NOT NULL,
        OriginalFormat NVARCHAR(2048) NOT NULL,
        LogName NVARCHAR(255),
        EventId INT,
        EventName NVARCHAR(255),
        Level INT,
        Message NVARCHAR(2048),
        PRIMARY KEY (StoryId, Offset)
    )

    -- Create indexes
    CREATE NONCLUSTERED INDEX IDX_LogEntry_StoryId ON LogEntry(StoryId);
    CREATE NONCLUSTERED INDEX IDX_LogEntry_Offset ON LogEntry(Offset);
    CREATE NONCLUSTERED INDEX IDX_LogEntry_Timestamp ON LogEntry(Timestamp);
    CREATE NONCLUSTERED INDEX IDX_LogEntry_OriginalFormat ON LogEntry(OriginalFormat);
    CREATE NONCLUSTERED INDEX IDX_LogEntry_LogName ON LogEntry(LogName);
    CREATE NONCLUSTERED INDEX IDX_LogEntry_EventId ON LogEntry(EventId);
    CREATE NONCLUSTERED INDEX IDX_LogEntry_EventName ON LogEntry(EventName);
    CREATE NONCLUSTERED INDEX IDX_LogEntry_Level ON LogEntry(Level);
    CREATE NONCLUSTERED INDEX IDX_LogEntry_Message ON LogEntry(Message);
END
