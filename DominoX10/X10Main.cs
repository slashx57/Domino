/*
    This file is part of Domino source code as fork of XTenLib source code.

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

/*     DominoX10
 *     Author: slashx57
 *     Project Homepage: https://github.com/slashx57/domino
 */

/*     For XTenLib
*     Author: Generoso Martello <gene@homegenie.it>
*     Project Homepage: https://github.com/genielabs/x10-lib-dotnet
*/
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NLog;
using DominoX10.Drivers;
using System.IO;
using System.Reflection;
using DominoShared;

namespace DominoX10
{
    public class X10Main : BaseManager
    {
        private Dictionary<string, X10Module> modules = new Dictionary<string, X10Module>();
        private string monitoredHouseCode = "A";

        // Variables for storing last addressed house codes and optmizing/speeding up X10 communication
        private List<X10Module> addressedModules = new List<X10Module>();
        private bool newAddressData = true;

        // State variables
        private byte expectedChecksum = 0x00;
        private X10CommState communicationState = X10CommState.Ready;

        // Max resend attempts when a X10 command failed
        private const int commandResendMax = 3;
        // Max wait for command acknowledge
        private double commandTimeoutSeconds = 5.0;
        // Store last X10 message (used to resend on error)
        private byte[] commandLastMessage = new byte[0];
        private int commandSendAttempts = 0;
        // I/O operation lock / monitor
        private object waitAckMonitor = new object();
        private object commandLock = new object();

        // Timestamps used for detecting communication timeouts
        private DateTime waitResponseTimestamp = DateTime.Now;
        private DateTime lastReceivedTs = DateTime.Now;
        // Variables used for preventing duplicated messages coming from RF
        private DateTime lastRfReceivedTs = DateTime.Now;
        private string lastRfMessage = "";
        private uint minRfRepeatDelayMs = 500;

        string itf = "CM11";

        // X10 objects and configuration
        private X10Interface x10interface;
        private string portName = "USB";

        // State variables
        private bool isInterfaceReady = false;

        // Read/Write error state variable
        private bool gotReadWriteError = true;

		//protected bool disconnectRequested = false;


		// This is used on Linux/Mono for detecting when the link gets disconnected
		//private int zeroChecksumCount = 0;

		/// <summary>
		/// Occurs when an X10 module changed.
		/// </summary>
		public event PropertyChangedEventHandler ModuleChanged;

        /// <summary>
        /// Occurs when plc address received.
        /// </summary>
        public event PlcAddressReceivedEventHandler PlcAddressReceived;

        /// <summary>
        /// Occurs when plc command received.
        /// </summary>
        public event PlcFunctionReceivedEventHandler PlcFunctionReceived;

        /// <summary>
        /// Occurs when RF data is received.
        /// </summary>
        public event RfDataReceivedEventHandler RfDataReceived;

        /// <summary>
        /// Occurs when x10 command received.
        /// </summary>
        public event X10CommandReceivedEventHandler RfCommandReceived;

        /// <summary>
        /// Occurs when x10 security data is received.
        /// </summary>
        public event X10SecurityReceivedEventHandler RfSecurityReceived;

        /// <summary>
        /// Gets or sets the house codes. This string is a comma separated list of house codes (eg. "A,B,O")
        /// </summary>
        /// <value>The house code.</value>
        public string HouseCode
        {
            get { return monitoredHouseCode; }
            set
            {
                monitoredHouseCode = value;
                for (int i = 0; i < modules.Keys.Count; i++)
                {
                    modules[modules.Keys.ElementAt(i)].PropertyChanged -= Module_PropertyChanged;
                }
                modules.Clear();

                string[] hc = monitoredHouseCode.Split(',');
                for (int i = 0; i < hc.Length; i++)
                {
                    for (int uc = 1; uc <= 16; uc++)
                    {
                        var module = new X10Module(this, hc[i] + uc.ToString());
                        module.PropertyChanged += Module_PropertyChanged;
                        modules.Add(hc[i] + uc.ToString(), module);
                    }
                }

                if (/*!gotReadWriteError &&*/ itf=="CM15")
                {
                    InitializeCm15();
                }
            }
        }

        /// <summary>
        /// Gets or sets the name of the port. This can be "USB" when using CM15 hardware or the
        /// serial port address if using CM11 (eg. "COM7" on Windows or "/dev/ttyUSB1" on Linux).
        /// </summary>
        /// <value>The name of the port.</value>
        public string PortName
        {
            get { return portName; }
            set
            {
                if (portName != value)
                {
                    // set to erro so that the connection watcher will reconnect
                    // using the new port
                    Close();
                    // instantiate the requested interface
                    if (value.ToUpper() == "USB")
                    {
                        x10interface = new CM15();
                    }
                    else
                    {
                        x10interface = new CM11(value);
                    }
                }
                portName = value;
            }
        }

        /// <summary>
        /// Gets the list of all X10 modules or a specific module (eg. var modA5 = x10lib.Modules["A5"]).
        /// </summary>
        /// <value>The modules.</value>
        public Dictionary<string, X10Module> Modules
        {
            get { return modules; }
        }

        /// <summary>
        /// Gets the addressed modules.
        /// </summary>
        /// <value>The addressed modules list.</value>
        public List<X10Module> AddressedModules
        {
            get { return addressedModules; }
        }

