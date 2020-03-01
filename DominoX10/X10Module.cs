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

#pragma warning disable 1591

using System;
using System.ComponentModel;

namespace DominoX10
{
    public class X10Module : INotifyPropertyChanged
    {
        
        #region Private fields

        private X10Main x10;
        private X10HouseCode houseCode;
        private X10UnitCode unitCode;
        private double statusLevel;

        #endregion

        #region Public events

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region Instance management

        public X10Module(X10Main x10c, string code)
        {
            x10 = x10c;
            Code = code;
            houseCode = Utility.HouseCodeFromString(code);
            unitCode = Utility.UnitCodeFromString(code);
            Level = 0.0;
            Description = "";
        }

        #endregion

        #region Public members

        /// <summary>
        /// Gets or sets the description.
        /// </summary>
        /// <value>The description.</value>
        public string Description { get; set; }

        /// <summary>
        /// Gets the House+Unit code of this module.
        /// </summary>
        /// <value>The code.</value>
        public string Code { get; }

        /// <summary>
        /// Gets the house code.
        /// </summary>
        /// <value>The house code.</value>
        public X10HouseCode HouseCode
        {
            get { return houseCode; }
        }

        /// <summary>
        /// Gets the unit code.
        /// </summary>
        /// <value>The unit code.</value>
        public X10UnitCode UnitCode
        {
            get { return unitCode; }
        }

        /// <summary>
        /// Turn On this module.
        /// </summary>
        public void On()
        {
            if (x10 != null)
            {
                x10.UnitOn(houseCode, unitCode);
            }
        }

        /// <summary>
        /// Turn Off this module.
        /// </summary>
        public void Off()
        {
            if (x10 != null)
            {
                x10.UnitOff(houseCode, unitCode);
            }
        }

        /// <summary>
        /// Dim the module by the specified percentage.
        /// </summary>
        /// <param name="percentage">Percentage.</param>
        public void Dim(int percentage = 5)
        {
            if (x10 != null)
            {
                x10.Dim(houseCode, unitCode, percentage);
            }
        }

        /// <summary>
        /// Open the shutter module by the specified percentage.
        /// </summary>
        /// <param name="percentage">Percentage.</param>
        public void ShOpen(int percentage = 5)
        {
            if (x10 != null)
            {
                x10.ShOpen(houseCode, unitCode, percentage);
            }
        }

        /// <summary>
        /// Brighten the module by the specified percentage.
        /// </summary>
        /// <param name="percentage">Percentage.</param>
        public void Bright(int percentage = 5)
        {
            if (x10 != null)
            {
                x10.Bright(houseCode, unitCode, percentage);
            }
        }

        /// <summary>
        /// Request the status of the module.
        /// </summary>
        public void GetStatus()
        {
            if (x10 != null)
            {
                x10.StatusRequest(houseCode, unitCode);
            }
        }

        /// <summary>
        /// Gets a value indicating whether this module is on.
        /// </summary>
        /// <value><c>true</c> if this module is on; otherwise, <c>false</c>.</value>
        public bool IsOn
        {
            get { return statusLevel != 0; }
        }

        /// <summary>
        /// Gets a value indicating whether this module is off.
        /// </summary>
        /// <value><c>true</c> if this module is off; otherwise, <c>false</c>.</value>
        public bool IsOff
        {
            get { return statusLevel == 0; }
        }

        /// <summary>
        /// Gets the dimmer level. This value ranges from 0.0 (0%) to 1.0 (100%).
        /// </summary>
        public double Level
        {
            get
            {
                return statusLevel; 
            }
            internal set
            {
                // This is used for the ComponentModel event implementation
                // Sets the level (0.0 to 1.0) and fire the PropertyChanged event.
                statusLevel = value;
                OnPropertyChanged("Level");
            }
        }

        #endregion

        #region Private members

        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }

        #endregion

    }
}

#pragma warning restore 1591

