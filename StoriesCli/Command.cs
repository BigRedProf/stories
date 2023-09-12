namespace BigRedProf.Stories.StoriesCli
{
	abstract public class Command
	{
		#region constructors
		protected Command()
		{
			Console.CancelKeyPress += Console_CancelKeyPress;
		}
		#endregion

		#region methods
		abstract public int Run(BaseCommandLineOptions options);
		abstract protected void OnCancelKeyPress();
		#endregion

		#region event handlers
		private void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
		{
			OnCancelKeyPress();
		}
		#endregion
	}
}