		public X10Main() : base()
        {
			this.MQTTRoot = Configuration["DmnX10:MQTTRoot"];

			// Setup X10 interface. 
			//  For CM15 set PortName = "USB"; 
			//  for CM11 use serial port path intead (eg. "COM7" or "/dev/ttyUSB0")
			PortName = Configuration["DmnX10:PortName"];
            
            //// Default interface is CM15: use "PortName" property to set a different interface driver
            //x10interface = new CM15();

            // Default house code is set to B
            HouseCode = "B";

            StatusChanged += (s, args) => {
                Program.logger.Debug("status changed to : " + args.Status);
            };
            ModuleChanged += (s, args) =>
            {
                var module = s as X10Module;
                if (args.PropertyName == "Level")
                {
					Publish(module.Code + "/" + args.PropertyName, (100*module.Level).ToString("F0"));
                }
                else
                {
					Program.logger.Info("Module property changed: {0} {1} = {2}", module.Code, args.PropertyName, module.Level);
					Publish(module.Code + "/" + args.PropertyName, module.Level.ToString("F2"));
                }
            };

            //PlcAddressReceived += X10_PlcAddressReceived;
            //PlcFunctionReceived += X10_PlcFunctionReceived;
            //// These RF events are only used for CM15
            //RfDataReceived += X10_RfDataReceived;
            //RfCommandReceived += X10_RfCommandReceived;
            //RfSecurityReceived += X10_RfSecurityReceived;
            //HWManager.x10.DecodeRX(new byte[] { 0x03, 0x02, 0xE2, 0xE2 });
            //HWManager.x10.DecodeRX(new byte[] { 0x03, 0x02, 0xEE, 0xE2 });
            //HWManager.x10.DecodeRX(new byte[] { 0x09, 0x11, 0xE7, 0x0E, 0x19, 0x01, 0xE7, 0x02, 0x19, 0x01 });
        }

		public bool Open()
        {
			startMQTT();

			lock (accessLock)
            {
                Close();
                Program.logger.Info("Connecting to {0}", PortName);
                bool success = (x10interface != null && x10interface.Open());
                if (success)
                {
                    //SendMessage(new byte[] { (byte)X10CommandType.PC_StatusRequest },false);
                    //byte[] reqData = new byte[] { (byte)X10CommandType.PC_StatusRequest };
                    communicationState = X10CommState.WaitingStatus;
					Program.logger.Debug("X10 connected, send status request, state:"+ communicationState);
                    OnStatusChanged(new StatusChangedEventArgs((int)X10CommState.WaitingStatus));
                    x10interface.WriteData(new byte[] { (byte)X10CommandType.PC_StatusRequest });

                    if (x10interface.GetType().Equals(typeof(CM15)))
                    {
                        // Set transceived house codes for CM15 X10 RF-->PLC
                        //InitializeCm15();
                        // For CM15 we do not need to receive ACK message to claim status as connected
                        OnStatusChanged(new StatusChangedEventArgs((int)X10CommState.Connected));
                    }
                    gotReadWriteError = false;
                    // Start the tasks
                    Readercts = new CancellationTokenSource();
                    reader = new Thread(new ParameterizedThreadStart(ReaderTask));
                    reader.Name = "X10.reader";
                    reader.Start(Readercts.Token);
                    //Writercts = new CancellationTokenSource();
                    //writer = new Thread(WriterTask);
                    //writer = new Thread(new ParameterizedThreadStart(WriterTask));
                    //writer.Start(Writercts.Token);
                }
                else
                {
					Program.logger.Info("unable to connect");
                    return false;
                }
            }
            return (x10interface != null &&
                !Readercts.IsCancellationRequested &&
                (isInterfaceReady || (!gotReadWriteError && x10interface.GetType().Equals(typeof(CM15)))));
        }

        public void Close()
        {
			//UnselectModules();
			lock (accessLock)
            {
                // Dispose the X10 interface
                try
                {
                    if (x10interface != null)
                    {
						Program.logger.Debug("closing connection");
                        x10interface.Close();
                    }
                }
                catch (Exception e)
                {
					Program.logger.Error(e);
                }
                gotReadWriteError = true;
                // Stop the Reader task
                //if (reader != null)
                //{
                //    if (!reader.Join(5000))
                //        throw new Exception("The reader refused to stop"); //reader.Abort();
                //    reader = null;
                //}
                OnStatusChanged(new StatusChangedEventArgs((int)X10CommState.Disconnected));
            }
        }

		public override void OnMqttMessageReceived(string topic, string payload)
		{
			string[] levels = topic.Split('/');

			if (levels.Count() >= 3 && levels[2] == "shopen")
			{
				X10Module mod = Program.manager.Modules[levels[1]];
				int value = int.Parse(payload);

				// send shopen to the module
				mod.ShOpen(value);
				// send status 
				Publish(levels[1] + "/status", value.ToString(), true);
				return;
			}
			if (levels.Count() >= 3 && levels[2] == "bright")
			{
				X10Module mod = Program.manager.Modules[levels[1]];
				int value = int.Parse(payload);

				// send bright to the module
				mod.Bright(value);
				// send status 
				Publish(levels[1] + "/status", value.ToString(), true);
				return;
			}
		}


