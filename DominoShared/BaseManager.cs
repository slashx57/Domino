/*
    This file is part of Domino source code.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

/*
 *     Author: slashx57
 *     Project Homepage: https://github.com/slashx57/domino
 */

using Microsoft.Extensions.Configuration;
using MQTTnet;
using MQTTnet.Extensions.ManagedClient;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DominoShared
{

	public partial class BaseManager : IDisposable
    {
        public static Logger logger { get; set; }
        protected Thread reader;
        protected Thread writer;
        protected CancellationTokenSource Readercts;
        protected CancellationTokenSource Writercts;
        protected object accessLock = new object();
		public static IConfigurationRoot Configuration;

		public int currentStatus { get; set; }  //to detect status changed

		protected readonly IConfiguration config;
        protected static object _lockObject = new object();
        public bool devMode = false; // in development mode?
        public string EnvironmentName { get; private set;}
        public string AppPath { get; private set; }

		public delegate void StatusChangedEventHandler(object sender, StatusChangedEventArgs args);
		public event StatusChangedEventHandler StatusChanged;
		public class StatusChangedEventArgs
		{
			public readonly int Status;
			public StatusChangedEventArgs(int status)
			{
				Status = status;
			}
		}

		public BaseManager()
		{
			Assembly asm = Assembly.GetExecutingAssembly();
			Configuration = LoadConfig(asm);

			this.MQTTDns = Configuration["Main:MQTTDns"];
			this.MQTTUser = Configuration["Main:MQTTUser"];
			this.MQTTPassword = Configuration["Main:MQTTPwd"];
		}

		/// <summary>
		/// Load configuration (Nlog, settings) for current environment
		/// </summary>
		/// <param name="asm"></param>
		/// <returns></returns>
		public virtual IConfigurationRoot LoadConfig(Assembly asm)
        {
			Console.OutputEncoding = Encoding.UTF8;
			Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("en-US");
			Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");

			string EnvironmentName = Environment.GetEnvironmentVariable("NETCORE_ENVIRONMENT");
            if (string.IsNullOrEmpty(EnvironmentName))  //Production mode by default 
                EnvironmentName = "Production";
            devMode = EnvironmentName.Equals("Development");

            AppPath =  Path.GetFullPath(AppContext.BaseDirectory);

            string NLogConfig = Path.GetFullPath(Path.Combine(AppPath, $"nlog.{EnvironmentName}.config"));
            LogManager.LoadConfiguration(NLogConfig);

            logger.Debug("Running on " + EnvironmentName + " mode");
            logger.Debug("AppPath :" + AppPath);
            logger.Debug("Log file :" + NLogConfig);

            var builder = new ConfigurationBuilder()
             .SetBasePath(AppPath)
             .AddJsonFile("mainsettings.json", optional: false, reloadOnChange: true)
             .AddJsonFile($"mainsettings.{EnvironmentName}.json", optional: true)
             .AddJsonFile(Path.Combine(AppPath, "appsettings.json"), optional: false, reloadOnChange: true)
             .AddJsonFile(Path.Combine(AppPath, $"appsettings.{EnvironmentName}.json"), optional: true);            

            if (devMode)
            {
                // UserSecrets are stored in 
                // %APPDATA% \ microsoft \ UserSecrets \ secrets.json
                // ~/.microsoft/usersecrets//secrets.json
                builder.AddUserSecrets(asm);
            }

			return builder.Build();
        }

        public void Dispose()
        {
            if (Readercts != null)
            {
                Readercts.Cancel();
                Readercts.Dispose();
                Readercts = null;
            }
            if (Readercts != null)
            {
                Writercts.Cancel();
                Writercts.Dispose();
                Writercts = null;
            }
        }

        protected virtual void OnStatusChanged(StatusChangedEventArgs args)
        {
            if (StatusChanged != null && currentStatus != args.Status)
            {
                StatusChanged(this, args);
                currentStatus = args.Status;
            }
        }

	}
}

