#region Copyright
// This is an unpublished work protected under the copyright laws of the
// United States and other countries.  All rights reserved.  Should
// publication occur the following will apply:  � 2008 GameTech
// International, Inc.
#endregion

//US4119: Set PIN number >  Sale with player card and PIN has not been set.

using System;
using System.IO;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using System.ComponentModel;
using System.Globalization;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics;
using System.Linq;
using System.Text;
using CrystalDecisions.CrystalReports.Engine;
using GTI.Modules.Shared;
using GTI.Controls;
using GTI.Modules.PlayerCenter.UI;
using GTI.Modules.PlayerCenter.Data;
using GTI.Modules.PlayerCenter.Properties;
using GTI.Modules.Shared.Business;
using GTI.Modules.PlayerCenter.Data.Printing;
using System.Collections.Concurrent;


namespace GTI.Modules.PlayerCenter.Business
{

    /// <summary>
    /// Represents the Player Center application.
    /// </summary>
    public sealed class PlayerManager
    {
        #region Constants and Data Types
        private const int ServerCommShutdownWait = 15000;
        private const string LogPrefix = "PlayerCenter - ";
        // FIX: DE2475 - Appears to be problems with the registration of GTIVidcap.ocx on the system.
        private const string GameTechDir = "%GMTCDRIVE%";      
        private const string VidSnapshotName = @"Common\VidSnapshot.exe";
        private const string TempPicFileName = "TempPlayerPic.jpg";
        // END: DE2475
        #endregion

        #region Member Variables

        private PlayerCenterModule m_module = null;        // System Related
        private BackgroundWorker m_worker = null;
        private Exception m_asyncException = null;
        private PlayerLoyaltyTier[] m_playerTiers = null;
        private PlayerListItem[] m_lastFindPlayersResults = null;
        private Player m_lastPlayerFromServer = null;        // TTP 50067
        private Bitmap m_lastPlayerPic = null;
        private SplashScreen m_loadingForm = null;        // UIs
        private MCPPlayerManagementForm m_mainMenuForm = null;
        private WaitForm m_waitForm = null;
        private ReportForm m_reportForm; // PDTS 312
        private object m_errorSync = new object();
        private object m_findPlayerSync = new object();
        private object m_lastPlayerSync = new object();
        private object m_playerPicSync = new object();
        private object m_logSync = new object();
        private bool m_loggingEnabled = false;
        private bool m_externalMagCardReader;        // PDTS 1064 - Portable POS Card Swipe.
        private int m_deviceId = 0;
        private int m_machineId = 0;
        private bool m_needPlayerCardPIN;
        private MagneticCardReader m_magCardReader;

        #endregion

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the PlayerManager class.
        /// </summary>
        /// <param name="module">The module which is running this 
        /// object.</param>
        public PlayerManager(PlayerCenterModule module)
        {            
            m_module = module;
        }


        public PlayerManager()
        {           
        }  

        #endregion
    
        #region Member Methods
        private static volatile PlayerManager m_instance;
        private static readonly object m_sync = new Object();
        public static PlayerManager Instance
        {
            get
            {
                if (m_instance == null)
                {
                    lock (m_sync)
                    {
                        if (m_instance == null)
                            m_instance = new PlayerManager();
                    }
                }

                return m_instance;
            }
        }
        #region Initialization Methods

        // FIX: DE2476
        /// <summary>
        /// Initializes all the POS's data.
        /// </summary>
        /// <param name="showLoadingForm">true to show a loading form while 
        /// initializing; otherwise false.</param>
        /// <param name="isTouchScreen">Whether this is being used by a 
        /// touchscreen based module.</param>
        public void Initialize(bool showLoadingForm, bool isTouchScreen)
        {
            string strErr = "PlayerManager start init.";
            
            try
            {
                // Check to see if we are already initialized.
                if (IsInitialized)
                    return;

                IsTouchScreen = isTouchScreen;
                // END: DE2476

                strErr = "create modcom.";
                ModuleComm modComm = null;

                // Get the system related ids.
                try
                {
                    modComm = new ModuleComm();
                    strErr = "modcom..set deviceid.";
                    m_deviceId = modComm.GetDeviceId();
                    strErr = "modcom..set machineid.";
                    m_machineId = modComm.GetMachineId();
                    strErr = "modcom..set operatorid.";
                    OperatorID = modComm.GetOperatorId();
                    GetOperatorID.operatorID = OperatorID;
                }
                catch (Exception e)
                {
                    MessageForm.Show(string.Format(Resources.GetDeviceInfoFailed, e.Message + "...last step: " + strErr), Resources.PlayerCenterName);
                    return;
                }

                strErr = "create setting obj.";
                Settings = PlayerCenterSettings.Instance;  // Create a settings object with the default values. //US4119 changed to singleton
                strErr = "Check to see what resolution to run in.";

                if (m_deviceId == Device.POSPortable.Id)      // Check to see what resolution to run in.
                    Settings.DisplayMode = new CompactDisplayMode();
                else
                    Settings.DisplayMode = new NormalDisplayMode();

                strErr = "Create and show the loading form.";
                m_loadingForm = new SplashScreen();
                strErr = "set form...version, cursor, app name.";
                m_loadingForm.Version = GetVersionAndCopyright(true);
                m_loadingForm.Cursor = Cursors.WaitCursor;
                m_loadingForm.ApplicationName = Properties.Resources.productName;
                strErr = "show form.";

                if (showLoadingForm) m_loadingForm.Show();
              
                strErr = "set form loading status.";
                m_loadingForm.Status = Resources.LoadingWorkstationInfo;                // Get the workstation's settings from the server.
                Application.DoEvents();

                try
                {
                    strErr = "get workstation settings.";
                    GetStaffModulePermission(modComm.GetStaffId(), (int)EliteModule.PlayerCenter, (int)ModuleFeature.ManualPointsAwardtoPlayer);//US2100/TA15674
                    GetWorkstationSettings();
                    m_magCardReader = new MagneticCardReader(Settings.MSRSettingsInfo);
              
                }
                catch (Exception e)
                {
                    if (IsTouchScreen)
                        MessageForm.Show(Settings.DisplayMode, string.Format(Resources.GetSettingsFailed, e.Message + "...last step: " + strErr));
                    else
                        MessageForm.Show(string.Format(Resources.GetSettingsFailed, e.Message + "...last step: " + strErr));
                    return;
                }

                strErr = "Check to see if we want to log everything.";

                try  // Check to see if we want to log everything.
                {
                    strErr = "EnableLogging.";
                    if (Settings.EnableLogging)
                    {
                        strErr = "EnableLogging..fire log entry.";
                        Logger.EnableFileLog(Settings.LoggingLevel, Settings.FileLogRecycleDays);
                        Logger.StartLogger(Logger.StandardPrefix);
                        m_loggingEnabled = true;
                        Log(string.Format("Initializing Player Center ({0})...", GetVersionAndCopyright(false)), LoggerLevel.Information);
                    }
                }
                catch (Exception e)
                {
                    if (IsTouchScreen)
                        MessageForm.Show(Settings.DisplayMode, string.Format(Resources.LogFailed, e.Message + "...last step: " + strErr));
                    else
                        MessageForm.Show(string.Format(Resources.LogFailed, e.Message + "...last step: " + strErr));

                    return;
                }

                strErr = "ForceEnglish.";
                // Check to see if we only want to display in English.
                if (Settings.ForceEnglish)
                {
                    Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-US");
                    Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture;
                    Log("Forcing English.", LoggerLevel.Configuration);
                }

                //if(!m_settings.ShowCursor)
                //    Cursor.Hide();

                //US2649 
                RunGetPlayerLocation();
                GetListLocationCity();
                GetListLocationState();
                GetListLocationZipCode();
                GetListLocationCountry();
                GetPackageName();

                //US2100
                //GetStaffModulePermission(modComm.GetStaffId(), (int)EliteModule.PlayerCenter, (int)ModuleFeature.ManualPointsAwardtoPlayer);

                strErr = "set form loading status..again.";
                //loading player status code
                m_loadingForm.Status = Resources.LoadingStatusCode;
                GetStatusCode();

                strErr = "set form loading status..loading tiers.";
                // Load the Player Loyalty Tiers
                m_loadingForm.Status = Resources.LoadingPlayerTiers;
                Application.DoEvents();

                try
                {
                    strErr = "get tiers.";
                    GetPlayerTiers();
                }
                catch (Exception e)
                {
                    if (IsTouchScreen)
                        MessageForm.Show(Settings.DisplayMode, string.Format(Resources.GetPlayerTiersFailed, e.Message + "...last step: " + strErr));
                    else
                        MessageForm.Show(string.Format(Resources.GetPlayerTiersFailed, e.Message + "...last step: " + strErr));

                    Log("Get player tiers failed: " + e.Message, LoggerLevel.Severe);
                    return;
                }

                strErr = "set form loading status..start player center.";
                m_loadingForm.Status = Resources.StartingPlayerCenter;
                Application.DoEvents();

                strErr = "create instance of player center form.";
                // Create main menu.
                m_mainMenuForm = new MCPPlayerManagementForm(this);

                // FIX: DE2476 - Performance slows down as player pictures are added.
                // Load our wait form.
                if(IsTouchScreen)
                    m_waitForm = new WaitForm(Settings.DisplayMode);
                else
                    m_waitForm = new WaitForm();

                m_waitForm.WaitImage = Resources.Waiting;
                m_waitForm.CancelButtonVisible = false;
                m_waitForm.ProgressBarVisible = false;
                m_waitForm.Cursor = Cursors.WaitCursor;
                // END: DE2476

                // PDTS 1064
                strErr = "init the mag. card reader";
                // Initialize the mag. card reader.
                try
                {
                    //if (m_deviceId == Device.POSPortable.Id && m_settings.MagCardMode == MagneticCardReaderMode.KeyboardAndCPCLTCP)
                    //{
                    //    // PDTS 1064
                    //    // Replace PlayerCenter's MagCardReader with ours (only
                    //    // if we are using the TCP mag card reader).
                    //    m_playerCenter.SetExternalMagCardReader(MagCardReader);
                    //}
                    //else
                    //    m_playerCenter.BeginMagCardReading(); // Rally DE1852

                    //Log("Player Center initialized.", LoggerLevel.Debug);
                    MagCardReader = new MagneticCardReader(Settings.MSRSettingsInfo);
                    m_externalMagCardReader = false;
                }
                catch(Exception e)
                {
                    if(IsTouchScreen)
                        MessageForm.Show(Settings.DisplayMode, string.Format(CultureInfo.CurrentCulture, Resources.MagLoadError, e.Message + "...last step: " + strErr));
                    else
                        MessageForm.Show(string.Format(CultureInfo.CurrentCulture, Resources.MagLoadError, e.Message + "...last step: " + strErr));

                    Log("Failed to initialize the mag. card reader:" + e.Message, LoggerLevel.Severe);
                    return;
                }

                strErr = "player center form...cursor";
                m_loadingForm.Cursor = Cursors.Default;

                strErr = "set intialize = true.";
                //Application.DoEvents();
                IsInitialized = true;

                strErr = "fire log...Player Center initialized!";
                Log("Player Center initialized!", LoggerLevel.Debug);
            }
            catch (Exception ex)
            {
                if (IsTouchScreen)
                    MessageForm.Show(Settings.DisplayMode, string.Format("PlayerManager.Initialize()...{0}", ex.Message + "...last step: " + strErr));
                else
                    MessageForm.Show(string.Format("PlayerManager.Initialize()...{0}", ex.Message + "...last step: " + strErr));

                Log("Get player tiers failed: " + ex.Message, LoggerLevel.Severe);
                return;

            }
        }

        /// <summary>
        ///  fill up the status code dictionary
        /// </summary>
        public static void GetStatusCode()
        {
            OperatorPlayerStatusList = GetOperatorPlayerStatusList.GetOperatorPlayerStatus(OperatorID);
        }

        //US2649

        public static void GetPackageName()
        {
            var message = new GetPackageItemMessage(OperatorID);
            message.Send();
            if (message.ReturnCode == (int)GTIServerReturnCode.Success)
            {
                PackageListName = message.PackageItems;
            }
        }