		/// <summary>
		/// Manager thread
		/// </summary>
		private void ReaderTask(object obj)
        {
            CancellationToken canceltoken = (CancellationToken)obj;
            while (!canceltoken.IsCancellationRequested)
            {

                //if (lmbx != null && lmbx.Count>0)
                byte[] readData = x10interface.ReadData();
                if (readData != null && readData.Length > 0)
                {
                    {
						//byte[] readData = mbx.message.Split('-').Select<string, byte>(s => Convert.ToByte(s, 16)).ToArray();
						Program.logger.Debug("receive :" + BitConverter.ToString(readData) + ", state:" + communicationState);
                        // last command succesfully sent
                        if (communicationState == X10CommState.WaitingReady && 
                            readData[0] == (int)X10CommandType.PLC_Ready && readData.Length == 1) // ready received
                        {
                            communicationState = X10CommState.Ready;
                            OnStatusChanged(new StatusChangedEventArgs((int)communicationState));
							Program.logger.Debug("Received : Ready, command successful, state:"+ communicationState);
                            SendMessage2(new byte[] { (byte)X10CommandType.PC_Ack });
                        }
                        else if (communicationState == X10CommState.WaitingStatus && readData.Length >= 9)
                        //(readData.Length == 2 && readData[0] == 0xFF && readData[1] == 0x00)) && !isInterfaceReady)
                        {
                            lastReceivedTs = DateTime.Now;
                            expectedChecksum = (byte)X10CommandType.EnableRingSig;
                            /*foreach (byte bmsg in readData)
                                expectedChecksum += bmsg;
                            expectedChecksum = (byte)(expectedChecksum & 0xff);*/

                            communicationState = X10CommState.WaitingChecksum;
							Program.logger.Debug("Received : Status, state:"+ communicationState);
                            OnStatusChanged(new StatusChangedEventArgs((int)communicationState));
                            SendMessage2(new byte[] { expectedChecksum });
                            //UpdateInterfaceTime(false);
                        }
                        else if (communicationState == X10CommState.WaitingChecksum && readData.Length == 1)
                        {
							// checksum is received only from CM11
							Program.logger.Debug("Received  : checksum {0}, expected {1} =>{2}", readData[0].ToString("X2"), expectedChecksum.ToString("X2"), (readData[0] == expectedChecksum) ? "ok" : "fail");
                            if (readData[0] == expectedChecksum)
                            {
                                communicationState = X10CommState.WaitingReady;
                                OnStatusChanged(new StatusChangedEventArgs((int)communicationState));
                                SendMessage2(new byte[] { (byte)X10CommandType.PC_Ack });
                            }
                            else
                                SendMessage2(commandLastMessage);
                        }
                        else if ((readData[0] == (int)X10CommandType.PLC_Poll) && readData.Length == 1)
                        {
                            lastReceivedTs = DateTime.Now;
                            SendMessage(new byte[] { (byte)X10CommandType.PC_Ready }, false);
                            communicationState = X10CommState.WaitingRX;
							Program.logger.Debug("Received : Polling, state:"+ communicationState);
                            OnStatusChanged(new StatusChangedEventArgs((int)communicationState));
                        }
                        else if ((readData[0] == (int)X10CommandType.PLC_FilterFail_Poll) && readData.Length == 1)
                        {
                            SendMessage(new byte[] { (int)X10CommandType.PLC_FilterFail_Poll }, false); // reply to filter fail poll
                            communicationState = X10CommState.Ready;
							Program.logger.Debug("Received : FilterFail_Poll, state:"+ communicationState);
                            OnStatusChanged(new StatusChangedEventArgs((int)communicationState));
                        }
                        else if (communicationState == X10CommState.WaitingRX)
                        {
							Program.logger.Debug("Received : RX");
                            lastReceivedTs = DateTime.Now;
                            DecodeRX(readData);
                            communicationState = X10CommState.Ready;
                            OnStatusChanged(new StatusChangedEventArgs((int)X10CommState.Ready));
                        }
                        else if (readData[0] == (int)X10CommandType.PLC_Macro)
                        {
							Program.logger.Debug("Received : Macro");
                            lastReceivedTs = DateTime.Now;
							Program.logger.Info("MACRO: {0}", BitConverter.ToString(readData));//macro triggered
                        }
                        else if (readData[0] == (int)X10CommandType.PLC_RF)
                        {
							Program.logger.Debug("Received : RF");
                            lastReceivedTs = DateTime.Now;
                            DecodeRF(readData);
                            communicationState = X10CommState.Ready;
                            OnStatusChanged(new StatusChangedEventArgs((int)X10CommState.Ready));
                        }
                        else if ((readData[0] == (int)X10CommandType.PLC_TimeRequest)) // on powerfail
                        {
							Program.logger.Info("Received : Time request from CMxx");
							communicationState = X10CommState.Ready;
							UpdateInterfaceTime(false);
                        }

                    }
                }

                Thread.Sleep(1000);
            }
        }

        #region X10 Commands Implementation

        /// <summary>
        /// Dim the specified module (housecode, unitcode) by the specified percentage.
        /// </summary>
        /// <param name="housecode">Housecode.</param>
        /// <param name="unitcode">Unitcode.</param>
        /// <param name="percentage">Percentage.</param>
        public void Dim(X10HouseCode housecode, X10UnitCode unitcode, int percentage)
        {
            lock (commandLock)
            {
				Program.logger.Debug("Command Dim HC=" + housecode.ToString() + " UC=" + unitcode.ToString() + " VAL=" + percentage.ToString() + "%");
                string huc = Utility.HouseUnitCodeFromEnum(housecode, unitcode);
                string hcfuntion = String.Format("{0:x1}{1:x1}", (int)housecode, (int)X10Command.Dim);
                SendModuleAddress(housecode, unitcode);
                if (itf=="CM15")
                {
                    double normalized = ((double)percentage / 100D);
                    SendMessage(new byte[] {
                        (int)X10CommandType.Function,
                        byte.Parse(hcfuntion, System.Globalization.NumberStyles.HexNumber),
                        (byte)(normalized * 210)
                    });
                    double newLevel = modules[huc].Level - normalized;
                    if (newLevel < 0)
                        newLevel = 0;
                    modules[huc].Level = newLevel;
                }
                else
                {
                    byte dimvalue = Utility.GetDimValue(percentage);
                    SendMessage(new byte[] {
                        (byte)((int)X10CommandType.Function | dimvalue | 0x04),
                        byte.Parse(hcfuntion, System.Globalization.NumberStyles.HexNumber)
                    });
                    double newLevel = modules[huc].Level - Utility.GetPercentageValue(dimvalue);
                    if (newLevel < 0)
                        newLevel = 0;
                    modules[huc].Level = newLevel;
                }
            }
        }
        /// <summary>
        /// Open the shutter module (housecode, unitcode) by the specified percentage.
        /// </summary>
        /// <param name="housecode">Housecode.</param>
        /// <param name="unitcode">Unitcode.</param>
        /// <param name="percentage">Percentage.</param>
        public void ShOpen(X10HouseCode housecode, X10UnitCode unitcode, int percentage)
        {
            lock (commandLock)
            {
				Program.logger.Debug("Command Shopen HC=" + housecode.ToString() + " UC=" + unitcode.ToString() + " VAL=" + percentage.ToString() + "%");
                //SendModuleAddress(housecode, unitcode);

                string hcfuntion = String.Format("{0:x1}{1:x1}", (int)housecode, (int)X10CommandType.ExtFunction);
                string hcUnit = String.Format("{0:X}{1:X}", (int)0, (int)unitcode);
                byte levelvalue = Utility.GetLevelValue(percentage);
                SendMessage(new byte[] {
                    (byte)X10CommandType.ExtFunction,
                    byte.Parse(hcfuntion, System.Globalization.NumberStyles.HexNumber),
                    byte.Parse(hcUnit, System.Globalization.NumberStyles.HexNumber),
                    levelvalue,
                    (byte)X10CommandExt.Shopen
                });
                string huc = Utility.HouseUnitCodeFromEnum(housecode, unitcode);
                modules[huc].Level = levelvalue;// percentage;
            }
        }

