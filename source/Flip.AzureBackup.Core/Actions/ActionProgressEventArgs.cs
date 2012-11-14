﻿namespace Flip.AzureBackup.Actions
{
	public sealed class ActionProgressEventArgs
	{
		public ActionProgressEventArgs(string fileFullPath, string message, decimal fraction)
		{
			this.FileFullPath = fileFullPath;
			this.Message = message;
			this.Fraction = fraction;
		}

		public string FileFullPath { get; private set; }
		public string Message { get; private set; }
		public decimal Fraction { get; private set; }
	}
}
