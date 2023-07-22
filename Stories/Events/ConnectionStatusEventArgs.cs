using BigRedProf.Data;
using System;

namespace BigRedProf.Stories.Events
{
	public class ConnectionStatusEventArgs : EventArgs
	{
		#region constructors
		public ConnectionStatusEventArgs(string status, string? message, Exception? exception)
		{
			Status = status;
			Message = message;
			Exception = exception;
		}
		#endregion

		#region properties
		public string Status
		{
			get;
			private set;
		}

		public string? Message
		{
			get;
			private set;
		}

		public Exception? Exception
		{
			get;
			private set;
		}
		#endregion
	}
}
