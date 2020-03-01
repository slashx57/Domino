using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DominoX10;

namespace Test
{
    class Program
    {
        private static Logger logger = LogManager.GetLogger("DominoX10");

        static void Main(string[] args)
        {
            // NOTE: To disable debug output uncomment the following two lines
            //LogManager.Configuration.LoggingRules.RemoveAt(0);
            //LogManager.Configuration.Reload();
            logger.Info("XTenLib test program");
            var x10 = new DmnX10();
            // Listen to XTenManager events
            x10.ConnectionStatusChanged += X10_ConnectionStatusChanged;
            x10.ModuleChanged += X10_ModuleChanged;
            x10.PlcAddressReceived += X10_PlcAddressReceived;
            x10.PlcFunctionReceived += X10_PlcFunctionReceived;
            // These RF events are only used for CM15
            x10.RfDataReceived += X10_RfDataReceived;
            x10.RfCommandReceived += X10_RfCommandReceived;
            x10.RfSecurityReceived += X10_RfSecurityReceived;
            // Setup X10 interface. For CM15 set PortName = "USB"; for CM11 use serial port path intead (eg. "COM7" or "/dev/ttyUSB0")
            x10.PortName = "/dev/ttyS0";
            x10.HouseCode = "B";
            // Connect to the interface
            logger.Info("Connecting to {0}",x10.PortName);
            x10.Connect();
            while (!x10.IsConnected)
                Thread.Sleep(1000);
            
            string command = "";
            var mod = x10.Modules["B13"];
            //mod.GetStatus();
            //mod.Off();
            mod.ShOpen(100);

            return;
            while (command != "!")
            {
                Console.WriteLine("[0] Unit Status");
                Console.WriteLine("[1] Unit On");
                Console.WriteLine("[2] Unit Off");
                Console.WriteLine("[3] Unit Level");
                Console.WriteLine("[4] Unit Dim");
                Console.WriteLine("[5] Unit Bright");
                Console.WriteLine("[6] Unit Shopen 1");
                Console.WriteLine("[7] Unit Shopen 20");
                Console.WriteLine("[!] Exit");
                Console.WriteLine("Status ="+(x10.IsConnected?"connected":"disconnected"));
                Console.WriteLine("\nEnter option and hit [enter]:");
                command = Console.ReadLine();
                /*string[] hexValuesSplit = command.Split(' ');
                byte[] btab = new byte[hexValuesSplit.Count()];
                int i = 0;
                foreach (String hex in hexValuesSplit)
                {
                    // Convert the number expressed in base-16 to an integer.
                    int value = Convert.ToInt32(hex, 16);
                    btab[i] = (byte)value;
                    i++;
                }
                x10.SendMessage(btab);*/
                switch (command)
                {
                    case "0":
                        // status
                        mod.GetStatus();
                        break;
                    case "1":
                        // Turn On
                        mod.On();
                        break;
                    case "2":
                        // Turn Off
                        mod.Off();
                        break;
                    case "3":
                        // 
                        Console.WriteLine("level=" + mod.Level); ;
                        break;
                    case "4":
                        // 
                        mod.Dim();
                        break;
                    case "5":
                        // 
                        mod.Bright();
                        break;
                    case "6":
                        // 
                        mod.ShOpen(1);
                        break;
                    case "7":
                        // 
                        mod.ShOpen(20);
                        break;
                }
            }

            // Disconnect the interface
            x10.Disconnect();
        }

      
        static void X10_ConnectionStatusChanged(object sender, ConnectionStatusChangedEventArgs args)
        {
            logger.Info("Interface connection status connected={0}", args.Connected);
        }

        static void X10_ModuleChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            var module = sender as X10Module;
            logger.Info("Module property changed: {0} {1} = {2}", module.Code, e.PropertyName, module.Level);
        }

        static void X10_PlcAddressReceived(object sender, PlcAddressReceivedEventArgs args)
        {
            logger.Info("PLC address received: HouseCode {0} Unit {1}", args.HouseCode, args.UnitCode);
        }

        static void X10_PlcFunctionReceived(object sender, PlcFunctionReceivedEventArgs args)
        {
            logger.Info("PLC function received: Command {0} HouseCode {1}", args.Command, args.HouseCode);
        }

        static void X10_RfDataReceived(object sender, RfDataReceivedEventArgs args)
        {
            logger.Info("RF data received: {0}", BitConverter.ToString(args.Data));
        }

        static void X10_RfCommandReceived(object sender, RfCommandReceivedEventArgs args)
        {
            logger.Info("Received RF command {0} House Code {1} Unit {2}", args.Command, args.HouseCode, args.UnitCode);
        }

        static void X10_RfSecurityReceived(object sender, RfSecurityReceivedEventArgs args)
        {
            logger.Info("Received RF Security event {0} from address {1}", args.Event, args.Address.ToString("X3"));
        }
    }
}