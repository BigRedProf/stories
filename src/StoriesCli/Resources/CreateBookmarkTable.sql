-- Create Bookmark table if it does not exist
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Bookmark')
BEGIN
    CREATE TABLE Bookmark (
        StoryId NVARCHAR(128) PRIMARY KEY,
        NextOffset BIGINT
    )
END
