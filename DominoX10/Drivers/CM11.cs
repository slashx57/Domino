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
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using Gbd.IO.Serial;
using Gbd.IO.Serial.Interfaces;
using Gbd.IO.Serial.Enums;
using NLog;

namespace DominoX10.Drivers
{
    /// <summary>
    /// CM11 driver.
    /// </summary>
    public class CM11 : X10Interface
    {
        private ISerialPort serialPort;
        //private SerialPort serialPort;
        private string portName = "";
        static Logger logger = LogManager.GetLogger("DominoX10");

        /// <summary>
        /// Initializes a new instance of the <see cref="DominoX10.Drivers.CM11"/> class.
        /// </summary>
        /// <param name="port">Serial port path.</param>
        public CM11(string port)
        {
            portName = port;
        }

        /// <summary>
        /// Open the hardware interface.
        /// </summary>
        public bool Open()
        {
            bool success = false;
            //
            try
            {
                //if (Environment.OSVersion.Platform.ToString().StartsWith("Win") == false)
                if (!System.IO.File.Exists(portName))
                {
                    logger.Error("port does not exist :"+portName);
                    return success;
                }
                var controller = Platform.GetController();
                if (controller == null)
                {
                    logger.Error("controller not available");
                    return success;
                }

                serialPort = controller.GetPort(portName);
                /*serialPort = new SerialPort();
                serialPort.PortName = portName;*/
                serialPort.SerialSettings.BaudRate_Int = 4800;
                serialPort.SerialSettings.Parity = Parity.None;
                serialPort.SerialSettings.DataBits = DataBits.D8;
                serialPort.SerialSettings.StopBits = StopBits.One;
                serialPort.SerialSettings.Handshake = Handshake.None;
                serialPort.BufferSettings.ReadTimeout = 150;
                serialPort.BufferSettings.WriteTimeout = 150;
                serialPort.PinStates.Rts_Enable = false;
                serialPort.PinStates.Dtr_Enable = false;
                
                // DataReceived event won't work under Linux / Mono
                //serialPort.DataReceived += HandleDataReceived;
                //serialPort.ErrorReceived += HanldeErrorReceived;

                if (serialPort.IsOpen == false)
                    serialPort.Open();
                // Send status request on connection
                //this.WriteData(new byte[] { (byte)X10CommandType.PLC_StatusRequest });
                serialPort.Uart.DiscardInBuffer();
                serialPort.Uart.DiscardOutBuffer();

                success = true;
            }
            catch (Exception e)
            {
                logger.Error(e);
            }

            return success;
        }

        /// <summary>
        /// Close the hardware interface.
        /// </summary>
        public void Close()
        {
            if (serialPort != null)
            {
                //serialPort.DataReceived -= HandleDataReceived
                //serialPort.ErrorReceived -= HanldeErrorReceived;
                try
                {
                    //serialPort.Dispose();
                    serialPort.Close();
                }
                catch (Exception e)
                {
                    logger.Error(e);
                }
                serialPort = null;
            }
        }

        /// <summary>
        /// Reads the data.
        /// </summary>
        /// <returns>The data.</returns>
        public byte[] ReadData()
        {
            int buflen = 32;
            int length = 0;
            int readBytes = 0;
            byte[] buffer = new byte[buflen];
            byte[] onecar = new byte[1];
            do
            {
                while (true)
                {
                    try
                    {
                        readBytes = serialPort.Uart.Read(onecar, 0, 1);
                        //logger.Debug("readbytes(ontime)=" + readBytes);
                        if (readBytes > 0) break;
                    }
                    catch (TimeoutException)
                    {
                        //logger.Debug("readbytes(timeout)="+readBytes+" first="+onecar[0]);
                        readBytes = 0;
                        break;
                    }
                    catch(Exception ex)
                    {
                        logger.Error(ex, "exception in ReadData");
                        break;
                    }
                }

                buffer[length] = onecar[0];
                length += readBytes;
                if (length > 1 && buffer[0] < length)
                {
                    //logger.Debug("break on length({0}) > msg_size({1})",length,buffer[0]);
                    break;
                }
            } while (readBytes > 0 && (buflen - length > 0));

            byte[] readData = new byte[length];
            /*if (length > 1 && length < 13)
            {
                readData[0] = (int)X10CommandType.PLC_Poll;
                Array.Copy(buffer, 0, readData, 1, length);
            }
            else*/
            {
                Array.Copy(buffer, readData, length);
            }
            //logger.Debug("total=" + length);

            //if (length>0)
            //    logger.Trace("received:" + BitConverter.ToString(readData));
            return readData;
        }

        /// <summary>
        /// Writes the data.
        /// </summary>
        /// <returns>true</returns>
        /// <c>false</c>
        /// <param name="bytesToSend">Bytes to send.</param>
        public bool WriteData(byte[] bytesToSend)
        {
            if (serialPort != null && serialPort.IsOpen)
            {
                serialPort.Uart.Write(bytesToSend, 0, bytesToSend.Length);
                logger.Trace("sent:" + BitConverter.ToString(bytesToSend));
                return true;
            }
            return false;
        }

    }
}