        /// <summary>
        /// Brighten the specified module (housecode, unitcode) by the specified percentage.
        /// </summary>
        /// <param name="housecode">Housecode.</param>
        /// <param name="unitcode">Unitcode.</param>
        /// <param name="percentage">Percentage.</param>
        public void Bright(X10HouseCode housecode, X10UnitCode unitcode, int percentage)
        {
            lock (commandLock)
            {
				Program.logger.Debug("Command Bright HC=" + housecode.ToString() + " UC=" + unitcode.ToString() + " VAL=" + percentage.ToString() + "%");
                string huc = Utility.HouseUnitCodeFromEnum(housecode, unitcode);
                //string hcunit = String.Format("{0:X}{1:X}", (int)housecode, (int)unitcode);
                string hcfuntion = String.Format("{0:x1}{1:x1}", (int)housecode, (int)X10Command.Bright);
                SendModuleAddress(housecode, unitcode);
                if (itf == "CM15")
                {
                    double normalized = ((double)percentage / 100D);
                    SendMessage(new byte[] {
                        (int)X10CommandType.Function,
                        byte.Parse(hcfuntion, System.Globalization.NumberStyles.HexNumber),
                        (byte)(normalized * 210)
                    });
                    double newLevel = modules[huc].Level + normalized;
                    if (newLevel > 1)
                        newLevel = 1;
                    modules[huc].Level = newLevel;
                }
                else
                {
                    byte dimvalue = Utility.GetDimValue(percentage);
                    SendMessage(new byte[] {
                        (byte)((int)X10CommandType.Function | dimvalue | 0x04),
                        byte.Parse(hcfuntion, System.Globalization.NumberStyles.HexNumber)
                    });
                    double newLevel = modules[huc].Level + Utility.GetPercentageValue(dimvalue);
                    if (newLevel > 1)
                        newLevel = 1;
                    modules[huc].Level = newLevel;
                }
            }
        }

        /// <summary>
        /// Turn on the specified module (housecode, unitcode).
        /// </summary>
        /// <param name="housecode">Housecode.</param>
        /// <param name="unitcode">Unitcode.</param>
        public void UnitOn(X10HouseCode housecode, X10UnitCode unitcode)
        {
            lock (commandLock)
            {
				Program.logger.Debug("Command UnitOn HC=" + housecode.ToString() + " UC=" + unitcode.ToString());
                //string hcunit = String.Format("{0:X}{1:X}", (int)housecode, (int)unitcode);
                string hcfuntion = String.Format("{0:x1}{1:x1}", (int)housecode, (int)X10Command.On);
                SendModuleAddress(housecode, unitcode);
                SendMessage(new byte[] {
                    (int)X10CommandType.Function,
                    byte.Parse(hcfuntion, System.Globalization.NumberStyles.HexNumber)
                });
                string huc = Utility.HouseUnitCodeFromEnum(housecode, unitcode);
                if (modules[huc].Level == 0.0)
                {
                    modules[huc].Level = 1.0;
                }
            }
        }

        /// <summary>
        /// Turn off the specified module (housecode, unitcode).
        /// </summary>
        /// <param name="housecode">Housecode.</param>
        /// <param name="unitcode">Unitcode.</param>
        public void UnitOff(X10HouseCode housecode, X10UnitCode unitcode)
        {
            lock (commandLock)
            {
				Program.logger.Debug("Command UnitOff HC=" + housecode.ToString() + " UC=" + unitcode.ToString());
                //string hcunit = String.Format("{0:X}{1:X}", (int)housecode, (int)unitcode);
                SendModuleAddress(housecode, unitcode);

                string hcfunction = String.Format("{0:x1}{1:x1}", (int)housecode, (int)X10Command.Off);
                SendMessage(new byte[] {
                    (int)X10CommandType.Function,
                    byte.Parse(hcfunction, System.Globalization.NumberStyles.HexNumber)
                });
                string huc = Utility.HouseUnitCodeFromEnum(housecode, unitcode);
                modules[huc].Level = 0.0;
            }
        }