        //US2100
         private void GetStaffModulePermission(int staffId, int moduleId, int moduleFeatureId)
        {
            StaffHasPermissionToAwardPoints = false;
            var message = new GetStaffModuleFeaturesMessage(staffId, moduleId, moduleFeatureId);
            message.Send();

            if (message.ReturnCode == (int)GTIServerReturnCode.Success)
            {         
                 StaffHasPermissionToAwardPoints =  (message.ModuleFeatureList.ToList().Count != 0)?true:false;
            }
        }

        public static void RunGetPlayerLocation()
        {
               GetPlayerLocation.GetPlayerLocationX();  
        }

        public static void GetListLocationCity()
        {
            ListLocationCity = GetPlayerLocationPer.CityName; 
        }

        public static void GetListLocationState()
        {
            ListLocationState = GetPlayerLocationPer.StateName;  
        }

        public static void GetListLocationZipCode()
        {
            ListLocationZipCode = GetPlayerLocationPer.ZipCodeName;   
        }

        public static void GetListLocationCountry()
        {
            ListLocationCountry = GetPlayerLocationPer.CountryName;
        }




        //US2649
        /// <summary>
        /// Returns a string with the version and copyright information of 
        /// the Player Center.
        /// </summary>
        /// <param name="justVersion">true if just the version is to be 
        /// returned; otherwise false.</param>
        /// <returns>A string with the version and optionally the copyright 
        /// information.</returns>
        private string GetVersionAndCopyright(bool justVersion)
        {
            // Get version.
            string version = 
                Assembly.GetExecutingAssembly().GetName().Version.Major.ToString() +
                "." + Assembly.GetExecutingAssembly().GetName().Version.Minor.ToString() +
                "." + Assembly.GetExecutingAssembly().GetName().Version.Build.ToString() +
                "." + Assembly.GetExecutingAssembly().GetName().Version.Revision.ToString();

            // Get copyright.
            if(!justVersion)
            {
                string copyright = string.Empty;

                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false);

                if(attributes.Length > 0)
                    copyright = ((AssemblyCopyrightAttribute)attributes[0]).Copyright;

                return Resources.Version + version + " - " + copyright;
            }
            else
                return version;
        }

        #endregion

        #region UI Methods

        /// <summary>
        /// Shows the main menu form modally.
        /// </summary>
        public void Start()
        {
            if (m_loadingForm != null) m_loadingForm.CloseForm();

            if(IsInitialized && m_mainMenuForm != null)
            {
                Log("Starting Player Center.", LoggerLevel.Information);

                if(!m_externalMagCardReader)
                {
                    MagCardReader.SynchronizingObject = m_mainMenuForm; // Rally DE1852
                    MagCardReader.BeginReading(); // PDTS 1064
                }

                Application.Run(m_mainMenuForm);
            }
        }

        /// <summary>
        /// Tells the Player Center to close the main menu form.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">An EventArgs object that contains the 
        /// event data.</param>
        public void ClosePlayerCenter(object sender, EventArgs e)
        {
            if(m_mainMenuForm != null)
                m_mainMenuForm.Close();
        }

        /// <summary>
        /// Tells the Player Center to bring its form to the front.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">An EventArgs object that contains the 
        /// event data.</param>
        internal void BringToFront(object sender, EventArgs e)
        {
            if (IsInitialized && m_mainMenuForm != null)
            {
                if (m_mainMenuForm.InvokeRequired)
                {
                    MethodInvoker del = ActivateMainForm;
                    m_mainMenuForm.Invoke(del);
                }
                else
                    ActivateMainForm();
            }
        }
        
