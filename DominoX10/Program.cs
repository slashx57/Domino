using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NLog;
using DominoX10.Drivers;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace DominoX10
{
	class Program
	{
		internal static Logger logger = LogManager.GetLogger("DominoX10");
		public static int status = 0;
		public static X10Main manager;

		static void Main(string[] args)
		{
			status = 1;

			AssemblyLoadContext.Default.Unloading += SigTermEventHandler; //register sigterm event handler. Don't forget to import System.Runtime.Loader!
			Console.CancelKeyPress += CancelHandler; //register sigint event handler
			AppDomain.CurrentDomain.UnhandledException += CurrentDomainUnhandledException;

			X10Main.logger = logger;
			manager = new X10Main();
			manager.Open();
			status = 2;

			while (status > 0)
			{
				Thread.Sleep(1000);
			}

			status = -1;
			manager.Close();
		}

		private static void SigTermEventHandler(AssemblyLoadContext obj)
		{
			logger.Debug("Unloading...");
			Console.ReadLine();
		}

		private static void CancelHandler(object sender, ConsoleCancelEventArgs e)
		{
			logger.Debug("Exiting...");
			status = -1;
			Console.ReadLine();
		}

		static void CurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			if (e.IsTerminating)
				logger.Fatal(e.ExceptionObject as Exception, "Unhandled exception. Terminating application!");
			else
				logger.Error(e.ExceptionObject as Exception, "Unhandled exception");
		}

	}
}