        /// <summary>
        /// Turn on all the light modules with the given housecode.
        /// </summary>
        /// <param name="houseCode">Housecode.</param>
        public void AllLightsOn(X10HouseCode houseCode)
        {
            lock (commandLock)
            {
				Program.logger.Debug("Command AllOn HC=" + houseCode.ToString());
                string hcunit = String.Format("{0:X}{1:X}", (int)houseCode, 0);
                string hcfuntion = String.Format("{0:x1}{1:x1}", (int)houseCode, (int)X10Command.All_Lights_On);
                SendMessage(new byte[] {
                    (int)X10CommandType.Address,
                    byte.Parse(hcunit, System.Globalization.NumberStyles.HexNumber)
                });
                SendMessage(new byte[] {
                    (int)X10CommandType.Function,
                    byte.Parse(hcfuntion, System.Globalization.NumberStyles.HexNumber)
                });
                // TODO: pick only lights module
                CommandEvent_AllLightsOn(houseCode);
            }
        }

        /// <summary>
        /// Turn off all the light modules with the given housecode.
        /// </summary>
        /// <param name="houseCode">Housecode.</param>
        public void AllUnitsOff(X10HouseCode houseCode)
        {
            lock (commandLock)
            {
				Program.logger.Debug("Command AllOff HC=" + houseCode.ToString());
                string hcunit = String.Format("{0:X}{1:X}", (int)houseCode, 0);
                string hcfuntion = String.Format("{0:x1}{1:x1}", (int)houseCode, (int)X10Command.All_Units_Off);
                SendMessage(new byte[] {
                    (int)X10CommandType.Address,
                    byte.Parse(hcunit, System.Globalization.NumberStyles.HexNumber)
                });
                SendMessage(new byte[] {
                    (int)X10CommandType.Function,
                    byte.Parse(hcfuntion, System.Globalization.NumberStyles.HexNumber)
                });
                // TODO: pick only lights module
                CommandEvent_AllUnitsOff(houseCode);
            }
        }

        /// <summary>
        /// Request module status.
        /// </summary>
        /// <param name="housecode">Housecode.</param>
        /// <param name="unitcode">Unitcode.</param>
        public void StatusRequest(X10HouseCode housecode, X10UnitCode unitcode)
        {
            lock (commandLock)
            {
				Program.logger.Debug("Command StatusReq HC=" + housecode.ToString() + " UC=" + unitcode.ToString());
                //string hcunit = String.Format("{0:X}{1:X}", (int)housecode, (int)unitcode);
                if (!SendModuleAddress(housecode, unitcode))
                {
					Program.logger.Debug("abort statusrequest");
                    return;
                }

                string hcfuntion = String.Format("{0:x1}{1:x1}", (int)housecode, (int)X10Command.Status_Request);
                if (!SendMessage(new byte[] {
                    (int)X10CommandType.Function,
                    byte.Parse(hcfuntion, System.Globalization.NumberStyles.HexNumber)
                }))
                {
					Program.logger.Debug("abort statusrequest");
                    return;
                }

            }
        }

        #endregion

        #region X10 Interface Commands

        private bool SendModuleAddress(X10HouseCode housecode, X10UnitCode unitcode)
        {
            // TODO: do more tests about this optimization (and comment out the "if" if tests are succesfully)
            //if (!addressedModules.Contains(mod) || addressedModules.Count > 1) // optimization disabled, uncomment to enable
            {
                UnselectModules();
                SelectModule(Utility.HouseUnitCodeFromEnum(housecode, unitcode));
                string hcUnit = String.Format("{0:X}{1:X}", (int)housecode, (int)unitcode);
                bool ret = SendMessage(new byte[] {
                    //(byte)X10CommandType.ExtAddress,
                    (byte)X10CommandType.Address,
                    byte.Parse(hcUnit, System.Globalization.NumberStyles.HexNumber)
                });
                newAddressData = true;
                return ret;
            }
        }

        private void UpdateInterfaceTime(bool batteryClear)
        {
            /*
            The PC must then respond with the following transmission

            Bit range	Description
            55 to 48	timer download header (0x9b)
            47 to 40	Current time (seconds)
            39 to 32	Current time (minutes ranging from 0 to 119)
            31 to 23	Current time (hours/2, ranging from 0 to 11)
            23 to 16	Current year day (bits 0 to 7)
            15	Current year day (bit 8)
            14 to 8		Day mask (SMTWTFS)
            7 to 4		Monitored house code
            3		Reserved
            2		Battery timer clear flag
            1		Monitored status clear flag
            0		Timer purge flag
            */
            var date = DateTime.Now;
            int minute = date.Minute;
            int hour = date.Hour / 2;
            if (Math.IEEERemainder(date.Hour, 2) > 0)
            {
                // Add remaining minutes 
                minute += 60;
            }
            int wday = Convert.ToInt16(Math.Pow(2, (int)date.DayOfWeek));
            int yearDay = date.DayOfYear - 1;
            if (yearDay > 255)
            {
                yearDay = yearDay - 256;
                // Set current yearDay flag in wday's 7:th bit, since yearDay overflowed...
                wday = wday + Convert.ToInt16(Math.Pow(2, 7));
            }
            // Build message
            byte[] message = new byte[7];
            message[0] = 0x9b;   // cm11 x10 time download header
            message[1] = Convert.ToByte(date.Second);
            message[2] = Convert.ToByte(minute);
            message[3] = Convert.ToByte(hour);
            message[4] = Convert.ToByte(yearDay);
            message[5] = Convert.ToByte(wday);
            message[6] = Convert.ToByte((batteryClear ? 0x07 : 0x03) + Utility.HouseCodeFromString(this.HouseCode)); // Send timer purgeflag + Monitored status clear flag, monitored house code.

            if (itf=="CM15")
            {
                // this seems to be needed only with CM15
                message[7] = 0x02;
            }

            UnselectModules();
			Program.logger.Debug("Update interface time");
			SendMessage(message);
        }