        /// <summary>
        /// Shows the player picture capture dialog and then signals a wait handle.
        /// This method should only be called on STA threads.
        /// </summary>
        internal void ShowPictureCapture()
        {
            // Clear out the previous pic.
            LastPlayerPic = null;

            // FIX: DE2475
            // Generate a temp file.
            string fileName = System.Environment.ExpandEnvironmentVariables(GameTechDir) + @"\Temp\";

            if (!Directory.Exists(fileName))
                Directory.CreateDirectory(fileName);

            fileName += TempPicFileName;

            // Setup the processes arguments.
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.FileName = Path.Combine(System.Environment.ExpandEnvironmentVariables(GameTechDir), VidSnapshotName);
            startInfo.Arguments = fileName;
            // END: DE2475

            lock (Settings.SyncRoot)
            {
                if (!Settings.ShowCursor)
                    startInfo.Arguments += " /hidecursor";
            }

            startInfo.CreateNoWindow = true;

            // Start the picture process.
            try
            {
                System.Diagnostics.Process picProcess = System.Diagnostics.Process.Start(startInfo);

                // Wait until it has closed.
                // Rally DE2427 - User is unable to take more than 1 picture without restarting EliteMCP.
                while (!picProcess.HasExited)
                {
                    Thread.Sleep(50);
                }

                // Did it take the picture?
                if (picProcess.ExitCode == 1)
                {
                    try
                    {
                        Bitmap tempPic = new Bitmap(fileName);
                        LastPlayerPic = new Bitmap(tempPic);

                        tempPic.Dispose();
                        tempPic = null;

                        System.IO.File.Delete(fileName);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                Log(Resources.errorFailedToTakePicture + ex.ToString(), LoggerLevel.Severe);
                if (IsTouchScreen)
                    MessageForm.Show(Settings.DisplayMode, Properties.Resources.errorFailedToTakePicture + ex.Message);
                else
                    MessageForm.Show(Properties.Resources.errorFailedToTakePicture + ex.Message, Properties.Resources.PlayerCenterName);
            }
            finally
            {
                // Signal the other waiting thread(s).
                EventWaitHandle waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, "Player Picture Wait Handle");
                waitHandle.Set();
            }
        }

        /// <summary>
        /// Displays the Player Management form.  The CurrentPlayer property 
        /// will be used to load the form with the starting values 
        /// (if applicable).
        /// </summary>
        /// <param name="playersSaved">true if the user saved any players 
        /// while the form was open; otherwise false.</param>
        /// <param name="playerToSet">An instance of a player object to set as 
        /// the current player or null.</param>
        public void ShowPlayerManagment(out bool playersSaved, out Player playerToSet)
        {
            if (m_loadingForm != null)//knc
                m_loadingForm.CloseForm();

            PlayerManagementForm mgmtForm = new PlayerManagementForm(this, Settings.DisplayMode);

            mgmtForm.EnableSetAsCurrentPlayer = true;
            mgmtForm.ShowDialog();

            // TTP 50120
            if (!(LastAsyncException is ServerCommException))
            {
                playersSaved = mgmtForm.PlayersSaved;
                playerToSet = mgmtForm.PlayerToSet;
            }
            else
            {
                playersSaved = false;
                playerToSet = null;
            }
        }
        
        /// <summary>
        /// Activates the main window for this application
        /// </summary>
        private void ActivateMainForm()
        {
            m_mainMenuForm.WindowState = FormWindowState.Normal;
            m_mainMenuForm.Activate();
        }

        // FIX: DE2476
        /// <summary>
        /// Shows the wait form modally.
        /// </summary>
        /// <param name="owner">Any object that implements IWin32Window that 
        /// represents the top-level window that will own the modal dialog 
        /// box.</param>
        internal void ShowWaitForm(IWin32Window owner)
        {
            if(m_waitForm != null)
                m_waitForm.ShowDialog(owner);
        }
        // END: DE2476

        /// <summary>
        /// Disposes of the LoadingForm.
        /// </summary>
        internal void DisposeLoadingForm()
        {
            if(m_loadingForm != null)
            {
                m_loadingForm.Hide();
                m_loadingForm.Dispose();
                m_loadingForm = null;
            }
        }

        #endregion

        /// <summary>
        /// Writes a message to the Player Center's log.
        /// </summary>
        /// <param name="message">The message to write to the log.</param>
        /// <param name="type">The level of the message.</param>
        /// <returns>true if success; otherwise false.</returns>
        internal bool Log(string message, LoggerLevel level)
        {
            lock (m_logSync)
            {
                if (m_loggingEnabled)
                {
                    StackFrame frame = new StackFrame(1, true);
                    string fileName = frame.GetFileName();
                    int lineNumber = frame.GetFileLineNumber();
                    message = PlayerManager.LogPrefix + message;

                    try
                    {
                        switch (level)
                        {
                            case LoggerLevel.Severe:
                                Logger.LogSevere(message, fileName, lineNumber);
                                break;

                            case LoggerLevel.Warning:
                                Logger.LogWarning(message, fileName, lineNumber);
                                break;

                            default:
                            case LoggerLevel.Information:
                                Logger.LogInfo(message, fileName, lineNumber);
                                break;

                            case LoggerLevel.Configuration:
                                Logger.LogConfig(message, fileName, lineNumber);
                                break;

                            case LoggerLevel.Debug:
                                Logger.LogDebug(message, fileName, lineNumber);
                                break;

                            case LoggerLevel.Message:
                                Logger.LogMessage(message, fileName, lineNumber);
                                break;

                            case LoggerLevel.SQL:
                                Logger.LogSql(message, fileName, lineNumber);
                                break;
                        }

                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
                else
                    return false;
            }
        }

        /// <summary>
        /// Based on the exception passed in, this method will translate
        /// the error message to localized text and rethrow the exception as
        /// a PlayerCenterException.  If the exception type is not recognized, 
        /// then the exception is rethrown as is.
        /// </summary>
        /// <param name="ex">The exception to reformat.</param>
        internal void ReformatException(Exception ex)
        {
            if(ex is MessageWrongSizeException)
                throw new PlayerCenterException(string.Format(Resources.MessagePayloadWrongSize, ex.Message), ex);
            else if(ex is ServerCommException)
                throw new PlayerCenterException(Resources.ServerCommFailed, ex);
            else if(ex is ServerException && ex.InnerException != null)
                throw new PlayerCenterException(string.Format(Resources.InvalidMessageResponse, ex.Message), ex.InnerException);
            else if(ex is ServerException)
            {
                int errorCode = (int)((ServerException)ex).ReturnCode;
                throw new PlayerCenterException(string.Format(Resources.ServerErrorCode, errorCode), ex);
            }
            else
                throw ex;
        }

        /// <summary>
        /// Based on the exception passed in, this method will translate
        /// the error message to localized text and return the value.
        /// </summary>
        /// <param name="ex">The exception to format.</param>
        /// <returns>The exception's localized message.</returns>
        internal string FormatExceptionMessage(Exception ex)
        {
            if(ex is MessageWrongSizeException)
                return string.Format(Resources.MessagePayloadWrongSize, ex.Message);
            else if(ex is ServerCommException)
                return Resources.ServerCommFailed;
            else if(ex is ServerException && ex.InnerException != null)
                return string.Format(Resources.InvalidMessageResponse, ex.Message);
            else if(ex is ServerException)
            {
                int errorCode = (int)((ServerException)ex).ReturnCode;
                return string.Format(Resources.ServerErrorCode, errorCode);
            }
            else
                return ex.Message;
        }

        /// <summary>
        /// Prepares the system for shutdown because server 
        /// communications failed.
        /// </summary>
        internal void ServerCommFailed()
        {
            // Display a message saying that the Player Center is closing.
            if(IsTouchScreen)
                MessageForm.Show(Settings.DisplayMode, Resources.ServerCommFailed + "\n\n" + Resources.ShuttingDown, MessageFormTypes.Pause, ServerCommShutdownWait);
            else
                MessageForm.Show(Resources.ServerCommFailed + "\n\n" + Resources.ShuttingDown, Resources.PlayerCenterName, MessageFormTypes.Pause, ServerCommShutdownWait);

            Log("Server communications failed.  Shutting down.", LoggerLevel.Severe);
            ClosePlayerCenter(this, new EventArgs());
        }

        /// <summary>
        /// Gets the settings from the server.
        /// </summary>
        private void GetWorkstationSettings()
        {
            // Send message for global settings.
            // Rally DE130
            GetSettingsMessage settingsMsg = new GetSettingsMessage(m_machineId, OperatorID, SettingsCategory.GlobalSystemSettings);

            try
            {
                settingsMsg.Send();
            }
            catch (Exception e)
            {
                ReformatException(e);
            }

            // Set the workstation id.
            //m_workstationId = settingsMsg.WorkstationId;

            // Loop through each setting and parse the value.
            SettingValue[] stationSettings = settingsMsg.Settings;

            foreach (SettingValue setting in stationSettings)
            {
                Settings.LoadSetting(setting);
            }

            // Rally TA7897
            // Send a message for license settings.
            GetLicenseFileSettingsMessage licSettingsMsg = new GetLicenseFileSettingsMessage(true);

            try
            {
                licSettingsMsg.Send();
            }
            catch (Exception e)
            {
                ReformatException(e);
            }

            // Loop through each setting and parse the value.
            foreach (LicenseSettingValue setting in licSettingsMsg.LicenseSettings)
            {
                Settings.LoadSetting(setting);
            }
            // END: TA7897

            //Get all setting on third party
            if (StaffHasPermissionToAwardPoints)//If staff has permission to grant points then check if the player pin is required.
            {
                GetSettingsMessage thirdPartySettingsMsg = new GetSettingsMessage(m_machineId, OperatorID, SettingsCategory.ThirdPartyPlayerTrackingSettings);

                try
                {
                    thirdPartySettingsMsg.Send();//Just get every setting
                }
                catch (Exception e)
                {
                    ReformatException(e);
                }

                // Set the workstation id.
                //m_workstationId = settingsMsg.WorkstationId;

                // Loop through each setting and parse the value.
                SettingValue[] thirdPartyStationSettings = thirdPartySettingsMsg.Settings;

                foreach (SettingValue setting in thirdPartyStationSettings)
                {
                    Settings.LoadSetting(setting);

                    if (Setting.ThirdPartyPlayerInterfaceNeedPINForRating == (Setting)setting.Id && Convert.ToBoolean(setting.Value))//And player does not have a pin
                    {
                        m_needPlayerCardPIN = true;
                    }
                }
            }
        }

        /// <summary>
        /// Retrieves the Player Loyalty Tiers from the server.
        /// </summary>
        private void GetPlayerTiers()
        {
            GetPlayerTierListMessage tierMsg = new GetPlayerTierListMessage(OperatorID);

            try
            {
                tierMsg.Send();
            }
            catch(Exception e)
            {
                ReformatException(e);
            }

            m_playerTiers = tierMsg.Tiers;
        }

        // PDTS 1064
        /// <summary>
        /// Sets a mag. card reader owned by another module.
        /// </summary>
        /// <param name="reader">The reader to use.</param>
        public void SetExternalMagCardReader(MagneticCardReader reader)
        {
            // Dispose of ours.
            if(MagCardReader != null)
            {
                MagCardReader.EndReading();
                MagCardReader.RemoveAllSources();
                MagCardReader = null;
            }

            MagCardReader = reader;
            m_externalMagCardReader = true;
        }

        // Rally DE1852
        /// <summary>
        /// Tells the Player Center to start it's mag. card reader.
        /// </summary>
        public void BeginMagCardReading()
        {
            if(!m_externalMagCardReader && MagCardReader != null)
                MagCardReader.BeginReading();
        }

        /// <summary>
        /// Tells the Player Center to stop it's mag. card reader.
        /// </summary>
        public void EndMagCardReading()
        {
            if(!m_externalMagCardReader && MagCardReader != null)
                MagCardReader.EndReading();
        }

        #region Get Player

        // FIX: DE2476
        // TTP 50067
        /// <summary>
        /// Creates a thread to get a player's data on the server and sets
        /// the WaitForm's settings.
        /// </summary>
        /// <param name="magCardNumber">The mag. card number of the player to 
        /// look for.</param>
        internal void GetPlayer(string magCardNumber)
        {
            // Set the wait message.
            m_waitForm.Message = Resources.WaitFormGettingPlayer;

            // Create the worker thread and run it.
            m_worker = new BackgroundWorker();
            m_worker.WorkerReportsProgress = true;
            m_worker.WorkerSupportsCancellation = false;
            m_worker.DoWork += new DoWorkEventHandler(GetPlayerData);
            m_worker.ProgressChanged += new ProgressChangedEventHandler(m_waitForm.ReportProgress);
            m_worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(GetPlayerComplete);
            m_worker.RunWorkerAsync(magCardNumber);
        }

        /// <summary>
        /// Creates a thread to get a player's data on the server and sets
        /// the WaitForm's settings.
        /// </summary>
        /// <param name="playerId">The id of the player to look for.</param>
        internal void GetPlayer(int playerId)
        {
            // Set the wait message.
            m_waitForm.Message = Resources.WaitFormGettingPlayer;

            // Create the worker thread and run it.
            m_worker = new BackgroundWorker();
            m_worker.WorkerReportsProgress = true;
            m_worker.WorkerSupportsCancellation = false;
            m_worker.DoWork += new DoWorkEventHandler(GetPlayerData);
            m_worker.ProgressChanged += new ProgressChangedEventHandler(m_waitForm.ReportProgress);
            m_worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(GetPlayerComplete);
            m_worker.RunWorkerAsync(playerId);
        }

        public bool IsBusy
        {
            get
            {
                return (m_worker != null && m_worker.IsBusy);
            }
            //set{ m_worker.IsBusy = value;}
        }
        private class PlayerLookupInfo
        {
            public int playerID = 0;
            public string CardNumber = string.Empty;
            public int PIN = 0;
            public bool UpdateCurrentPlayer = false;
            public bool WaitFormDisplayed = false;
        }

        internal void StartGetPlayer(int playerId)
        {
            StartGetPlayer(playerId, 0);
        }

          internal void StartGetPlayer(string magCardNumber, int PIN)
        {


            PlayerLookupInfo playerInfo = new PlayerLookupInfo();

            playerInfo.CardNumber = magCardNumber;
            playerInfo.PIN = PIN;

             m_waitForm.Message = Resources.WaitFormGettingPlayer;
            m_worker = new BackgroundWorker();
            m_worker.WorkerReportsProgress = true;
            m_worker.WorkerSupportsCancellation = false;
            m_worker.DoWork += new DoWorkEventHandler(SendGetPlayer);
            m_worker.ProgressChanged += new ProgressChangedEventHandler(m_waitForm.ReportProgress);
            m_worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(GetPlayerCompleteAwardPoints);
            m_worker.RunWorkerAsync(playerInfo);


            // TTP 50114
            //RunWorker(m_settings.EnableAnonymousMachineAccounts ? Resources.WaitFormGettingMachine : Resources.WaitFormGettingPlayer,
            //          new DoWorkEventHandler(SendGetPlayer), (object)playerInfo, new RunWorkerCompletedEventHandler(GetPlayerComplete));
        }

        internal void StartGetPlayer(int playerId, int PIN)
        {
            PlayerLookupInfo playerInfo = new PlayerLookupInfo();
            playerInfo.playerID = playerId;
            playerInfo.PIN = PIN;        
            m_waitForm.Message = Resources.WaitFormGettingPlayer;
            m_worker = new BackgroundWorker();
            m_worker.WorkerReportsProgress = true;
            m_worker.WorkerSupportsCancellation = false;
            m_worker.DoWork += new DoWorkEventHandler(SendGetPlayer);
            m_worker.ProgressChanged += new ProgressChangedEventHandler(m_waitForm.ReportProgress);
            m_worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(GetPlayerCompleteAwardPoints);
            m_worker.RunWorkerAsync(playerInfo);
        
        }

            internal void StartSetPlayerCardPIN(int playerId, int PIN)
        {
            PlayerLookupInfo playerInfo = new PlayerLookupInfo();

            playerInfo.playerID = playerId;
            playerInfo.PIN = PIN;
            m_waitForm.Message = Resources.WaitFormUpdatingPlayer;
            m_worker = new BackgroundWorker();
            m_worker.WorkerReportsProgress = true;
            m_worker.WorkerSupportsCancellation = false;
            m_worker.DoWork += new DoWorkEventHandler(SendGetSetPlayerCardPIN);
            m_worker.ProgressChanged += new ProgressChangedEventHandler(m_waitForm.ReportProgress);
            m_worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(GetSetPlayerCardPINComplete);
            m_worker.RunWorkerAsync(playerInfo);
                 
        }

            internal void StartGetPlayerCardPIN(int playerId)
        {
            PlayerLookupInfo playerInfo = new PlayerLookupInfo();

            playerInfo.playerID = playerId;
            playerInfo.PIN = -1;

           m_waitForm.Message = Resources.WaitFormUpdatingPlayer;
            m_worker = new BackgroundWorker();
            m_worker.WorkerReportsProgress = true;
            m_worker.WorkerSupportsCancellation = false;
            m_worker.DoWork += new DoWorkEventHandler(SendGetSetPlayerCardPIN);
            m_worker.ProgressChanged += new ProgressChangedEventHandler(m_waitForm.ReportProgress);
            m_worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(GetSetPlayerCardPINComplete);
            m_worker.RunWorkerAsync(playerInfo);
        }


          private void SendGetSetPlayerCardPIN(object sender, DoWorkEventArgs e)
        {
            SetupThread();

            // Unbox the argument.
            int playerId = ((PlayerLookupInfo)(e.Argument)).playerID;
            int PIN = ((PlayerLookupInfo)(e.Argument)).PIN;

            // Are we getting the PIN?
            if (PIN == -1) //yes
            {
                GetPlayerMagCardPINMessage PINMsg = new GetPlayerMagCardPINMessage(playerId);

                PINMsg.Send();

                PIN = PINMsg.PlayerMagCardPIN;
            }
            else //setting the PIN
            {
                SetPlayerMagCardPINMessage PINMsg = new SetPlayerMagCardPINMessage(playerId, PIN);

                PINMsg.Send();
            }

            e.Result = new Tuple<int , int>(playerId, PIN);
        }

          private void GetSetPlayerCardPINComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            // Set the error that occurred (if any).
            LastAsyncException = e.Error;

            if (e.Error == null)
            {
                int playerId = ((Tuple<int, int>)e.Result).Item1;
                int PIN = ((Tuple<int, int>)e.Result).Item2;

                if (PIN > 0 && CurrentPlayer != null && CurrentPlayer.Id == playerId)
                    CurrentPlayer.PlayerCardPIN = PIN;
            }

            // Close the wait form.
            m_waitForm.CloseForm();
        }


      
          internal static void ForceEnglish()
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-US");
            Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture;
        }
        private PlayerCenterSettings m_settings;// = new PlayerCenterSettings();

        internal void SetupThread()
        {
            // Set the language.
            lock (m_settings.SyncRoot)
            {
                if (m_settings.ForceEnglish)
                    ForceEnglish();
                else
                    Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture;
            }

            // Wait a couple of ticks to let the wait form display.
            System.Windows.Forms.Application.DoEvents();
        }
             private ConcurrentQueue<ServerMessage> pendingMessages = new ConcurrentQueue<ServerMessage>();
            private bool ShouldStartProcessingMessage(ServerMessage message)
        {
            bool addMessage = true; // defaults to true as undefined messages should always be added to the queue to be processed
            lock (pendingMessages) // keep the "any" check and the enqueue atomic
            {
                if (message is FindPlayerByCardMessage) // US4809
                {
                    string magCardNum = (message as FindPlayerByCardMessage).MagCardNumber;

                    addMessage = !pendingMessages.Any(x =>
                    {
                        if (!(x is FindPlayerByCardMessage))
                            return false;
                        else
                            return String.Equals((x as FindPlayerByCardMessage).MagCardNumber, magCardNum, StringComparison.CurrentCultureIgnoreCase);
                    });
                }

                if (addMessage)
                {
                    pendingMessages.Enqueue(message);
                    //IsBusy = true;
                }
            }

            return addMessage;
        }
        //private bool enableMachineAccounts=false;
        private void SendGetPlayer(object sender, DoWorkEventArgs e)
        {
            SetupThread();

            // FIX: DE2580 - A player swipe should enter the player in the
            // raffle and give them points.
            bool enableMachineAccounts, promptForCreate  =false, enterRaffle = false;
            enableMachineAccounts= false;
            //// Set the options.
            //lock (m_settings.SyncRoot)
            //{
            //    // TTP 50114
            //    enableMachineAccounts = m_settings.EnableAnonymousMachineAccounts;
            //    promptForCreate = m_settings.PromptForPlayerCreation; // PDTS 1044
            //    enterRaffle = m_settings.SwipeEntersRaffle;
            //}

            // Unbox the argument.
            PlayerLookupInfo sentPlayer = (PlayerLookupInfo)e.Argument;
            int playerId = sentPlayer.playerID;
            string magCardNum = sentPlayer.CardNumber;
            int PIN = sentPlayer.PIN;
            bool updatePlayer = sentPlayer.UpdateCurrentPlayer;
            bool justSynced = false;

            if (!m_settings.ThirdPartyPlayerInterfaceUsesPIN)
                PIN = 0;

            // Are we getting the player by id or mag. card?
            if (playerId == 0)
            {
                FindPlayerByCardMessage cardMsg = new FindPlayerByCardMessage();
                cardMsg.MagCardNumber = magCardNum;
                cardMsg.PIN = PIN;
                cardMsg.SyncPlayerWithThirdParty = m_settings.ThirdPartyPlayerSyncMode == 0;

                if (!ShouldStartProcessingMessage(cardMsg))
                {
                    Log("FindPlayerByCardMessage with same card already being processed, ignored extra call", LoggerLevel.Message);
                    return; // message is already pending, don't bother trying to send it again
                }

                // Send the message.
                try
                {
                    cardMsg.Send();
                }
                catch (ServerCommException)
                {
                    throw; // Don't repackage the ServerCommException
                }
                catch (Exception ex)
                {
                    // TTP 50114
                    throw new PlayerCenterException(string.Format(Resources.GetPlayerFailed, ServerExceptionTranslator.FormatExceptionMessage(ex)), ex);
                }

                // Set the id that we got back from the server.
                if (cardMsg.PlayerId == 0)
                {
                    // PDTS 1044
                    // Can we create the account?
                    bool noSyncWithThirdPartySoAddPlayer = Settings.ThirdPartyPlayerInterfaceID != 0 && (!cardMsg.SyncPlayerWithThirdParty || cardMsg.ThirdPartyInterfaceDown);
                    //promptForCreate && 
                    if ( !string.IsNullOrEmpty(magCardNum) && ((Settings.ThirdPartyPlayerInterfaceID == 0) || noSyncWithThirdPartySoAddPlayer))
                    {
                        bool doCreate = false;

                        if (noSyncWithThirdPartySoAddPlayer)
                        {
                            doCreate = true;
                        }
                        else
                        {
                            //if (m_waitForm != null && !m_waitForm.IsDisposed && m_waitForm.InvokeRequired) // if we're using the wait form
                            //{
                            //    CreatePlayerPromptDelegate prompt = new CreatePlayerPromptDelegate(PromptToCreatePlayer);
                            //    doCreate = ((DialogResult)m_waitForm.Invoke(prompt, new object[] { m_waitForm }) == DialogResult.Yes);
                            //}
                            //else if (m_sellingForm != null && m_sellingForm.InvokeRequired) // if there's no wait form, but still requires the UI thread
                            //{
                            //    //CreatePlayerPromptDelegate prompt = new CreatePlayerPromptDelegate(PromptToCreatePlayer);
                            //    doCreate = ((DialogResult)m_sellingForm.Invoke(prompt, new object[] { m_sellingForm }) == DialogResult.Yes);
                            //}
                            //else // Just try it? Hopefully it doesn't get here if the UI thread is required
                            //{
                            //    doCreate = (PromptToCreatePlayer(m_waitForm) == DialogResult.Yes);
                            //}
                        }

                        if (doCreate)
                            playerId = CreatePlayerForPOS(magCardNum);
                        else
                            throw new PlayerCenterUserCancelException(Resources.NoPlayersFound);
                    }
                    else
                    {
                        throw new PlayerCenterException( Resources.NoPlayersFound);
                    }
                }
                else
                {
                    playerId = cardMsg.PlayerId;

                    if (cardMsg.SyncPlayerWithThirdParty && cardMsg.PointsUpToDate)//(if invalid pin = true /false) ; if valid pin = true/true
                        justSynced = true;
                }
            }

            Player player = null;
            int opId;

            //lock (m_currentOp.SyncRoot)
            //{
            opId = OperatorID;
            //}

            if (!enableMachineAccounts)
            {
                PlayerCardSwipeMessage swipeMsg = new PlayerCardSwipeMessage(playerId, null, enterRaffle, PIN);

                try
                {
                    swipeMsg.Send();
                }
                catch (ServerCommException)
                {
                    throw; // Don't repackage the ServerCommException
                }
                catch (Exception ex)
                {
                    throw new PlayerCenterException(string.Format(CultureInfo.CurrentCulture, Resources.CardSwipeFailed, ServerExceptionTranslator.FormatExceptionMessage(ex)), ex);
                }
            }
            // END: DE2580

            try
            {
                bool syncPlayer = !justSynced && (m_settings.ThirdPartyPlayerSyncMode == 0 || updatePlayer); //realtime or need points

                player = new Player(playerId, opId, PIN, syncPlayer, justSynced);//syncPlayer, justSynced If invalid pin = true/false ; if valisd pin = false /true
            }
            catch (ServerCommException)
            {
                throw; // Don't repackage the ServerCommException
            }
            catch (ServerException exc)
            {
                // TTP 50114
                throw new PlayerCenterException(string.Format(CultureInfo.CurrentCulture, Resources.GetPlayerFailed, ServerExceptionTranslator.FormatExceptionMessage(exc)) + " " + string.Format(CultureInfo.CurrentCulture, Resources.MessageName, exc.Message), exc);
            }
            catch (Exception exc)
            {
                // TTP 50114
                throw new PlayerCenterException(string.Format(CultureInfo.CurrentCulture,   Resources.GetPlayerFailed, ServerExceptionTranslator.FormatExceptionMessage(exc)), exc);
            }

            //US4320
            //try
            //{
            //    //player.DiscountUsageDictionary = GetDiscountUsageBySessionMessage.GetDiscountUsageBySession(playerId, CurrentSessionPlayedId);
            //}
            //catch (Exception ex)
            //{
            //    throw new PlayerCenterException(string.Format(CultureInfo.CurrentCulture, Resources.GetPlayerDiscountUsageFailed, ServerExceptionTranslator.FormatExceptionMessage(ex)), ex);
            //}

            e.Result = new Tuple<Player, bool, bool>(player, updatePlayer, sentPlayer.WaitFormDisplayed);
        }

    public  void Message1(string xmessage)
    {
        MessageForm.Show(m_mainMenuForm, m_settings.DisplayMode, string.Format(CultureInfo.CurrentCulture,
                                                      Resources.PlayerSetFailed, "test"));
    }

    public void Message2(string xmessage)
    {
        MessageForm.Show(m_mainMenuForm, m_settings.DisplayMode, string.Format(CultureInfo.CurrentCulture,
                                   Resources.MessageName, "hello"));
    }

        private void GetPlayerCompleteAwardPoints(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error == null && e.Result == null) // the message didn't run
            {
                //                if (m_waitForm != null && !m_waitForm.IsDisposed && m_waitForm.WaitDialogIsActive)
                //                    m_waitForm.CloseForm();

                return;
            }

            try
            {
                // Set the error that occurred (if any).
                LastAsyncException = e.Error;
                Player player = null;

                if (e.Error == null)
                {
                    Tuple<Player, bool, bool> result = (Tuple<Player, bool, bool>)e.Result;
                    player = result.Item1;
                    bool updatePoints = result.Item2;

                    // If there is no sale, then start it.
                    //if (m_currentSale == null)
                    //    StartSale(false);

                    if (updatePoints)//false for both valid pin and invalid pin
                    {
                        if (CurrentPlayer != null) //we have one, update it
                        {
                            CurrentPlayer.PlayerCardPINError = player.PlayerCardPINError;
                            CurrentPlayer.PointsBalance = player.PointsBalance;
                            CurrentPlayer.PointsUpToDate = player.PointsUpToDate;
                        }
                    }
                    else
                    {
                        // Set the player we retrieved to the current player.
                        try
                        {
                            CurrentPlayer = player; //Do we want to assign the new value?
                            //SetPlayer(player, true, true);
                        }
                        catch (PlayerCenterException ex)
                        {
                            // TTP 50114
                            MessageForm.Show(m_mainMenuForm, m_settings.DisplayMode, string.Format(CultureInfo.CurrentCulture,
                                                 Resources.PlayerSetFailed, ex.Message));
                        }
                    }

                    if (!player.PlayerCardPINError && player.ErrorMessage != string.Empty)
                        MessageForm.Show(m_mainMenuForm, m_settings.DisplayMode, string.Format(CultureInfo.CurrentCulture,
                                     Resources.MessageName, player.ErrorMessage));
                }

                // US4809 ***
                //EventHandler<GetPlayerEventArgs> handler = GetPlayerCompletedAwardPoints;
                //if (handler != null)
                //    handler(this, new GetPlayerEventArgs(player, LastAsyncException));
            }
            catch (Exception ex)
            {
                Log("Error finishing player lookup " + ex.ToString(), LoggerLevel.Severe);
            }
            finally
            {
                // Close the wait form.
                if (m_waitForm != null && !m_waitForm.IsDisposed)
                    m_waitForm.CloseForm();

                DoneProcessingMessage(); // notify that we're done processing the message.
            }
        }


     

