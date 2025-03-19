using System;
using System.ComponentModel.DataAnnotations;
using CommandLine;
using Microsoft.Extensions.Logging;

namespace BigRedProf.Stories.StoriesCli
{
    [Verb("synclogs2sql", HelpText = "Continuously synchronize story logs to SQL Server.")]
    public class SyncLogsToSqlOptions : BaseCommandLineOptions
	{
        [Option("sqlServer", Required = false, HelpText = "The SQL Server server.")]
        public string SqlServer { get; set; } = default!;

        [Option("sqlDatabase", Required = false, HelpText = "The SQL Server database.")]
        public string SqlDatabase { get; set; } = default!;

        [Option("sqlUsername", Required = false, HelpText = "The SQL Server username.")]
        public string SqlUsername { get; set; } = default!;

        [Option('p', "sqlPassword", Required = false, HelpText = "The SQL Server password.")]
        public string SqlPassword { get; set; } = default!;

        [Option("sqlTrustServerCertificate", Required = false, HelpText = "Disables SSL Verification on the SQL Server.")]
        public bool TrustServerCertificate { get; set; } = false;
    }
}