        private void InitializeCm15()
        {
            lock (commandLock)
            {
                // BuildTransceivedCodesMessage return byte message for setting transceive codes from given comma separated _monitoredhousecode
                UpdateInterfaceTime(false);
                byte[] trcommand = CM15.BuildTransceivedCodesMessage(monitoredHouseCode);
                SendMessage(trcommand);
                SendMessage(new byte[] { (byte)X10CommandType.PC_StatusRequest });
            }
        }

        #endregion

        #region X10 Command Input Events and Modules status update

        private void CommandEvent_On()
        {
            for (int m = 0; m < addressedModules.Count; m++)
            {
                X10Module mod = addressedModules[m];
                mod.Level = 1.0;
            }
        }

        private void CommandEvent_Off()
        {
            for (int m = 0; m < addressedModules.Count; m++)
            {
                X10Module mod = addressedModules[m];
                mod.Level = 0.0;
            }
        }

        private void CommandEvent_Bright(byte parameter)
        {
            for (int m = 0; m < addressedModules.Count; m++)
            {
                X10Module mod = addressedModules[m];
                var brightLevel = Math.Round(mod.Level + (((double)parameter) / 210D), 2);
                if (brightLevel > 1)
                    brightLevel = 1;
                mod.Level = brightLevel;
            }
        }

        private void CommandEvent_Dim(byte parameter)
        {
            for (int m = 0; m < addressedModules.Count; m++)
            {
                X10Module mod = addressedModules[m];
                var dimLevel = Math.Round(mod.Level - (((double)parameter) / 210D), 2);
                if (dimLevel < 0)
                    dimLevel = 0;
                mod.Level = dimLevel;
            }
        }

        private void CommandEvent_AllUnitsOff(X10HouseCode houseCode)
        {
            UnselectModules();
            // TODO: select only light modules 
            foreach (KeyValuePair<string, X10Module> modkv in modules)
            {
                if (modkv.Value.Code.StartsWith(houseCode.ToString()))
                {
                    modkv.Value.Level = 0.0;
                }
            }
        }

        private void CommandEvent_AllLightsOn(X10HouseCode houseCode)
        {
            UnselectModules();
            // TODO: pick only light modules 
            foreach (KeyValuePair<string, X10Module> modkv in modules)
            {
                if (modkv.Value.Code.StartsWith(houseCode.ToString()))
                {
                    modkv.Value.Level = 1.0;
                }
            }
        }

        #endregion

        #region Modules life cycle and events

        private X10Module SelectModule(string address)
        {
            if (!modules.Keys.Contains(address))
            {
                var newModule = new X10Module(this, address);
                newModule.PropertyChanged += Module_PropertyChanged;
                modules.Add(address, newModule);
            }
            var module = modules[address];
            if (!addressedModules.Contains(module))
            {
                addressedModules.Add(module);
            }
            return module;
        }

        private void UnselectModules()
        {
            addressedModules.Clear();
        }

        private void Module_PropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            // Route event to listeners
            if (ModuleChanged != null)
            {
                try
                {
                    ModuleChanged(sender, args);
                }
                catch (Exception e)
                {
					Program.logger.Error(e);
                }
            }
        }

        #endregion

        public void DecodeRX(byte[] readData)
        {
            if (readData.Length > 3)
            {
                int messageLength = readData[0];
                if (readData.Length > messageLength)
                {
                    char[] bitmapData = Convert.ToString(readData[1], 2).PadLeft(8, '0').ToCharArray();
                    byte[] functionBitmap = new byte[messageLength - 1];
                    for (int i = 0; i < functionBitmap.Length; i++)
                    {
                        functionBitmap[i] = byte.Parse(bitmapData[7 - i].ToString());
                    }

                    byte[] messageData = new byte[messageLength - 1];
                    Array.Copy(readData, 2, messageData, 0, messageLength - 1);

                    // CM15 Extended receive has got inverted data
                    if (messageLength > 2 && itf=="CM15")
                    {
                        Array.Reverse(functionBitmap, 0, functionBitmap.Length);
                        Array.Reverse(messageData, 0, messageData.Length);
                    }

					Program.logger.Debug("FNMAP: {0}", BitConverter.ToString(functionBitmap));
					Program.logger.Debug("DATA : {0}", BitConverter.ToString(messageData));

                    for (int b = 0; b < messageData.Length; b++)
                    {
                        // read current byte data (type: 0x00 address, 0x01 function)
                        if (functionBitmap[b] == (byte)X10FunctionType.Address) // address
                        {
                            X10HouseCode houseCode = (X10HouseCode)Convert.ToInt16(messageData[b].ToString("X2").Substring(0, 1), 16);
                            X10UnitCode unitCode = (X10UnitCode)Convert.ToInt16(messageData[b].ToString("X2").Substring(1, 1), 16);
                            string address = Utility.HouseUnitCodeFromEnum(houseCode, unitCode);

							Program.logger.Info("      {0}) Address = {1}", b, address);

                            if (newAddressData)
                            {
                                newAddressData = false;
                                UnselectModules();
                            }
                            SelectModule(address);

                            OnPlcAddressReceived(new PlcAddressReceivedEventArgs(houseCode, unitCode));
                        }
                        else if (functionBitmap[b] == (byte)X10FunctionType.Function) // function
                        {
                            var command = (X10Command)Convert.ToInt16(messageData[b].ToString("X2").Substring(1, 1), 16);
                            var houseCode = X10HouseCode.NotSet;
                            Enum.TryParse<X10HouseCode>(Convert.ToInt16(messageData[b].ToString("X2").Substring(0, 1), 16).ToString(), out houseCode);

							Program.logger.Info("      {0}) House code = {1}", b, houseCode);
							Program.logger.Info("      {0})    Command = {1}", b, command);

                            switch (command)
                            {
                                case X10Command.All_Lights_Off:
                                    //if (houseCode != X10HouseCode.NotSet)
                                    //    CommandEvent_AllUnitsOff(houseCode);
                                    break;
                                case X10Command.All_Lights_On:
                                    // TODO : tester quand un volet se ferme et qu'on l'ouvre en manu
                                    //if (houseCode != X10HouseCode.NotSet)
                                    //    CommandEvent_AllLightsOn(houseCode);
                                    break;
                                case X10Command.On:
                                    CommandEvent_On();
                                    break;
                                case X10Command.Off:
                                    CommandEvent_Off();
                                    break;
                                case X10Command.Bright:
                                    CommandEvent_Bright(messageData[++b]);
                                    break;
                                case X10Command.Dim:
                                    CommandEvent_Dim(messageData[++b]);
                                    break;
                            }
                            newAddressData = true;

                            OnPlcFunctionReceived(new PlcFunctionReceivedEventArgs(command, houseCode));
                        }
                    }
                }
            }
        }