        private void DoneProcessingMessage()
        {
            // Since only one message can be sent at a time, we only have to remove the oldest message, we don't have to search and remove
            ServerMessage temp;
            pendingMessages.TryDequeue(out temp);

            if (pendingMessages.Count == 0) { }
                //IsBusy = false;
        }
           //  public event EventHandler<GetPlayerEventArgs> GetPlayerCompletedAwardPoints;
        // END: DE2476

        /// <summary>
        /// Gets a player's data from the server.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The DoWorkEventArgs object that 
        /// contains the event data.</param>
        private void GetPlayerData(object sender, DoWorkEventArgs e)
        {
            // Set the language.
            lock(Settings.SyncRoot)
            {
                if(Settings.ForceEnglish)
                    Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-US");

                Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture;
            }

            // Wait a couple of ticks to let the wait form display.
            System.Threading.Thread.Sleep(100);

            // Unbox the argument.
            int playerId = 0;
            string magCardNum = string.Empty;

            magCardNum = e.Argument as string;

            if(magCardNum == null)
                playerId = (int)e.Argument;

            // Are we getting the player by id or mag. card?
            if (playerId == 0)
            {
                FindPlayerByCardMessage cardMsg = new FindPlayerByCardMessage();
                cardMsg.MagCardNumber = magCardNum;

                // Send the message.
                try
                {
                    cardMsg.Send();
                }
                catch (ServerCommException)
                {
                    throw; // Don't repackage the ServerCommException
                }
                catch (Exception ex)
                {
                    throw new PlayerCenterException(string.Format(CultureInfo.CurrentCulture, Resources.GetPlayerFailed, ServerExceptionTranslator.FormatExceptionMessage(ex)), ex);
                }

                // Set the id that we got back from the server.
                if (cardMsg.PlayerId == 0)
                    throw new PlayerCenterException(Resources.NoPlayersFound);
                else
                    playerId = cardMsg.PlayerId;
            }

            Player player = null;
            
            try
            {
                player = new Player(playerId, OperatorID, -1);
            }
            catch(ServerCommException)
            {
                throw; // Don't repackage the ServerCommException
            }
            catch(ServerException exc)
            {
                throw new PlayerCenterException(string.Format(CultureInfo.CurrentCulture, Resources.GetPlayerFailed, ServerExceptionTranslator.FormatExceptionMessage(exc)) + " " + string.Format(CultureInfo.CurrentCulture, Resources.MessageName, exc.Message), exc);
            }
            catch(Exception exc)
            {
                throw new PlayerCenterException(string.Format(CultureInfo.CurrentCulture, Resources.GetPlayerFailed, ServerExceptionTranslator.FormatExceptionMessage(exc)), exc);
            }

            e.Result = player;
        }

        /// <summary>
        /// Handles the event when the get player data BackgroundWorker 
        /// is complete.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The RunWorkerCompletedEventArgs object that 
        /// contains the event data.</param>
        private void GetPlayerComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            // Set the error that occurred (if any).
            LastAsyncException = e.Error;

            // TTP 50067
            if(e.Error != null)
                LastPlayerFromServer = null;
            else
                LastPlayerFromServer = (Player)e.Result;

