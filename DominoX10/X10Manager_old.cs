/*
    This file is part of XTenLib source code.

    XTenLib is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    XTenLib is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with XTenLib.  If not, see <http://www.gnu.org/licenses/>.  
*/

/*
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
using DmnX10.Drivers;
using DominoShared.Data;
using DominoShared;
using Microsoft.Extensions.Configuration;

namespace DmnX10
{
    /// <summary>
    /// X10 Home Automation library for .NET / Mono. It supports CM11 (serial) and CM15 (USB) hardware.
    /// </summary>
    public class X10Manager : BaseManager
    {
        protected static Logger logger = LogManager.GetCurrentClassLogger();
        // X10 objects and configuration
        private X10Interface x10interface;
        private string portName = "USB";

        // State variables
        private bool isInterfaceReady = false;

        // Read/Write error state variable
        private bool gotReadWriteError = true;

        // This is used on Linux/Mono for detecting when the link gets disconnected
        private int zeroChecksumCount = 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="DmnX10.X10Manager"/> class.
        /// </summary>
        public X10Manager(string _connectionString) 
            : base(_connectionString)
        {
            // Default interface is CM15: use "PortName" property to set a different interface driver
            x10interface = new CM15();
        }

        /// <summary>
        /// Releases unmanaged resources and performs other cleanup operations before the
        /// <see cref="DmnX10.X10Manager"/> is reclaimed by garbage collection.
        /// </summary>
        ~X10Manager()
        {
            Close();
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

        public bool Open()
        {
            lock (accessLock)
            {
                Close();
                logger.Info("Connecting to {0}", PortName);
                bool success = (x10interface != null && x10interface.Open());
                if (success)
                {
                    logger.Debug("send status request");
                    //SendMessage(new byte[] { (byte)X10CommandType.PC_StatusRequest },false);
                    byte[] reqData = new byte[] { (byte)X10CommandType.PC_StatusRequest };
                    MailboxTable mbx = OnSubmitData(new SubmitDataEventArgs(Global.strX10, "W", BitConverter.ToString(reqData)));
                    //communicationState = X10CommState.WaitingStatus;
                    //x10interface.WriteData(new byte[] { (byte)X10CommandType.StatusRequest });
                    OnStatusChanged(new StatusChangedEventArgs((int)X10CommState.WaitingStatus));
                    logger.Info("X10 connected, waiting status");

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
                    reader.Start(Readercts.Token);
                    Writercts = new CancellationTokenSource();
                    writer = new Thread(WriterTask);
                    writer = new Thread(new ParameterizedThreadStart(WriterTask));
                    writer.Start(Writercts.Token);
                }
                else
                {
                    logger.Info("unable to connect");
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
                logger.Debug("closing connection");
                try
                {
                    x10interface.Close();
                }
                catch (Exception e)
                {
                    logger.Error(e);
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

        private void ReaderTask(object obj)
        {
            logger.Debug("Start readertask");
            CancellationToken canceltoken = (CancellationToken)obj;
            while (!canceltoken.IsCancellationRequested)
            {
                while (x10interface != null && !gotReadWriteError && !canceltoken.IsCancellationRequested)
                {
                    try
                    {
                        byte[] readData = x10interface.ReadData();
                        if (readData!=null && readData.Length > 0)
                        {
                            MailboxTable mbx = OnSubmitData(new SubmitDataEventArgs(Global.strX10, "R", BitConverter.ToString(readData)));
                            //dbContext.AddToMailbox(DateTime.Now, Global.strX10, "R", BitConverter.ToString(readData));
                            // last command ACK timeout
                            /*var elapsedFromWaitAck = DateTime.Now - waitResponseTimestamp;
                            if (elapsedFromWaitAck.TotalSeconds >= commandTimeoutSeconds && communicationState != X10CommState.Ready)
                            {
                                logger.Warn("Command acknowledge timeout");
                                communicationState = X10CommState.Ready;
                            }*/
                            
                        }
                    }
                    catch (Exception e)
                    {
                        if (!e.GetType().Equals(typeof(TimeoutException)) && !e.GetType().Equals(typeof(OverflowException)))
                        {
                            gotReadWriteError = true;
                            logger.Error(e, "exception in readertask");
                        }
                    }
                }
                if (!canceltoken.IsCancellationRequested)
                {
                    while (Open() == false)
                        Thread.Sleep(500);
                }
            }
            logger.Debug("exit ReaderTask");
        }

        private void WriterTask(object obj)
        {
            logger.Debug("Start writertask");
            CancellationToken canceltoken = (CancellationToken)obj;
            while (!canceltoken.IsCancellationRequested)
            {
                while (x10interface != null && !gotReadWriteError && !canceltoken.IsCancellationRequested)
                {
                    try
                    {
                        List<MailboxTable> lmbx = OnRequestData(new RequestDataEventArgs(Global.strX10, "W"));
                        //List<MailboxTable> lmbx = dbContext.PeekMailbox(Global.strX10,"W");
                        foreach (MailboxTable mbx in lmbx)
                        {
                            if (mbx.message.Length < 2) continue;
                            byte[] writeData = mbx.message.Split('-').Select<string, byte>( s => Convert.ToByte(s, 16)).ToArray();
                            if (!x10interface.WriteData(writeData))
                                logger.Warn("Interface I/O error");
                            else
                            {
                                OnProcessedData(new ProcessedDataEventArgs(mbx, "sent"));
                                //dbContext.ArchiveMailboxMsg(mbx, null);
                            }
                        }
                        Thread.Sleep(100);
                    }
                    catch (Exception e)
                    {
                        if (!e.GetType().Equals(typeof(TimeoutException)) && !e.GetType().Equals(typeof(OverflowException)))
                        {
                            gotReadWriteError = true;
                            logger.Error(e, "exception in writertask");
                        }
                    }
                }
            }
            logger.Debug("exit WriterTask");
        }

    }

}