        void DecodeRF(byte[] readData)
        {
            bool isSecurityCode = (readData.Length == 8 && readData[1] == (byte)X10Defs.RfSecurityPrefix && ((readData[3] ^ readData[2]) == 0x0F) && ((readData[5] ^ readData[4]) == 0xFF));
            bool isCodeValid = isSecurityCode || (readData.Length == 6 && readData[1] == (byte)X10Defs.RfCommandPrefix && ((readData[3] & ~readData[2]) == readData[3] && (readData[5] & ~readData[4]) == readData[5]));

            // Still unknown meaning of the last byte in security codes
            if (isSecurityCode && readData[7] == 0x80)
                readData[7] = 0x00;

            // Repeated messages check
            if (isCodeValid)
            {
                if (lastRfMessage == BitConverter.ToString(readData) && (lastReceivedTs - lastRfReceivedTs).TotalMilliseconds < minRfRepeatDelayMs)
                {
					Program.logger.Warn("Ignoring repeated message within {0}ms", minRfRepeatDelayMs);
                    return;
                }
                lastRfMessage = BitConverter.ToString(readData);
                lastRfReceivedTs = DateTime.Now;
            }

			Program.logger.Debug("RFCOM: {0}", BitConverter.ToString(readData));
            OnRfDataReceived(new RfDataReceivedEventArgs(readData));

            // Decode received 32 bit message
            // house code + 4th bit of unit code
            // unit code (3 bits) + function code
            if (isSecurityCode)
            {
                var securityEvent = X10RfSecurityEvent.NotSet;
                Enum.TryParse<X10RfSecurityEvent>(readData[4].ToString(), out securityEvent);
                uint securityAddress = BitConverter.ToUInt32(new byte[] { readData[2], readData[6], readData[7], 0x00 }, 0);
                if (securityEvent != X10RfSecurityEvent.NotSet)
                {
					Program.logger.Info("Security Event {0} Address {1}", securityEvent, securityAddress);
                    OnRfSecurityReceived(new RfSecurityReceivedEventArgs(securityEvent, securityAddress));
                }
                else
                {
					Program.logger.Warn("Could not parse security event");
                }
            }
            else if (isCodeValid)
            {
                // Parse function code
                var hf = X10RfFunction.NotSet;
                Enum.TryParse<X10RfFunction>(readData[4].ToString(), out hf);
                // House code (4bit) + unit code (4bit)
                byte hu = readData[2];
                // Parse house code
                var houseCode = X10HouseCode.NotSet;
                Enum.TryParse<X10HouseCode>((Utility.ReverseByte((byte)(hu >> 4)) >> 4).ToString(), out houseCode);
                switch (hf)
                {
                    case X10RfFunction.Dim:
                    case X10RfFunction.Bright:
						Program.logger.Info("Command {0}", hf);
                        if (hf == X10RfFunction.Dim)
                            CommandEvent_Dim((byte)X10Defs.DimBrightStep);
                        else
                            CommandEvent_Bright((byte)X10Defs.DimBrightStep);
                        OnRfCommandReceived(new RfCommandReceivedEventArgs(hf, X10HouseCode.NotSet, X10UnitCode.Unit_NotSet));
                        break;
                    case X10RfFunction.AllLightsOn:
                    case X10RfFunction.AllLightsOff:
                        if (houseCode != X10HouseCode.NotSet)
                        {
							Program.logger.Info("Command {0} HouseCode {1}", hf, houseCode);
                            if (hf == X10RfFunction.AllLightsOn)
                                CommandEvent_AllLightsOn(houseCode);
                            else
                                CommandEvent_AllUnitsOff(houseCode);
                            OnRfCommandReceived(new RfCommandReceivedEventArgs(hf, houseCode, X10UnitCode.Unit_NotSet));
                        }
                        else
                        {
							Program.logger.Warn("Unable to decode house code value");
                        }
                        break;
                    case X10RfFunction.NotSet:
						Program.logger.Warn("Unable to decode function value");
                        break;
                    default:
                        // Parse unit code
                        string houseUnit = Convert.ToString(hu, 2).PadLeft(8, '0');
                        string unitFunction = Convert.ToString(readData[4], 2).PadLeft(8, '0');
                        string uc = (Convert.ToInt16(houseUnit.Substring(5, 1) + unitFunction.Substring(1, 1) + unitFunction.Substring(4, 1) + unitFunction.Substring(3, 1), 2) + 1).ToString();
                        // Parse module function
                        var fn = X10RfFunction.NotSet;
                        Enum.TryParse<X10RfFunction>(unitFunction[2].ToString(), out fn);
                        switch (fn)
                        {
                            case X10RfFunction.On:
                            case X10RfFunction.Off:
                                var unitCode = X10UnitCode.Unit_NotSet;
                                Enum.TryParse<X10UnitCode>("Unit_" + uc.ToString(), out unitCode);
                                if (unitCode != X10UnitCode.Unit_NotSet)
                                {
									Program.logger.Info("Command {0} HouseCode {1} UnitCode {2}", fn, houseCode, unitCode.Value());
                                    UnselectModules();
                                    SelectModule(houseCode.ToString() + unitCode.Value().ToString());
                                    if (fn == X10RfFunction.On)
                                        CommandEvent_On();
                                    else
                                        CommandEvent_Off();
                                    OnRfCommandReceived(new RfCommandReceivedEventArgs(fn, houseCode, unitCode));
                                }
                                else
                                {
									Program.logger.Warn("Could not parse unit code");
                                }
                                break;
                        }
                        break;
                }
            }
            else
            {
				Program.logger.Warn("Bad Rf message received");
            }
        }

