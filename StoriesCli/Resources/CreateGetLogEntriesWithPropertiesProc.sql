IF NOT EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'GetLogEntriesWithProperties')
BEGIN
    EXEC('CREATE PROCEDURE [dbo].[GetLogEntriesWithProperties]
        AS
        BEGIN
            -- Drop the global temp table if it exists
            IF OBJECT_ID(''tempdb..##LogEntriesWithProperties'') IS NOT NULL DROP TABLE ##LogEntriesWithProperties;

            DECLARE @columns NVARCHAR(MAX) = '''';
            DECLARE @sql     NVARCHAR(MAX) = '''';

            SELECT @columns += ''['' + __Property__Name__ + ''],''
            FROM (SELECT DISTINCT Name AS __Property__Name__ FROM LogEntryProperty) AS Names
            ORDER BY __Property__Name__;

            IF LEN(@columns) > 0 
                SET @columns = LEFT(@columns, LEN(@columns) - 1);
            ELSE 
                PRINT ''No distinct __Property__Name__ found in LogEntryProperty. Cannot proceed with PIVOT.'';

            IF LEN(@columns) > 0
            BEGIN
                SET @sql = ''
                SELECT le.*, '' + @columns + '' INTO ##LogEntriesWithProperties
                FROM LogEntry le
                LEFT JOIN (
                    SELECT StoryId, Offset, '' + @columns + '' 
                    FROM (
                        SELECT StoryId, Offset, Name AS __Property__Name__, Value
                        FROM LogEntryProperty
                    ) AS SourceTable
                    PIVOT (
                        MAX(Value) FOR __Property__Name__ IN ('' + @columns + '')
                    ) AS PivotTable
                ) AS p ON le.StoryId = p.StoryId AND le.Offset = p.Offset
                '';

                EXEC sp_executesql @sql;
            END
        END
    ');
END
