using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Mezeo
{
    public static class BasicInfo
    {
        private static string userName="";
        private static string password="";
        private static string serviceUrl="";
        private static DateTime lastSync;
        private static DateTime lastUpdateCheckAt;
        private static bool autoSync=true;
        private static bool loggingEnabled = false;
        private static string syncDirPath="";
        private static string lastExecutedVersion="";
        private static bool isInitailSync = true;
        private static string nqParentURI = "";
        private static string strMacAddress = null;

        //Registry Value Names 
        private static string USERNAME = "Username";
        private static string PASSWORD = "Password";
        private static string SERVICEURL = "ServiceUrl";
        private static string LASTSYNC = "LastSync";
        private static string AUTOSYNC = "AutoSync";
        private static string LOGGING = "LoggingEnabled";
        private static string SYNCDIRPATH = "SyncDirPath";
        private static string LASTVERSION = "LastVersion";
        private static string INITIALSYNC = "InitialSync";
        private static string LASTUPDATEDCHECK = "LastUpdateCheckAt";
        private static string MACADDRESS = "InitialMacAddress";

        //Flag for updates 
        public static bool updateAvailable = false;

        //Flag to check sync app is pause or not 
        public static bool isSyncPause = false;
        private static RegistryHandler regHandler = new RegistryHandler();

        public static string UserName
        {
            get { return userName; }
            set 
            {
                userName = value;
                regHandler.Write(USERNAME, userName, Microsoft.Win32.RegistryValueKind.Binary, true);
            }
        }

        public static string Password
        {
            get { return password; }
            set 
            {
                password = value;
                regHandler.Write(PASSWORD, password, Microsoft.Win32.RegistryValueKind.Binary, true);
            }
        }

        public static string ServiceUrl
        {
            get { return serviceUrl; }
            set 
            { 
                serviceUrl = value;
                regHandler.Write(SERVICEURL, serviceUrl, Microsoft.Win32.RegistryValueKind.Binary, false);
            }
        }

        public static DateTime LastSyncAt
        {
            get { return lastSync; }
            set 
            { 
                lastSync = value;
                regHandler.Write(LASTSYNC, lastSync, Microsoft.Win32.RegistryValueKind.Binary, false);
            }
        }

        public static bool AutoSync
        {
            get { return autoSync; }
            set 
            { 
                autoSync = value;
                regHandler.Write(AUTOSYNC, autoSync, Microsoft.Win32.RegistryValueKind.Binary, false);
            }
        }

        public static bool LoggingEnabled
        {
            get { return loggingEnabled; }
            set 
            {
                loggingEnabled = value;
                regHandler.Write(LOGGING, loggingEnabled, Microsoft.Win32.RegistryValueKind.Binary, false);
            }
        }

        public static string SyncDirPath
        {
            get { return syncDirPath; }
            set 
            { 
                syncDirPath = value;
                regHandler.Write(SYNCDIRPATH, syncDirPath, Microsoft.Win32.RegistryValueKind.String, false);
            }
        }

        public static string LastExecutedVersion
        {
            get { return lastExecutedVersion; }
            set 
            { 
                lastExecutedVersion = value;
                regHandler.Write(LASTVERSION, lastExecutedVersion, Microsoft.Win32.RegistryValueKind.String, false);
            }
        }

        public static bool LoadRegistryValues()
        {             
            if (regHandler.isKeyExists())
            {
                ReadRegValue();
                return true;
            }
            else
            {
                WriteRegValue();
                return false;
            }
        }

        public static bool IsInitialSync
        {
            get
            {
                return isInitailSync;
            }
            set
            {
                isInitailSync = value;
                regHandler.Write(INITIALSYNC, isInitailSync, Microsoft.Win32.RegistryValueKind.Binary, false);
            }
        }


        public static DateTime LastUpdateCheckAt
        {
            get { return lastUpdateCheckAt; }
            set
            {
                lastUpdateCheckAt = value;
                regHandler.Write(LASTUPDATEDCHECK, lastUpdateCheckAt, Microsoft.Win32.RegistryValueKind.Binary, false);
            }
        }

        public static string MacAddress
        {
            get
            {
                if ((null == strMacAddress) || (strMacAddress.Length == 0))
                {
                    // Look in the registry first.
                    try
                    {
                        strMacAddress = regHandler.Read(MACADDRESS, Microsoft.Win32.RegistryValueKind.String, false);
                        if (null == strMacAddress)
                            strMacAddress = GetNinMacAddress();
                    }
                    catch (Exception ex)
                    {
                        LogWrapper.LogMessage("BasicInfo - ReadRegValue", "Caught exception: " + ex.Message);
                    }

                    // Only calculate the address once.  This prevents issues if there are multiple
                    // network cards or if someone switches/replaces a network card.
                    if ((null == strMacAddress) || (strMacAddress.Length == 0))
                        strMacAddress = GetNinMacAddress();
                    if ((null != strMacAddress) && (strMacAddress.Length != 0))
                        regHandler.Write(MACADDRESS, strMacAddress, Microsoft.Win32.RegistryValueKind.String, false);
                }
                if ((null == strMacAddress) || (strMacAddress.Length == 0))
                    strMacAddress = "";

                return strMacAddress;
            }
        }

        public static bool IsCredentialsAvailable
        {
            get
            {
                return userName.Trim().Length != 0 && password.Trim().Length != 0 && serviceUrl.Trim().Length != 0;
            }
        }

        public static string NQParentURI
        {
            get
            {
                return nqParentURI;
            }
            set
            {
                nqParentURI = value;
            }
        }

        public static bool UpdateAvailable
        {
            get
            {
                return updateAvailable;
            }
            set
            {
                updateAvailable = value;
            }
        }

        private static void ReadRegValue()
        {
            userName = regHandler.Read(USERNAME, Microsoft.Win32.RegistryValueKind.Binary, true);
            password = regHandler.Read(PASSWORD, Microsoft.Win32.RegistryValueKind.Binary, true);
            serviceUrl = regHandler.Read(SERVICEURL, Microsoft.Win32.RegistryValueKind.Binary, false);
            syncDirPath = regHandler.Read(SYNCDIRPATH, Microsoft.Win32.RegistryValueKind.String, false);
            autoSync = Convert.ToBoolean(regHandler.Read(AUTOSYNC, Microsoft.Win32.RegistryValueKind.Binary, false));
            isInitailSync = Convert.ToBoolean(regHandler.Read(INITIALSYNC, Microsoft.Win32.RegistryValueKind.Binary, false));
            try
            {
                string regValue = regHandler.Read(LASTSYNC, Microsoft.Win32.RegistryValueKind.Binary, false);
                if (null != regValue)
                    lastSync = DateTime.Parse(regValue);
            }
            catch (Exception ex)
            {
                LogWrapper.LogMessage("BasicInfo - ReadRegValue", "Caught exception: " + ex.Message);
            }
            try
            {
                string regValue = regHandler.Read(LOGGING, Microsoft.Win32.RegistryValueKind.Binary, false);
                if (null != regValue)
                    loggingEnabled = Convert.ToBoolean(regValue);
            }
            catch (Exception ex)
            {
                LogWrapper.LogMessage("BasicInfo - ReadRegValue", "Caught exception: " + ex.Message);
            }
            try
            {
                string regValue = regHandler.Read(LASTUPDATEDCHECK, Microsoft.Win32.RegistryValueKind.Binary, false);
                if (null != regValue)
                    lastUpdateCheckAt = DateTime.Parse(regValue);
            }
            catch (Exception ex)
            {
                LogWrapper.LogMessage("BasicInfo - ReadRegValue", "Caught exception: " + ex.Message);
            }
            try
            {
                lastExecutedVersion = regHandler.Read(LASTVERSION, Microsoft.Win32.RegistryValueKind.String, false);
                if (null == lastExecutedVersion)
                    lastExecutedVersion = "1.0.0";
            }
            catch (Exception ex)
            {
                LogWrapper.LogMessage("BasicInfo - ReadRegValue", "Caught exception: " + ex.Message);
            }
            try
            {
                strMacAddress = regHandler.Read(MACADDRESS, Microsoft.Win32.RegistryValueKind.String, false);
                // Don't set the value here.  The getter/setter will save it to the registry the first time
                // it is initialized.
                //if (null == strMacAddress)
                //    strMacAddress = GetNinMacAddress();
            }
            catch (Exception ex)
            {
                LogWrapper.LogMessage("BasicInfo - ReadRegValue", "Caught exception: " + ex.Message);
            }
        }

        private static void WriteRegValue()
        {
            regHandler.Write(USERNAME, userName, Microsoft.Win32.RegistryValueKind.Binary, true);
            regHandler.Write(PASSWORD, password, Microsoft.Win32.RegistryValueKind.Binary, true);
            regHandler.Write(SERVICEURL, serviceUrl, Microsoft.Win32.RegistryValueKind.Binary, false);
            regHandler.Write(SYNCDIRPATH, syncDirPath, Microsoft.Win32.RegistryValueKind.String, false);
            regHandler.Write(LASTSYNC, lastSync, Microsoft.Win32.RegistryValueKind.Binary, false);
            regHandler.Write(AUTOSYNC, autoSync, Microsoft.Win32.RegistryValueKind.Binary, false);
            regHandler.Write(INITIALSYNC, isInitailSync, Microsoft.Win32.RegistryValueKind.Binary, false);
            regHandler.Write(LOGGING, loggingEnabled, Microsoft.Win32.RegistryValueKind.Binary, false);
            regHandler.Write(LASTUPDATEDCHECK, lastUpdateCheckAt, Microsoft.Win32.RegistryValueKind.Binary, false);
            regHandler.Write(LASTVERSION, lastExecutedVersion, Microsoft.Win32.RegistryValueKind.String, false);
            //regHandler.Write(MACADDRESS, strMacAddress, Microsoft.Win32.RegistryValueKind.String, false);
        }

        private static string GetNinMacAddress()
        {
            string macAddresses = "";

            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up)
                {
                    macAddresses += nic.GetPhysicalAddress().ToString();
                    break;
                }
            }
            return macAddresses;
        }

        public static string GetQueueName()
        {
            return MacAddress + "-" + UserName;
        }

        public static string GetUpdateURL()
        {
#if MEZEO32
            string osVersion = "32";
#else
            string osVersion = "64";
#endif
            return string.Format("{0}/update/sync/win/{1}/versioninfo.xml", ServiceUrl, osVersion);
        }

    }

    public static class LogWrapper
    {
        public static void LogMessage(string tag, string message)
        {
            if (BasicInfo.LoggingEnabled)
                Debugger.Instance.logMessage(tag, message);
        }
    }
}