        /// <summary>
        /// fonction bloquante d'envoi d'un message
        /// </summary>
        /// <param name="message"></param>
        public bool SendMessage(byte[] message, bool checksum = true)
        {
            try
            {
                // Wait for message delivery acknowledge
                lock (waitAckMonitor)
                {
					// have a 500ms pause between each output message
					Program.logger.Debug("Wait 100ms "+ (DateTime.Now - lastReceivedTs).TotalMilliseconds + " or ready");
                    while ((DateTime.Now - lastReceivedTs).TotalMilliseconds < 100 || 
                        (checksum && communicationState != X10CommState.Ready))
                    {
                        Thread.Sleep(500);
                    }
					Program.logger.Debug("SendMessage "+ BitConverter.ToString(message) + ", state=" + communicationState);
                    if (checksum)//message.Length > 1 && IsConnected)
                    {
                        // calculate checksum
                        if (itf=="CM11")
                        {
                            expectedChecksum = 0;
                            foreach (byte bmsg in message)
                                expectedChecksum += bmsg;
                            expectedChecksum = (byte)(expectedChecksum & 0xff);
							Program.logger.Debug("expectedChecksum=" + expectedChecksum.ToString("X0"));
                            communicationState = X10CommState.WaitingChecksum;
                            OnStatusChanged(new StatusChangedEventArgs((int)communicationState));
                        }
                        else
                        {
							Program.logger.Debug("send is waiting ack");
                            communicationState = X10CommState.WaitingAck;
                            OnStatusChanged(new StatusChangedEventArgs((int)communicationState));
                        }
                        commandSendAttempts = 0;
                        SendMessage2(message);

                        //attends la réponse ou réitère si dépassé
                        while (commandSendAttempts < commandResendMax && communicationState != X10CommState.Ready)
                        {
                            var elapsedFromWait = DateTime.Now - waitResponseTimestamp;
                            while (elapsedFromWait.TotalSeconds < commandTimeoutSeconds && communicationState != X10CommState.Ready)
                            {
                                Thread.Sleep(1);
                                elapsedFromWait = DateTime.Now - waitResponseTimestamp;
                            }
                            if (elapsedFromWait.TotalSeconds >= commandTimeoutSeconds && communicationState != X10CommState.Ready)
                            {
								// Resend last message
								Program.logger.Warn("Previous command timed out, resending ({0})", commandSendAttempts);
                                SendMessage2(commandLastMessage);
                            }
                        }
						Program.logger.Debug("exit wait");
                        communicationState = X10CommState.Ready;
                        OnStatusChanged(new StatusChangedEventArgs((int)communicationState));
                        commandSendAttempts = 0;
                        commandLastMessage = new byte[0];
                    }
                    else
                    {
                        if (!x10interface.WriteData(message))
                        {
							Program.logger.Warn("Interface I/O error");
                        } 
                    }
                }
            }
            catch (Exception ex)
            {
				Program.logger.Error(ex, "error in sendmessage");
                //gotReadWriteError = true;
            }
            return (communicationState == X10CommState.Ready);
        }

        private void SendMessage2(byte[] message)
        {
            //logger.Debug("send:" + BitConverter.ToString(message) + " state:" + communicationState.ToString());
            if (!x10interface.WriteData(message))
            {
				Program.logger.Warn("Interface I/O error");
            }
            commandSendAttempts++;
            commandLastMessage = message;
            waitResponseTimestamp = DateTime.Now;
        }

		#region Events Raising
		/// <summary>
		/// Raises the plc address received event.
		/// </summary>
		/// <param name="args">Arguments.</param>
		protected virtual void OnPlcAddressReceived(PlcAddressReceivedEventArgs args)
        {
            if (PlcAddressReceived != null)
                PlcAddressReceived(this, args);
        }

        /// <summary>
        /// Raises the plc function received event.
        /// </summary>
        /// <param name="args">Arguments.</param>
        protected virtual void OnPlcFunctionReceived(PlcFunctionReceivedEventArgs args)
        {
            if (PlcFunctionReceived != null)
                PlcFunctionReceived(this, args);
        }

        /// <summary>
        /// Raises the rf data received event.
        /// </summary>
        /// <param name="args">Arguments.</param>
        protected virtual void OnRfDataReceived(RfDataReceivedEventArgs args)
        {
            if (RfDataReceived != null)
                RfDataReceived(this, args);
        }

        /// <summary>
        /// Raises the RF command received event.
        /// </summary>
        /// <param name="args">Arguments.</param>
        protected virtual void OnRfCommandReceived(RfCommandReceivedEventArgs args)
        {
            if (RfCommandReceived != null)
                RfCommandReceived(this, args);
        }

        /// <summary>
        /// Raises the RF security received event.
        /// </summary>
        /// <param name="args">Arguments.</param>
        protected virtual void OnRfSecurityReceived(RfSecurityReceivedEventArgs args)
        {
            if (RfSecurityReceived != null)
                RfSecurityReceived(this, args);
        }

        #endregion
    }
}