            // Close the wait form.
            // FIX: DE2476
            m_waitForm.CloseForm();
            // END: DE2476
        }

        #endregion

        #region Find Players

        // FIX: DE2476
        /// <summary>
        /// Creates a thread to find players on the server and sets
        /// the WaitForm's settings.
        /// </summary>
        /// <param name="magCardNumber">The mag card number to search on.  
        /// This value takes priority over last and first name.</param>
        /// <param name="firstName">The last name to search on.  
        /// If blank, last name isn't used in the search.</param>
        /// <param name="lastName">The first name to search on.
        /// If blank, first name isn't used in the search.</param>
        internal void FindPlayers(string magCardNumber, string firstName, string lastName)
        {
            // Set the wait message.
            m_waitForm.Message = Resources.WaitFormFindingPlayers;

            // Set the search params.
            string[] parameters = new string[3];
            parameters[0] = magCardNumber;
            parameters[1] = firstName;
            parameters[2] = lastName;

            // Create the worker thread and run it.
            m_worker = new BackgroundWorker();
            m_worker.WorkerReportsProgress = true;
            m_worker.WorkerSupportsCancellation = false;
            m_worker.DoWork += new DoWorkEventHandler(GetPlayerList);
            m_worker.ProgressChanged += new ProgressChangedEventHandler(m_waitForm.ReportProgress);
            m_worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(FindPlayersComplete);
            m_worker.RunWorkerAsync(parameters);
        }
        // END: DE2476

        /// <summary>
        /// Searches for players on the server.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The DoWorkEventArgs object that 
        /// contains the event data.</param>
        private void GetPlayerList(object sender, DoWorkEventArgs e)
        {
            // Set the language.
            lock(Settings.SyncRoot)
            {
                if(Settings.ForceEnglish)
                    Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-US");

                Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture;
            }

            // Wait a couple of ticks to let the wait form display.
            System.Threading.Thread.Sleep(100);

            // Unbox the search params.
            string[] parameters = (string[])e.Argument;

            if(parameters[0] != string.Empty) // Mag. Card
            {
                FindPlayerByCardMessage cardMsg = new FindPlayerByCardMessage();
                cardMsg.MagCardNumber = parameters[0];

                // Send the message.
                try
                {
                    cardMsg.Send();
                }
                catch(ServerCommException ex)
                {
                    throw ex; // Don't repackage the ServerCommException
                }
                catch(Exception ex)
                {
                    throw new PlayerCenterException(string.Format(Resources.GetPlayerFailed, FormatExceptionMessage(ex)), ex);
                }

                if (cardMsg.PlayerId > 0)
                {
                    PlayerListItem temp = new PlayerListItem();
                    temp.Id = cardMsg.PlayerId;
                    temp.FirstName = cardMsg.FirstName;
                    temp.LastName = cardMsg.LastName;
                    temp.MiddleInitial = cardMsg.MiddleInitial;

                    e.Result = new PlayerListItem[] { temp };
                }
            }
            else // First and Last Name
            {
                GetPlayerListMessage listMsg = new GetPlayerListMessage();
                listMsg.FirstName = parameters[1];
                listMsg.LastName = parameters[2];

                // Send the message.
                try
                {
                    listMsg.Send();
                }
                catch(ServerCommException ex)
                {
                    throw ex; // Don't repackage the ServerCommException
                }
                catch(Exception ex)
                {
                    throw new PlayerCenterException(string.Format(Resources.GetPlayerListFailed, FormatExceptionMessage(ex)), ex);
                }

                e.Result = listMsg.Players;
            }
        }

        /// <summary>
        /// Handles the event when the find player BackgroundWorker is 
        /// complete.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The RunWorkerCompletedEventArgs object that 
        /// contains the event data.</param>
        private void FindPlayersComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            // Set the error that occurred (if any).
            LastAsyncException = e.Error;

            if(e.Error == null)
            {
                // Set the results of the search.
                LastFindPlayersResults = (PlayerListItem[])e.Result;
            }
            else
            {
                LastFindPlayersResults = null;
            }

            // Close the wait form.
            // FIX: DE2476
            m_waitForm.CloseForm();
            // END: DE2476
        }

        #endregion

        #region Save Player

        //public void Message1()
        //{
        //    MessageForm.Show(m_mainMenuForm, m_settings.DisplayMode, string.Format(CultureInfo.CurrentCulture,
        //                                     Resources.PlayerSetFailed, "Hello"));
        //}

        // FIX: DE2476
        /// <summary>
        /// Creates a thread to save the player to the server and sets
        /// the WaitForm's settings.
        /// </summary>
        /// <param name="waitForm">The wait form to be used while 
        /// saving the player.</param>
        /// <param name="player">The player to save.</param>
        public void SavePlayer(Player player)
        {
            // Set the wait message.
            m_waitForm.Message = Resources.WaitFormSavingPlayer;

            // Create the worker thread and run it.
            m_worker = new BackgroundWorker();
            m_worker.WorkerReportsProgress = true;
            m_worker.WorkerSupportsCancellation = false;
            m_worker.DoWork += new DoWorkEventHandler(SavePlayerToServer);
            m_worker.ProgressChanged += new ProgressChangedEventHandler(m_waitForm.ReportProgress);
            m_worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(SavePlayerCompleted);
            m_worker.RunWorkerAsync(player);
        }
        // END: DE2476

        //US4119: Set PIN number >  Sale with player card and PIN has not been set.   
        /// <summary>
        /// Updates existing player
        /// </summary>
        /// <param name="player">The player to save.</param>
        public void UpdateExistingPlayer(Player player)
        {
            SetPlayerDataMessage setMsg = new SetPlayerDataMessage
            {
                PlayerId = player.Id,
                FirstName = player.FirstName,
                MiddleInitial = player.MiddleInitial,
                LastName = player.LastName,
                GovIssuedIdNumber = player.GovIssuedIdNumber,
                BirthDate = player.BirthDate,
                Email = player.Email,
                PlayerIdentity = player.PlayerIdentity,
                PhoneNumber = player.PhoneNumber,
                Gender = player.Gender,
                PinNumber = player.PinNumber,
                Address1 = player.Address1,
                Address2 = player.Address2,
                City = player.City,
                State = player.State,
                Zip = player.Zip,
                Country = player.Country,
                JoinDate = player.JoinDate,
                LastVisit = player.LastVisit,
                PointsBalance = player.PointsBalance,
                VisitCount = player.VisitCount,
                Comment = player.Comment,
                MagCardNumber = player.MagneticCardNumber
            };

            // Send the message.
            try
            {
                setMsg.Send();
            }
            catch (ServerCommException)
            {
                // TTP 50120
                throw; // Don't repackage the ServerCommException
            }
            catch (Exception ex)
            {
                string message = Resources.errorDupMagCard;
                if (setMsg.ReturnCode != 1)
                {
                    message = ex.Message;
                    throw new PlayerCenterException(string.Format(Resources.SavePictureFailed, FormatExceptionMessage(ex)), ex);
                }
                else
                {
                    throw new DuplicateException(message);
                }
            }
        }

        /// <summary>
        /// Adds or saves a player to the server.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The DoWorkEventArgs object that 
        /// contains the event data.</param>
        private void SavePlayerToServer(object sender, DoWorkEventArgs e)
        {
            // Set the language.
            lock(Settings.SyncRoot)
            {
                if(Settings.ForceEnglish)
                    Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-US");

                Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture;
            }

            // Wait a couple of ticks to let the wait form display.
            System.Threading.Thread.Sleep(100);

            // Unbox the argument.
            Player player = (Player)e.Argument;

            if(player.Id == 0) // We are creating a player.
            {
                CreateNewPlayerMessage createMsg = new CreateNewPlayerMessage();
                createMsg.FirstName = player.FirstName;
                createMsg.MiddleInitial = player.MiddleInitial;
                createMsg.LastName = player.LastName;
                createMsg.GovIssuedIdNumber = player.GovIssuedIdNumber;
                createMsg.BirthDate = player.BirthDate;
                createMsg.Email = player.Email;
                createMsg.PlayerIdentity = player.PlayerIdentity;
                createMsg.PhoneNumber = player.PhoneNumber;
                createMsg.Gender = player.Gender;
                createMsg.PinNumber = player.PinNumber;
                createMsg.Address1 = player.Address1;
                createMsg.Address2 = player.Address2;
                createMsg.City = player.City;
                createMsg.State = player.State;
                createMsg.Zip = player.Zip;
                createMsg.Country = player.Country;
                createMsg.JoinDate = player.JoinDate;
                createMsg.LastVisit = player.JoinDate; // set the last visit to join date on acct create, not blank --> player.LastVisit;
                createMsg.PointsBalance = player.PointsBalance;
                createMsg.VisitCount = player.VisitCount;
                createMsg.Comment = player.Comment;
                createMsg.MagCardNumber = player.MagneticCardNumber;

                // Send the message.
                try
                {
                    createMsg.Send();
                }
                catch(ServerCommException ex)
                {
                    Log("Server communication error sending the 'CreateNewPlayer' message " + ex.ToString(), LoggerLevel.Severe);
                    // TTP 50120
                    throw ex; // Don't repackage the ServerCommException
                }
                catch (Exception ex)
                {
                    Log("Error processing the 'CreateNewPlayer' message " + ex.ToString(), LoggerLevel.Severe);
                    string message = Resources.errorDupMagCard;
                    if (createMsg.ReturnCode != 1)
                    {
                        message = ex.Message;
                        throw new PlayerCenterException(string.Format(Resources.CreatePlayerFailed, FormatExceptionMessage(ex)), ex);
                    }
                    else
                    {
                        throw new DuplicateException(message);
                    }                      
                   
                }
                if (createMsg.PlayerId < 1) throw new PlayerCenterException(string.Format(Resources.CreatePlayerFailed, "Id < 1"));

                // Get the id returned.
                player.Id = createMsg.PlayerId;
            }
            else // We are updating a player
            {
                UpdateExistingPlayer(player); //US4119
            }

            // Save the player's picture (if applicable).
            SetPlayerImageMessage setPicMsg = new SetPlayerImageMessage();
            setPicMsg.PlayerId = player.Id;
            setPicMsg.Image = player.Image;

            try
            {
                setPicMsg.Send();
            }
            catch(ServerCommException)
            {
                // TTP 50120
                throw; // Don't repackage the ServerCommException
            }
            catch(Exception ex)
            {
                throw new PlayerCenterException(string.Format(Resources.SavePictureFailed, FormatExceptionMessage(ex)), ex);
            }

            //save the player's active status
            SetPlayerStatusCode.Save(player.Id, player.ActiveStatusList);

            // Now attempt to read back the information to make sure it saved.
            // TTP 50067
            Player reloadPlayer = null;

            try
            {
                reloadPlayer = new Player(player.Id, OperatorID);
            }
            catch(ServerCommException)
            {
                throw; // Don't repackage the ServerCommException
            }
            catch(ServerException exc)
            {
                throw new PlayerCenterException(string.Format(CultureInfo.CurrentCulture, Resources.GetPlayerFailed, ServerExceptionTranslator.FormatExceptionMessage(exc)) + " " + string.Format(CultureInfo.CurrentCulture, Resources.MessageName, exc.Message), exc);
            }
            catch(Exception exc)
            {
                throw new PlayerCenterException(string.Format(CultureInfo.CurrentCulture, Resources.GetPlayerFailed, ServerExceptionTranslator.FormatExceptionMessage(exc)), exc);
            }

            e.Result = reloadPlayer;
        }

        /// <summary>
        /// Handles the event when the player BackgroundWorker is complete.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The RunWorkerCompletedEventArgs object that 
        /// contains the event data.</param>
        private void SavePlayerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            // Set the error that occurred (if any).
            LastAsyncException = e.Error;

            // TTP 50067
            if(e.Error != null)
                LastPlayerFromServer = null;
            else
                LastPlayerFromServer = (Player)e.Result;

            // Close the wait form.
            // FIX: DE2476
            m_waitForm.CloseForm();
            // END: DE2476
        }

        // PDTS 1044
        /// <summary>
        /// Attempts to create a new player with just a mag. card number.
        /// </summary>
        /// <param name="magCardNum">The mag. card number to assign to the 
        /// newly created player.</param>
        /// <returns>The id of the created player.</returns>
        /// <exception cref="System.ArgumentException">magCardNum is null or an 
        /// empty string.</exception>
        /// <exception cref="GTI.Modules.Shared.ServerCommException">
        /// The server did not response to a message request.</exception>
        /// <exception cref="PlayerCenterException">
        /// A player with this mag. card number already exists.</exception>
        public int CreatePlayerForPOS(string magCardNum)
        {
            if(string.IsNullOrEmpty(magCardNum) || magCardNum.Trim().Length == 0)
                throw new ArgumentException("magCardNum");

            CreateNewPlayerMessage createMsg = new CreateNewPlayerMessage();
            createMsg.JoinDate = DateTime.Now;
            createMsg.LastVisit = createMsg.JoinDate;
            createMsg.MagCardNumber = magCardNum;

            // Send the message.
            try
            {
                createMsg.Send();
            }
            catch(ServerCommException ex)
            {
                Log("Server communication error sending the 'CreateNewPlayer' message " + ex.ToString(), LoggerLevel.Severe);
                throw ex; // Don't repackage the ServerCommException
            }
            catch(ServerException ex)
            {
                Log("Error processing the 'CreateNewPlayer' message " + ex.ToString(), LoggerLevel.Severe);
                if((int)ex.ReturnCode == 1)
                    throw new PlayerCenterException(Resources.errorDupMagCard);
                else
                    throw;
            }

            return createMsg.PlayerId;
        }

        #endregion

        #region Player Raffle Methods

        #region Player Report

        // FIX: DE2476
        // Rally US144
        /// <summary>
        /// Starts the process of generating a player list or mailing label report.
        /// </summary>
        /// <param name="args">The player list's arguments.</param>
        internal void StartGetPlayerReport(bool listReport, PlayerListParams args)
        {
            // Set the wait message.
            m_waitForm.Message = Resources.WaitFormGettingReport;

            // Create the worker thread and run it.
            m_worker = new BackgroundWorker();
            m_worker.WorkerReportsProgress = true;
            m_worker.WorkerSupportsCancellation = false;
            m_worker.DoWork += new DoWorkEventHandler(GetPlayerReport);
            m_worker.ProgressChanged += new ProgressChangedEventHandler(m_waitForm.ReportProgress);
            m_worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(GetPlayerReportComplete);

            // Box the arguments.
            object[] workerArgs = new object[2];
            workerArgs[0] = listReport;
            workerArgs[1] = args;

            m_worker.RunWorkerAsync(workerArgs);
        }
        // END: DE2476

        /// <summary>
        /// Gets a player report from the server and passes parameters to it.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The DoWorkEventArgs object that 
        /// contains the event data.</param>
        private void GetPlayerReport(object sender, DoWorkEventArgs e)
        {
            string drive, dir, dbServer, dbName, dbUser, dbPass;

            // Set the language and options.
            lock(Settings.SyncRoot)
            {
                if(Settings.ForceEnglish)
                    Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-US");

                Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture;

                drive = Settings.ClientInstallDrive;
                dir = Settings.ClientInstallRootDir;
                dbServer = Settings.DatabaseServer;
                dbName = Settings.DatabaseName;
                dbUser = Settings.DatabaseUser;
                dbPass = Settings.DatabasePassword;
            }

            // Wait a couple of ticks to let the wait form display.
            System.Threading.Thread.Sleep(100);

            // Unbox the arguments.
            object[] args = (object[])e.Argument;
            bool listReport = (bool)args[0];
            PlayerListParams listParams = (PlayerListParams)args[1];

            // Ask the server for the report.
            GetReportMessage reportMsg = null;
            
            if(listReport)
                reportMsg = new GetReportMessage((int)ReportIDs.Player_PlayerListLastName);
            else
                reportMsg = new GetReportMessage((int)ReportIDs.Player_PlayerMailingLabels);

            try
            {
                reportMsg.Send();
            }
            catch(ServerCommException)
            {
                throw; // Don't repackage the ServerCommException
            }
            catch(Exception ex)
            {
                throw new PlayerCenterException(string.Format(CultureInfo.CurrentCulture, Resources.GetReportFailed, ServerExceptionTranslator.FormatExceptionMessage(ex)), ex);
            }

            // Save the report to a temporary file.
            string path = drive + dir + @"\Temp";

            if(!Directory.Exists(path))
                Directory.CreateDirectory(path);

            path += @"\TempPlayerReport.rpt";

            FileStream fileStream = new FileStream(path, FileMode.Create);
            BinaryWriter writer = new BinaryWriter(fileStream);

            writer.Write(reportMsg.ReportFile);
            writer.Flush();
            writer.Close();

            // Clean up.
            // TTP 50135
            writer = null;
            fileStream.Dispose();
            fileStream = null;

            // Open the report back up in a Crystal Reports document.
            CrystalDecisions.CrystalReports.Engine.ReportDocument reportDoc = new CrystalDecisions.CrystalReports.Engine.ReportDocument();
            reportDoc.Load(path);

            // Set the database connection information.
            foreach(CrystalDecisions.Shared.IConnectionInfo connInfo in reportDoc.DataSourceConnections)
            {
                connInfo.SetConnection(dbServer, dbName, dbUser, dbPass);
            }

            // Set the parameters.
            DateTime invalidDate = new DateTime(1900, 1, 1);

            reportDoc.SetParameterValue("@OperatorID", OperatorID);
            //reportDoc.SetParameterValue("@Birthday", listParams.UseBirthday);

            if(listParams.UseBirthday)
            {
                reportDoc.SetParameterValue("@BDFrom", listParams.FromBirthday);
                reportDoc.SetParameterValue("@BDEnd", listParams.ToBirthday);
            }
            else
            {
                reportDoc.SetParameterValue("@BDFrom", invalidDate);
                reportDoc.SetParameterValue("@BDEnd", invalidDate);
            }

         //   reportDoc.SetParameterValue("@Gender", listParams.UseGender);

            if(listParams.UseGender)
            {
                reportDoc.SetParameterValue("@GenderType", listParams.Gender);
            }
            else
            {
                reportDoc.SetParameterValue("@GenderType", string.Empty);
            }


         //   reportDoc.SetParameterValue("@PointsBalance", listParams.UsePoints);
            if (listParams.PBIsRange) 
            {
                reportDoc.SetParameterValue("@Min", listParams.MinPoints);
                reportDoc.SetParameterValue("@Max", listParams.MaxPoints);
            }
            else
            {
                reportDoc.SetParameterValue("@Min", -1M);
                reportDoc.SetParameterValue("@Max", 0M);
            }

            //reportDoc.SetParameterValue("@PBisOption", listParams.PBIsOption);
            if (listParams.PBIsOption)
            {
                reportDoc.SetParameterValue("@PBOptionSelected", listParams.PBOptionSelected);
                reportDoc.SetParameterValue("@PBOptionValue", listParams.PBOptionValue);
            }
            else
            {
                reportDoc.SetParameterValue("@PBOptionSelected", string.Empty);
                reportDoc.SetParameterValue("@PBOptionValue", 0M);
            }

        //    reportDoc.SetParameterValue("@LastVisit", listParams.UseLastVisit);

            if(listParams.UseLastVisit)
            {
                reportDoc.SetParameterValue("@LVStart", listParams.FromLastVisit);
                reportDoc.SetParameterValue("@LVEnd", listParams.ToLastVisit);
            }
            else
            {
                reportDoc.SetParameterValue("@LVStart", invalidDate);
                reportDoc.SetParameterValue("@LVEnd", invalidDate);
            }

            reportDoc.SetParameterValue("@Spend", listParams.UseSpend);
            reportDoc.SetParameterValue("@Average", listParams.UseAverageSpend);

            if (listParams.SAIsRange)
            {
                reportDoc.SetParameterValue("@AmountFrom", listParams.FromSpend);
                reportDoc.SetParameterValue("@AmountTo", listParams.ToSpend);
                reportDoc.SetParameterValue("@StartDate", listParams.FromSpendDate);
                reportDoc.SetParameterValue("@EndDate", listParams.ToSpendDate);
            }
            else
            {
                reportDoc.SetParameterValue("@AmountFrom", 0M);
                reportDoc.SetParameterValue("@AmountTo", 0M);
                if (listParams.SAOption)
                {
                    reportDoc.SetParameterValue("@StartDate", listParams.FromSpendDate);
                    reportDoc.SetParameterValue("@EndDate", listParams.ToSpendDate);
                }
                else if(listParams.IsProduct) //TEST
                {
                    reportDoc.SetParameterValue("@StartDate", listParams.FromSpendDate);
                    reportDoc.SetParameterValue("@EndDate", listParams.ToSpendDate);
                }
                else
                {
                    reportDoc.SetParameterValue("@StartDate", invalidDate);
                    reportDoc.SetParameterValue("@EndDate", invalidDate);
                }

            }

            reportDoc.SetParameterValue("@SAOption", listParams.SAOption);
            if (listParams.SAOption)
            {
                reportDoc.SetParameterValue("@SAOptionSelected", listParams.SAOptionSelected);
                reportDoc.SetParameterValue("@SAOptionValue", listParams.SAOptionValue);
            }
            else
            {
                reportDoc.SetParameterValue("@SAOptionSelected", string.Empty);
                reportDoc.SetParameterValue("@SAOptionValue", 0M);
            }


            // Rally US493
           // reportDoc.SetParameterValue("@Status", listParams.UseStatus);

            if(listParams.UseStatus)
                reportDoc.SetParameterValue("@StatusId", listParams.Status);
            else
                reportDoc.SetParameterValue("@StatusId", string.Empty);

            if (listParams.IsLocation)
            {
                reportDoc.SetParameterValue("@LocationType", listParams.LocationType);
                reportDoc.SetParameterValue("@LocationDefinition", listParams.LocationDefinition);
            }
            else
            {
                reportDoc.SetParameterValue("@LocationType", 0M);
                reportDoc.SetParameterValue("@LocationDefinition", string.Empty);
            }

            reportDoc.SetParameterValue("@IsNOfDaysPlayed", listParams.IsNumberOfdDaysPlayed);          
            reportDoc.SetParameterValue("@IsNOfSessioPlayed", listParams.IsNumberOfSessionPlayed );
                        
            if (listParams.IsNumberOfdDaysPlayed || listParams.IsNumberOfSessionPlayed 
                || listParams.DaysOFweekAndSession != string.Empty   
                )   /*&& listParams.IsDPDateRange*/
            {
                    reportDoc.SetParameterValue("@DPDateRangeFrom", listParams.DPDateRangeFrom);
                    reportDoc.SetParameterValue("@DPDateRangeTo", listParams.DPDateRangeTo);
            }
                           
           else
            {
                    reportDoc.SetParameterValue("@DPDateRangeFrom", invalidDate);
                    reportDoc.SetParameterValue("@DPDateRangeTo", invalidDate);
            }

            reportDoc.SetParameterValue("@IsDPRange", listParams.IsDPRange);

            if (listParams.IsNumberOfdDaysPlayed && listParams.IsDPRange)
            {

                int min = Int32.Parse(listParams.DPRangeFrom); 
                int max = Convert.ToInt32(listParams.DPRangeTo);  
                
                reportDoc.SetParameterValue("@DPRangeFrom",min);
                reportDoc.SetParameterValue("@DPRangeTo", max);
            }
            else
            {
                reportDoc.SetParameterValue("@DPRangeFrom", 0M);
                reportDoc.SetParameterValue("@DPRangeTo", 0M);
            }

            reportDoc.SetParameterValue("@IsDPOption", listParams.IsDPOption); 

            if (listParams.IsNumberOfdDaysPlayed && listParams.IsDPOption)
            {
                reportDoc.SetParameterValue("@DPOprtionSelected", listParams.DPOptionSelected);
                reportDoc.SetParameterValue("@DPOptionValue", Convert.ToInt32(listParams.DPOptionValue , CultureInfo.CurrentCulture));
                //reportDoc.SetParameterValue("@DPOptionValue", listParams.DPOptionValue);
            }
            else
            {
                reportDoc.SetParameterValue("@DPOprtionSelected", string.Empty);
                reportDoc.SetParameterValue("@DPOptionValue", 0M);
            }


            reportDoc.SetParameterValue("@IsSPRange", listParams.IsSPRange);

            if (listParams.IsNumberOfSessionPlayed && listParams.IsSPRange)
            {
                reportDoc.SetParameterValue("@SPRangeFrom", Convert.ToInt32(listParams.SPRangeFrom , CultureInfo.CurrentCulture) );
                reportDoc.SetParameterValue("@SpRangeTo", Convert.ToInt32(listParams.SPRangeTo, CultureInfo.CurrentCulture));
            }
            else
            {
                reportDoc.SetParameterValue("@SPRangeFrom", 0M);
                reportDoc.SetParameterValue("@SPRangeTo", 0M);
            }

            reportDoc.SetParameterValue("@IsSPOption", listParams.IsSPOption);

            if (listParams.IsNumberOfSessionPlayed && listParams.IsSPOption)
            {
                reportDoc.SetParameterValue("@SPOprtionSelected", listParams.SPOptionSelected);
                reportDoc.SetParameterValue("@SPOptionValue", Convert.ToInt32(listParams.SPOptionValue, CultureInfo.CurrentCulture));
            }
            else
            {
                reportDoc.SetParameterValue("@SPOprtionSelected", string.Empty);
                reportDoc.SetParameterValue("@SPOptionValue", 0M);
            }

            if (listParams.DaysOFweekAndSession != string.Empty)
            {
                reportDoc.SetParameterValue("@DaysOfWeekNSessionNbr", listParams.DaysOFweekAndSession);     
            }
            else
            {
                reportDoc.SetParameterValue("@DaysOfWeekNSessionNbr", string.Empty);   
            }

            reportDoc.SetParameterValue("@IsPackageName", listParams.IsProduct);

            if (listParams.IsProduct)  
            {
                reportDoc.SetParameterValue("@PackageName", listParams.SelectedProduct);  
            }
            else
            {
                reportDoc.SetParameterValue("@PackageName", string.Empty);    
            }


            //US2649

            e.Result = reportDoc;
        }

        /// <summary>
        /// Handles the event when the player report BackgroundWorker is complete.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The RunWorkerCompletedEventArgs object that 
        /// contains the event data.</param>
        private void GetPlayerReportComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            // Set the error that occurred (if any).
            LastAsyncException = e.Error;

            if(e.Error == null)
            {
                if(m_reportForm == null)
                    m_reportForm = new ReportForm(this);

                try
                {
                    m_reportForm.Report = (ReportDocument)e.Result;//On TEST jkc
                }
                catch(Exception ex)
                {
                    LastAsyncException = ex;
                }
            }

            // Close the wait form.
            // FIX: DE2476
            m_waitForm.CloseForm();
            // END: DE2476
        }

        #endregion

        #region Export Player List

        // FIX: DE2476
        /// <summary>
        /// Starts the process of generating an export file with player 
        /// information.
        /// </summary>
        /// <param name="fileName">The name of the file to save the list 
        /// to.</param>
        /// <param name="args">The player list's arguments.</param>
        internal void StartExportPlayerList(string fileName, PlayerListParams args)
        {
            // Set the wait message.
            m_waitForm.Message = Resources.WaitFormExportingList;

            // Create the worker thread and run it.
            m_worker = new BackgroundWorker();
            m_worker.WorkerReportsProgress = true;
            m_worker.WorkerSupportsCancellation = false;
            m_worker.DoWork += new DoWorkEventHandler(ExportPlayerList);
            m_worker.ProgressChanged += new ProgressChangedEventHandler(m_waitForm.ReportProgress);
            m_worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(ExportPlayerListComplete);

            // Box the arguments.
            object[] workerArgs = new object[2];
            workerArgs[0] = fileName;
            workerArgs[1] = args;

            m_worker.RunWorkerAsync(workerArgs);
        }
        
        // END: DE2476
        // US2149 - Enclose text field in double quotes.
        /// <summary>
        /// Encloses the specified string in double quotes and escapes any
        /// embedded double quotes.
        /// </summary>
        /// <param name="field">The field to escape.</param>
        /// <returns>The escaped field.</returns>
        private string EscapeTextField(string field)
        {
            StringBuilder fieldBuilder = new StringBuilder(field);

            fieldBuilder.Replace("\"", "\"\"");

            fieldBuilder.Insert(0, '"').Append('"');

            return fieldBuilder.ToString();
        }

        /// <summary>
        /// Gets a player report from the server and passes parameters to it.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The DoWorkEventArgs object that 
        /// contains the event data.</param>
        private void ExportPlayerList(object sender, DoWorkEventArgs e)
        {
            // Set the language.
            lock(Settings.SyncRoot)
            {
                if(Settings.ForceEnglish)
                    Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-US");

                Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture;
            }

            // Wait a couple of ticks to let the wait form display.
            System.Threading.Thread.Sleep(100);

            // Unbox the arguments.
            object[] args = (object[])e.Argument;
            string fileName = (string)args[0];
            PlayerListParams listParams = (PlayerListParams)args[1];//

            // Rally DE1872
            GetPlayerListReportMessage listMsg = new GetPlayerListReportMessage(listParams);

            try
            {
                listMsg.Send();
            }
            catch(ServerCommException)
            {
                throw; // Don't repackage the ServerCommException
            }
            catch(Exception ex)
            {//ERROR Here
                throw new PlayerCenterException(string.Format(CultureInfo.CurrentCulture, Resources.GetPlayerListFailed, ServerExceptionTranslator.FormatExceptionMessage(ex)), ex);
            }

            if(listMsg.Players != null && listMsg.Players.Length > 0)
            {
                // Write the data out to the specified file.
                StreamWriter writer = File.CreateText(fileName);

                // US1872 - Add headers to the export file.
                // US2149 - Enclose text field in double quotes.
                writer.Write(EscapeTextField(Resources.PlayerId));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.FirstName));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.MiddleInitial));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.LastName));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.BirthDate));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.Email));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.Gender));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.Address1));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.Address2));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.City));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.State));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.Zip));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.Country));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.RefundableCredit));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.NonRefundableCredit));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.LastVisit));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.PointsBalance));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.TotalSpend));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.AverageSpend));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.VisitCount));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.StatusList));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.GovIssuedIdNum));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.PlayerIdentity));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.PhoneNumber));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.JoinDate));
                writer.Write(Resources.ExportSeparator);
                writer.Write(EscapeTextField(Resources.Comment));
                writer.Write(Resources.ExportSeparator);
                writer.WriteLine(EscapeTextField(Resources.MagCardNumber));

                foreach(PlayerExportItem item in listMsg.Players)
                {
                    if(item.Player != null)
                    {
                        // US1769 - Add more fields to export.
                        writer.Write(item.Player.Id);
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(EscapeTextField(item.Player.FirstName));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(EscapeTextField(item.Player.MiddleInitial));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(EscapeTextField(item.Player.LastName));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(item.Player.BirthDate.ToShortDateString());
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(EscapeTextField(item.Player.Email));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(EscapeTextField(item.Player.Gender));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(EscapeTextField(item.Player.Address1));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(EscapeTextField(item.Player.Address2));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(EscapeTextField(item.Player.City));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(EscapeTextField(item.Player.State));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(EscapeTextField(item.Player.Zip));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(EscapeTextField(item.Player.Country));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(item.Player.RefundableCredit.ToString("0.00", CultureInfo.CurrentCulture));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(item.Player.NonRefundableCredit.ToString("0.00", CultureInfo.CurrentCulture));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(item.Player.LastVisit.ToShortDateString());
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(item.Player.PointsBalance.ToString("0.00", CultureInfo.CurrentCulture));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(item.Player.TotalSpend.ToString("0.00", CultureInfo.CurrentCulture));
                        writer.Write(Resources.ExportSeparator);
                        if (/*item.AverageSpend == 0 ||*/ item.AverageSpend == null)
                        {
                            writer.Write("");
                        }
                        else
                        {
                            writer.Write(item.AverageSpend);
                        }
//                        writer.Write(item.AverageSpend.ToString("0.00", CultureInfo.CurrentCulture));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(item.Player.VisitCount);
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(EscapeTextField(item.StatusList));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(EscapeTextField(item.Player.GovIssuedIdNumber));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(EscapeTextField(item.Player.PlayerIdentity));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(EscapeTextField(item.Player.PhoneNumber));
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(item.Player.JoinDate.ToShortDateString());
                        writer.Write(Resources.ExportSeparator);
                        writer.Write(EscapeTextField(item.Player.Comment));
                        writer.Write(Resources.ExportSeparator);
                        writer.WriteLine(EscapeTextField(item.Player.MagneticCardNumber));
                    }
                }

                writer.Flush();
                writer.Close();
                writer.Dispose();

                e.Result = listMsg.Players.Length;
            }
            else
                e.Result = 0;
            
        }

        /// <summary>
        /// Handles the event when the export player list BackgroundWorker is 
        /// complete.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The RunWorkerCompletedEventArgs object that 
        /// contains the event data.</param>
        private void ExportPlayerListComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            // Set the error that occurred (if any).
            LastAsyncException = e.Error;

            // Did we export any players?
            if(e.Error == null)
                LastNumPlayersExported = (int)e.Result;

            // Close the wait form.
            // FIX: DE2476
            m_waitForm.CloseForm();
            // END: DE2476
        }

        #endregion

        #region Print Player Raffle

        // US4781
        /// <summary>
        /// Runs the "print player raffle" functionality on a separate thread
        /// </summary>
        /// <param name="playerListFilters"></param>
        internal void StartPrintPlayerRaffle(PlayerListParams playerListFilters)
        {
            // Set the wait message.
            m_waitForm.Message = Resources.WaitFormPrintingPlayerRaffle;
            m_waitForm.CancelButtonVisible = true;
            m_waitForm.CancelButtonClick += new EventHandler(m_waitFormPrintRaffle_CancelButtonClick);

            // Create the worker thread and run it.
            m_worker = new BackgroundWorker();
            m_worker.WorkerReportsProgress = true;
            m_worker.WorkerSupportsCancellation = true;
            m_worker.DoWork += new DoWorkEventHandler(PrintPlayerRaffle);
            m_worker.ProgressChanged += new ProgressChangedEventHandler(m_waitForm.ReportProgress);
            m_worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(PrintPlayerRaffleComplete);
            
            m_worker.RunWorkerAsync(playerListFilters);
        }

        /// <summary>
        /// Performs the player list lookup and the printing of the player raffle tickets
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PrintPlayerRaffle(object sender, DoWorkEventArgs e)
        {
            PlayerListParams listParams = (PlayerListParams)e.Argument;
            string printer = Settings.POSreceiptPrinterName;

            GetPlayerListReportMessage listMsg = new GetPlayerListReportMessage(listParams);
            try
            {
                listMsg.Send();
            }
            catch (ServerCommException ex)
            {
                Log("Server communication error sending the 'GetPlayerListReport' message " + ex.ToString(), LoggerLevel.Severe);
                throw ex; // Don't repackage the ServerCommException
            }
            catch (Exception ex)
            {
                Log("Error processing the 'GetPlayerListReport' message " + ex.ToString(), LoggerLevel.Severe);
                throw new PlayerCenterException(string.Format(CultureInfo.CurrentCulture, Resources.GetPlayerListFailed, ServerExceptionTranslator.FormatExceptionMessage(ex)), ex);
            }

            if (m_worker.CancellationPending)
                return;

            decimal playerCount = listMsg.Players == null ? 0 : listMsg.Players.Length;

            if (playerCount > 0)
            {
                // update the wait message. We're on a separate thread now, so we have to call back to the UI.
                m_waitForm.BeginInvoke(((Action)(()=>
                {
                    m_waitForm.Message = String.Format(Resources.WaitFormPrintingPlayerRaffle2, playerCount);
                    m_waitForm.ProgressBarVisible = true;
                })));

                string raffleName = listParams.ListName;
                // if the raffle is for one day, print the gaming date and session instead of the raffle name for Colusa
                if (!String.IsNullOrWhiteSpace(listParams.DaysOFweekAndSession)
                    && listParams.DPDateRangeFrom != DateTime.MinValue
                    && listParams.DPDateRangeTo != DateTime.MinValue
                    && listParams.DPDateRangeTo.Subtract(listParams.DPDateRangeFrom).Days == 0)
                {
                    Dictionary<string, List<int>> dayOfWeekAndSession = ConvertDayAndSessionString(listParams.DaysOFweekAndSession);
                    string dayOfWeek = listParams.ToLastVisit.DayOfWeek.ToString().Substring(0, 3);
                    string allDays = "All";
                    // if the session filter contains something in the date range
                    if (dayOfWeekAndSession.ContainsKey(allDays) ||
                        dayOfWeekAndSession.ContainsKey(dayOfWeek))
                    {
                        HashSet<int> sessions = new HashSet<int>();
                        if (dayOfWeekAndSession.ContainsKey(allDays))
                        {
                            foreach (int session in dayOfWeekAndSession[allDays])
                                sessions.Add(session);
                        }
                        if (dayOfWeekAndSession.ContainsKey(dayOfWeek))
                        {
                            foreach (int session in dayOfWeekAndSession[dayOfWeek])
                                sessions.Add(session);
                        }
                        
                        raffleName = String.Format("Gaming Date: {0}, Session(s): {1}",
                            listParams.DPDateRangeFrom.ToShortDateString(), String.Join(",", sessions));
                    }
                }

                decimal progress = 0, percentage = 0;
                foreach (var player in listMsg.Players)
                {
                    try
                    {
                        if (m_worker.CancellationPending) // remove print objects in OS's printer queue?
                            return;
                        PlayerRaffleReceipt receipt = new PlayerRaffleReceipt(player, raffleName);
                        receipt.Print(printer, 1);
                    }
                    catch (Exception ex)
                    {
                        string message = "Error printing the player's raffle ticket " + ex.ToString();
                        Log(message, LoggerLevel.Severe);
                        message += Environment.NewLine + Environment.NewLine + "Would you like to continue printing?";
                        DialogResult result = MessageForm.Show(message, "Error printing", MessageFormTypes.YesCancel);
                        if (result == DialogResult.Cancel)
                            break;
                    }
                    percentage = (++progress/playerCount)*100.0m;

                    m_worker.ReportProgress((int)percentage);
                }
            }
            e.Result = (int)playerCount;
        }

        /// <summary>
        /// Actions that occur when the player raffle print completes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PrintPlayerRaffleComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            // Set the error that occurred (if any).
            LastAsyncException = e.Error;

            // Close the wait form.
            m_waitForm.CloseForm();
            m_waitForm.ProgressBarVisible = false;
        }

        /// <summary>
        /// Cancels printing the player raffle receipts
        /// </summary>
        internal void CancelPrintPlayerRaffle()
        {
            Log("Physical player raffle printing was cancelled by user", LoggerLevel.Message);
            m_waitForm.BeginInvoke(((Action)(() =>
            {
                m_waitForm.Message = "Cancelling...";
            })));
            // Note: this should only be able to be called while the "printing raffle" window is displaying, but will cancel any worker that supports cancellation (looks like the others do not, however)
            if (m_worker.WorkerSupportsCancellation)
                m_worker.CancelAsync();
        }

        /// <summary>
        /// Actions that occur when the user presses the "cancel" button on the wait form
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void m_waitFormPrintRaffle_CancelButtonClick(object sender, EventArgs e)
        {
            m_waitForm.CancelButtonClick -= m_waitFormPrintRaffle_CancelButtonClick;
            CancelPrintPlayerRaffle();

            //Reset wait form properties
            m_waitForm.CancelButtonVisible = false;
        }

        /// <summary>
        /// Takes the day of week and session filter list from the player list filter and converts it into something that's easier to use
        /// </summary>
        /// <param name="dayAndSession">string that has a day of the week and session for use in the player list filter formatted like 'All (1),Mon (1:2:3:4:5)'</param>
        /// <returns>a map of the 3 character day of week to the list of sessions for that day</returns>
        internal static Dictionary<string, List<int>> ConvertDayAndSessionString(string dayAndSession)
        {
            Dictionary<string, List<int>> daysToSessionList = new Dictionary<string, List<int>>();
            List<string> filters = new List<string>(dayAndSession.Split(','));

            string day, sessString;
            int dayLoc, session;
            List<int> sessions;
            foreach (string dayOfWeek in filters)
            {
                dayLoc = dayOfWeek.IndexOf("(");
                day = dayOfWeek.Substring(0, dayLoc - 1);       // parse out the day
                sessString = dayOfWeek.Substring(dayLoc + 1);   // take out the rest
                sessString = sessString.Replace(")", "");       // remove parenthesis

                sessions = new List<int>();
                List<string> sesSplit = new List<string>(sessString.Split(':'));
                foreach (string sesStr in sesSplit)             //parse out sessions
                {
                    if (Int32.TryParse(sesStr, out session))
                        sessions.Add(session);
                }

                daysToSessionList.Add(day, sessions);
            }

            return daysToSessionList;
        }
        #endregion

        #endregion

        /// <summary>
        /// Cancels any pending transactions and shuts down the POS.
        /// </summary>
        public void Shutdown()
        {
            Log("Shutting down.", LoggerLevel.Debug);

            // PDTS 1064
            if(!m_externalMagCardReader && MagCardReader != null)
            {
                MagCardReader.EndReading();
                MagCardReader.RemoveAllSources();
            }

            MagCardReader = null;

            m_module = null;
            m_deviceId = 0;
            m_machineId = 0;
            //m_workstationId = 0;
            m_worker = null;
            LastAsyncException = null;

            // FIX: DE2476
            if(m_waitForm != null)
            {
                m_waitForm.Dispose();
                m_waitForm = null;
            }
            // END: DE2476

            if(m_reportForm != null)
            {
                m_reportForm.Dispose();
                m_reportForm = null;
            }

            if(m_mainMenuForm != null)
            {
                m_mainMenuForm.Dispose();
                m_mainMenuForm = null;
            }

            if(m_loadingForm != null)
            {
                m_loadingForm.CloseForm();
                m_loadingForm.Dispose();
                m_loadingForm = null;
            }

            if(!Settings.ShowCursor)
                Cursor.Show();

            Settings = null;

            OperatorID = 0;

            Log("Shutdown complete.", LoggerLevel.Information);
            m_loggingEnabled = false;

            IsInitialized = false;
        }
        #endregion

        #region Member Properties
        // FIX: DE2476
        internal bool IsTouchScreen { get; set; }
        // END: DE2476
        /// <summary>
        /// Gets whether the PointOfSale was initialized.
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// Gets the Player Center's current settings.
        /// </summary>
        public PlayerCenterSettings Settings { get { return m_settings; } set { m_settings = value; } }


        public bool StaffHasPermissionToAwardPoints { get; set; }       //US2001

        /// <summary>
        /// Gets whether to allow picture capturing.
        /// </summary>
        public bool AllowPictureCapture
        {
            get
            {
                return (m_deviceId != Device.POSPortable.Id);
            }
        }

        /// <summary>
        /// Gets or sets the last exception that was thrown by another thread.
        /// </summary>
        public Exception LastAsyncException
        {
            get
            {
                lock(m_errorSync)
                {
                    return m_asyncException;
                }
            }
            set
            {
                lock(m_errorSync)
                {
                    m_asyncException = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the current player for the Player Management form.
        /// </summary>
        public Player CurrentPlayer { get; set; }

        /// <summary>
        /// Gets or sets the results of the last FindPlayers call.
        /// </summary>
        internal PlayerListItem[] LastFindPlayersResults
        {
            get
            {
                lock(m_findPlayerSync)
                {
                    return m_lastFindPlayersResults;
                }
            }
            set
            {
                lock(m_findPlayerSync)
                {
                    m_lastFindPlayersResults = value;
                }
            }
        }

        // TTP 50067
        /// <summary>
        /// Gets or sets the last player loaded from the server.
        /// </summary>
        internal Player LastPlayerFromServer
        {
            get
            {
                lock(m_lastPlayerSync)
                {
                    return m_lastPlayerFromServer;
                }
            }
            set
            {
                lock(m_lastPlayerSync)
                {
                    m_lastPlayerFromServer = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the last picture taken using the camera.
        /// </summary>
        internal Bitmap LastPlayerPic
        {
            get
            {
                lock(m_playerPicSync)
                {
                    return m_lastPlayerPic;
                }
            }
            set
            {
                lock(m_playerPicSync)
                {
                    m_lastPlayerPic = value;
                }
            }
        }

        // Rally US144
        /// <summary>
        /// Gets the number of players returned in the last export.
        /// </summary>
        internal int LastNumPlayersExported { get; private set; }

        // PDTS 1064
        /// <summary>
        /// Gets the MagneticCardReader instance for PlayerCenter.
        /// </summary>
        internal MagneticCardReader MagCardReader { get; private set; }

        //internal bool StaffHasPermissionToAdjustPointsManually { get { return StaffHasPermissionToAwardPoints; } }

        // Rally US144
        /// <summary>
        /// Displays the report form.
        /// </summary>
        internal void ShowReportForm()
        {
            if(m_reportForm != null)
            {
                m_reportForm.Show();//On TEST jkc
                m_reportForm.BringToFront();
            }
        }

        public int GetOperatorId()
        {
         return OperatorID;   
        }

        internal static int OperatorID { get; private set; }
        internal static List<PlayerStatus> OperatorPlayerStatusList { get; private set; }
        internal static List<PackageItem> PackageListName { get; private set; }  
        internal static List<LocationCity> ListLocationCity { get; private set; }
        internal static List<LocationState> ListLocationState { get; private set; }
        internal static List<LocationZipCode> ListLocationZipCode { get; private set; }
        internal static List<LocationCountry> ListLocationCountry { get; private set; } 

        
        #endregion
    }

    // Rally US144
    /// <summary>
    /// The parameters to the player list report.
    /// </summary>
    internal struct PlayerListParams
    {
        public bool UseBirthday;
        public DateTime FromBirthday;
        public DateTime ToBirthday;
        public bool UseGender;
        public string Gender;      
        public bool UsePoints;
        public bool PBIsOption;
        public bool PBIsRange;
        public string PBOptionSelected;
        public decimal PBOptionValue;     
        public decimal MinPoints;
        public decimal MaxPoints;
        public bool UseLastVisit;
        public DateTime FromLastVisit;
        public DateTime ToLastVisit;
        public bool UseSpend;
        public bool UseAverageSpend;
        public bool SAIsRange;
        public decimal FromSpend;
        public decimal ToSpend;
        public DateTime FromSpendDate;
        public DateTime ToSpendDate;
        public bool SAOption;
        public string SAOptionSelected;
        public decimal SAOptionValue;
        // Rally US493
        public bool UseStatus;
        public string Status;
        public bool IsLocation;
        public int LocationType;
        public string LocationDefinition;
        public bool UseZipCode;
        public string ZipCode;
        public bool UseCity;
        public string City;
        public bool UseState;
        public string State;
        public bool UseCountry;
        public string Country;
        public bool IsNumberOfdDaysPlayed;
        public DateTime DPDateRangeFrom;
        public DateTime DPDateRangeTo;
        public bool IsDPRange;
        public string DPRangeFrom;
        public string DPRangeTo;
        public bool IsDPOption;
        public string DPOptionSelected;
        public string DPOptionValue;
        public bool IsNumberOfSessionPlayed;
        public bool IsSPRange;
        public string SPRangeFrom;
        public string SPRangeTo;
        public bool IsSPOption;
        public string SPOptionSelected;
        public string SPOptionValue;
        public string DaysOFweekAndSession;
        public bool IsProduct;
        public string SelectedProduct;
        public string ListName;
    }

    internal class GetOperatorID
    {
        public static int operatorID;
    }

        //public class GetPlayerEventArgs : EventArgs
    //{
    //    #region Constructors
    //    /// <summary>
    //    /// Initializes a new instance of the GetPlayerEventArgs class.
    //    /// </summary>
    //    /// <param name="player">The player found.</param>
    //    public GetPlayerEventArgs(Player player)
    //    {
    //        Player = player;
    //    }

    //    /// <summary>
    //    /// Initializes a new instance of the GetPlayerEventArgs class.
    //    /// </summary>
    //    /// <param name="ex">The exception encountered while looking up the player.</param>
    //    public GetPlayerEventArgs(Exception ex)
    //    {
    //        Error = ex;
    //    }

    //    /// <summary>
    //    /// Initializes a new instance of the GetPlayerEventArgs class.
    //    /// </summary>
    //    /// <param name="player">The player found.</param>
    //    /// <param name="ex">The exception encountered while looking up the player.</param>
    //    public GetPlayerEventArgs(Player player, Exception ex)
    //    {
    //        Player = player;
    //        Error = ex;
    //    }
    //    #endregion

        //#region Member Variables
        ///// <summary>
        ///// Gets the player found or null an error occurred.
        ///// </summary>
        //public Player Player
        //{
        //    get;
        //     set;
        //}

        ///// <summary>
        ///// The error encountered while getting the player information
        ///// </summary>
        //public Exception Error
        //{
        //    get;
        //    set;
        //}
        //#endregion
    //}
}
