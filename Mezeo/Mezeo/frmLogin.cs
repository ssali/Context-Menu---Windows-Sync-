using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using System.Threading;
using System.Globalization;
using System.Resources;
using MezeoFileSupport;
using AppLimit.NetSparkle;
using System.IO;

// To compile this in Visual Studio 2010 with .NET 4.0 open Properties for
// the IWshRuntimeLibrary reference and change 'Embed Interop Types' from true to false.
using IWshRuntimeLibrary;  // Requires a reference to "C:\Windows\System32\wshom.ocx".
using System.Security;
using System.Security.AccessControl;

namespace Mezeo
{
    public partial class frmLogin : Form
    {
        NotificationManager notificationManager;
        CloudService mezeoFileCloud;
        
        MezeoFileSupport.LoginDetails loginDetails;
        frmSyncManager syncManager;

        public bool isLoginSuccess = false;
        public bool showLogin = false;
        public bool isFromSyncMgrVerification = false;
        private Sparkle _sparkle;

        //Adding for installer with upgread
       //static uint s_uTaskbarRestart;
        public static AutoResetEvent _Appexit = new AutoResetEvent(false);
       
        public frmLogin()
        {
            InitializeComponent();

            this.Icon = Properties.Resources.MezeoVault;

            //this.HandleCreated += new EventHandler(frmLogin_HandleCreated);
            // this.HandleDestroyed += new EventHandler(frmLogin_HandleDestroyed);
            notificationManager = new NotificationManager();
            notificationManager.NotificationHandler = this.niSystemTray;

            //niSystemTray.ContextMenuStrip = cmSystemTrayLogin;

            mezeoFileCloud = new CloudService();

            LoadResources();

            string RSSFeed = BasicInfo.GetUpdateURL();
            TimeSpan checkFrequency = new TimeSpan(Convert.ToInt32(global::Mezeo.Properties.Resources.BrUpdateTimer), 0, 0);

            if (RSSFeed.Length != 0)
            {
                _sparkle = new Sparkle(RSSFeed);
                _sparkle.StartLoop(true, false, checkFrequency);
            }

            EventQueue.InitEventQueue(BasicInfo.MacAddress);
        }

        private void frmLogin_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.WindowState = FormWindowState.Minimized;
            }
            else
            {
                niSystemTray.Visible = false;
            }
        }

        private void niSystemTray_DoubleClick(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Normal;
            this.Focus();
        }

        private void frmLogin_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
            }
            else
            {
                this.ShowInTaskbar = true;
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {

            if (syncManager != null && (syncManager.IsSyncThreadInProgress()))
            {
                DialogResult dResult = MessageBox.Show(LanguageTranslator.GetValue("MezeoExitString1") + "\n" + LanguageTranslator.GetValue("MezeoExitString2"), AboutBox.AssemblyTitle, MessageBoxButtons.OKCancel);
                if (dResult == DialogResult.Cancel)
                    return;
                    
                syncManager.ApplicationExit();
              
            }

            niSystemTray.Visible = false;
            _Appexit.Set();
            System.Environment.Exit(0);
        }

        private void niSystemTray_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {

                MethodInfo mi = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
                mi.Invoke(niSystemTray, null);
            }
        }

        private void btnLogin_Click(object sender, EventArgs e)
        {
            /*Change done for: If login promt from syncMgr then on click 'Login' only verify login credentials and launch syncMgr. 
                In this case we do not need to verify NQ , Sync directory or DB existence.*/
            if (isFromSyncMgrVerification)
            {
                VerifyCredentialsAgainFromSyncMgr();
            }
            else
            {
                Login();
            }
        }

        private void HideUrl()
        {
            if (Convert.ToBoolean(global::Mezeo.Properties.Resources.BrServerUrlVisible) == false)
            {
                txtServerUrl.Visible = false;
            }
            else
            {
                txtServerUrl.Visible = true;
            }
        }
        public void Login()
        {
            CheckForIllegalCrossThreadCalls = false;

            this.UseWaitCursor = true;

            this.txtUserName.Enabled = false;
            this.txtPasswrod.Enabled = false;
            this.txtServerUrl.Enabled = false;
            this.btnLogin.Enabled = false;
            this.labelError.Text = "";

            bwLogin.RunWorkerAsync();
        }

        public void bwCheckServerStatus_DoWork(object sender, DoWorkEventArgs e)
        {
            TimeSpan timeOnline = new TimeSpan(0, 5, 0);
            TimeSpan timeOffline = new TimeSpan(0, 1, 0);
            TimeSpan time = timeOnline;
            while (!_Appexit.WaitOne(time))
            {
                syncManager.CheckServerStatus();
                if (syncManager.CanNotTalkToServer())
                    time = timeOffline;  // Wait for a lesser amount of time before checking on the server status.
                else
                    time = timeOnline;  // Wait for a longer period of time before checking on the server status.
            }
        }

        private string validateServiceUrl(string url)
        {
            if (url.Length < 8)
                return url;

            if(!url.Substring(0,8).Equals("https://"))
	        {
                url = "https://" + url;
                txtServerUrl.Text = url;
	        }

            if (!url.Substring(url.Length-3,3).Equals("/v2"))
	        {
		        url += "/v2";
	        }

            return url;
        }

        private void LoadResources()
        {
            this.Text = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + LanguageTranslator.GetValue("LoginFormTitle");
            
            //Loading resources at runtime for login screen
            this.txtUserName.CueText = LanguageTranslator.GetValue("UserIdCueText");
            this.txtPasswrod.CueText = LanguageTranslator.GetValue("PasswordCueText");

            this.txtServerUrl.CueText = global::Mezeo.Properties.Resources.BrServerUrlCueText;
            this.txtServerUrl.Text = global::Mezeo.Properties.Resources.BrServerUrlCueText;
            //Show or hide url part of branding stuff
            HideUrl();
            this.labelError.Text = "";

            //Loading resources at runtime for for context menu 
            this.toolStripMenuItem2.Text = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion;
            this.toolStripMenuItem6.Text = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion;
          
  
            this.btnLogin.Text = LanguageTranslator.GetValue("LoginButtonText");	
            this.exitToolStripMenuItem.Text	 = LanguageTranslator.GetValue("ExitSyncManager");
	        this.loginToolStripMenuItem.Text = LanguageTranslator.GetValue("LoginButtonText");

            this.menuItem3.Text = LanguageTranslator.GetValue("ExitSyncManager");

            this.msShowSyncMgr.Text = LanguageTranslator.GetValue("SyncManager");
            this.msSyncMgrExit.Text = LanguageTranslator.GetValue("ExitSyncManager");	

            this.toolStripMenuItem3.Text = LanguageTranslator.GetValue("CheckforUpdate");
            this.toolStripMenuItem4.Text = LanguageTranslator.GetValue("SyncProgress");
            this.toolStripMenuItem5.Text = LanguageTranslator.GetValue("WebsiteUrl");
            this.toolStripMenuItem7.Text = LanguageTranslator.GetValue("PauseSync");
           

            isFromSyncMgrVerification = false;

            if (!BasicInfo.LoadRegistryValues())
            {
                showLogin = true;
                niSystemTray.ContextMenuStrip = cmSystemTrayLogin;
            }
            else
            {
                if (!BasicInfo.IsCredentialsAvailable)
                {
                    showLogin = true;
                    niSystemTray.ContextMenuStrip = cmSystemTrayLogin;
                }
                else
                {
                    txtUserName.Text = BasicInfo.UserName;
                    txtUserName.Enabled = false;
   
                    txtPasswrod.Text = BasicInfo.Password;
                    
                    txtServerUrl.Text = BasicInfo.ServiceUrl;
                    txtServerUrl.Enabled = false;

                    showLogin = false;
                    SyncEvaluatingBalloonMessage();
                }
            }
        }

        private void bwLogin_DoWork(object sender, DoWorkEventArgs e)
        {
            int referenceCode = 0;
            loginDetails = mezeoFileCloud.Login(txtUserName.Text, txtPasswrod.Text,validateServiceUrl(txtServerUrl.Text), ref referenceCode);
            e.Result = referenceCode;
        }

        public int checkReferenceCode()
        {
                int referencecode = 0;
                loginDetails = mezeoFileCloud.Login(BasicInfo.UserName, BasicInfo.Password, validateServiceUrl(BasicInfo.ServiceUrl), ref referencecode);
                return referencecode; 
        }

        private void ShowSyncManagerOfffline()
        {
            BasicInfo.UserName = txtUserName.Text;
            BasicInfo.Password = txtPasswrod.Text;
            BasicInfo.ServiceUrl = txtServerUrl.Text;

            isLoginSuccess = false;

            if (showLogin)
            {
                this.Close();
            }

            niSystemTray.ContextMenuStrip = cmSystemTraySyncMgr;

            if (syncManager == null)
            {
                syncManager = new frmSyncManager(mezeoFileCloud, loginDetails, notificationManager);

                mezeoFileCloud.SetSynManager(ref syncManager);
                syncManager.CreateControl();
                syncManager.ParentForm = this;
            }

            if (checkReferenceCode() > 0)
            {
                //If we are login and talk with server do not do anything 
            }
            else
            {
                //If we are login and can not talk with server show app is offline
                 syncManager.DisableSyncManager();
                 syncManager.ShowSyncManagerOffline();
            }

            syncManager.SyncOfflineMessage();
        }

        public void VerifyCredentialsAgainFromSyncMgr()
        {
            this.UseWaitCursor = true;

            this.txtUserName.Enabled = false;
            this.txtPasswrod.Enabled = false;
            this.txtServerUrl.Enabled = false;
            this.btnLogin.Enabled = false;
            this.labelError.Text = "";

            int referenceCode = 0;
            loginDetails = mezeoFileCloud.Login(txtUserName.Text, txtPasswrod.Text, validateServiceUrl(txtServerUrl.Text), ref referenceCode);
            if (loginDetails == null && (referenceCode == 401 || referenceCode == 403))
            {
                this.UseWaitCursor = false;
                ShowLoginAgainFromSyncMgr();
                return;
            }

            string NQParentURI = mezeoFileCloud.NQParentUri(loginDetails.szManagementUri, ref referenceCode);
            if (NQParentURI.Trim().Length != 0 && (referenceCode == 401 || referenceCode == 403))
            {
                this.UseWaitCursor = false;
                ShowLoginAgainFromSyncMgr();
                return;
            }

            loginDetails.szNQParentUri = NQParentURI;

            isLoginSuccess = true;
            this.UseWaitCursor = false;
            BasicInfo.Password = txtPasswrod.Text;
            syncManager.LoginDetail = loginDetails;
            if (showLogin)
            {
                this.Close();
            }

            showLogin = false;
            isFromSyncMgrVerification = false;

            niSystemTray.ContextMenuStrip = cmSystemTraySyncMgr;

            if (isLoginSuccess)
            {
                LoginSuccessTask();
            }
        }


        private void LoginSuccessTask()
        {
              DbHandler dbHandler = new DbHandler();
              if (!bwCheckServerStatus.IsBusy)
                bwCheckServerStatus.RunWorkerAsync();
                //syncManager.EnableSyncManager();
              syncManager.SetCanNotTalkToServer(false);
                if (BasicInfo.IsInitialSync)
                {
                    syncManager.InitializeSync();
                }
                else
                {
                    syncManager.SetUpSync();
                    syncManager.SetUpSyncNowNotification();
                    if (dbHandler.GetInitialSyncEventCount() == 0)
                        syncManager.ProcessOfflineEvents();
                    else if (dbHandler.GetInitialSyncEventCount() > 0)
                        syncManager.SyncNow();
                }
        }

        public void ShowLoginAgainFromSyncMgr()
        {
            this.labelError.Text = LanguageTranslator.GetValue("LoginErrorText");
            this.txtUserName.Enabled = false;
            this.txtPasswrod.Enabled = true;
            this.txtServerUrl.Enabled = false;

            this.btnLogin.Enabled = true;

            niSystemTray.ContextMenuStrip = cmSystemTrayLogin;
           // SyncEvaluatingBalloonMessage();

            this.Show();

            showLogin = true;

            isFromSyncMgrVerification = true;
            isLoginSuccess = false;
        }

        private void bwLogin_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            this.UseWaitCursor = false;
            if (loginDetails == null)
            {
                if ((int)e.Result == 403 || (int)e.Result == 401)
                {
                    if (showLogin)
                    {
                        ShowLoginError();
                    }
                    else
                    {
                        this.labelError.Text = LanguageTranslator.GetValue("LoginErrorText");
                        this.txtUserName.Enabled = false;
                        this.txtPasswrod.Enabled = true;
                        this.txtServerUrl.Enabled = false;

                        this.btnLogin.Enabled = true;

                        showLogin = true;
                    }
                    this.Show();
                }
                else
                {
                    if (BasicInfo.IsCredentialsAvailable)
                        ShowSyncManagerOfffline();
                    else
                        ShowLoginError();
                }
                return;
            }
            else if (loginDetails.nAccountType == 0)
            {
                ShowGuestLoginError();
                return;
            }
            else if (loginDetails.szContainerContentsUri == "")
            {
                /* Fogbugzid: 2295 login - return message when account has no default storage container or no 
                containers at all */
                ShowNoRootContainerError();
                return;
            }
            else
            {
                BasicInfo.UserName = txtUserName.Text;
                BasicInfo.Password = txtPasswrod.Text;
                BasicInfo.ServiceUrl = txtServerUrl.Text;

                isLoginSuccess = true;
              //  SyncEvaluatingBalloonMessage();
            }
             
            if (showLogin)
            {
                this.Close();
            }
           // mezeoFileCloud.AppEventViewer(AboutBox.AssemblyTitle, LanguageTranslator.GetValue("LoginSuccess"), 3);
            niSystemTray.ContextMenuStrip = cmSystemTraySyncMgr;

            CheckAndCreateSyncDirectory();
            CheckAndCreateNotificationQueue();
            HandleUpdates();
            if (syncManager == null)
            {
                syncManager = new frmSyncManager(mezeoFileCloud, loginDetails, notificationManager);

                //Set Sync Manager for Progress bar
                mezeoFileCloud.SetSynManager(ref syncManager);
                syncManager.CreateControl();
                syncManager.ParentForm = this;
            }

            if (isLoginSuccess)
            {
                LoginSuccessTask();
            }
        }

        private void ShowLoginError()
        {
            this.labelError.Text = LanguageTranslator.GetValue("LoginErrorText");
            this.txtUserName.Enabled = true;
            this.txtPasswrod.Enabled = true;
            this.txtServerUrl.Enabled = true;

            this.btnLogin.Enabled = true;

            isLoginSuccess = false;
        }

        private void ShowGuestLoginError()
        {
            this.labelError.Text = LanguageTranslator.GetValue("LoginGuestAccMsgText");
            this.txtUserName.Enabled = true;
            this.txtPasswrod.Enabled = true;
            this.txtServerUrl.Enabled = true;

            this.btnLogin.Enabled = true;

            isLoginSuccess = false;
        }

        private void ShowNoRootContainerError()
        {
            this.labelError.Text = LanguageTranslator.GetValue("NoRootContainerMsgTxt");
            this.txtUserName.Enabled = true;
            this.txtPasswrod.Enabled = true;
            this.txtServerUrl.Enabled = true;

            this.btnLogin.Enabled = true;

            isLoginSuccess = false;
        }

        private void frmLogin_Load(object sender, EventArgs e)
        {
        }

        public void showSyncManager()
        {
            if (syncManager != null)
            {
                syncManager.Show();
                syncManager.Focus();
            }
        }

        private void msShowSyncMgr_Click(object sender, EventArgs e)
        {
            showSyncManager();
        }

        private void CheckAndCreateNotificationQueue()
        {
            int nStatusCode = 0;
            string NQParentURI = mezeoFileCloud.NQParentUri(loginDetails.szManagementUri, ref nStatusCode);

            if (NQParentURI.Trim().Length != 0)
            {
                loginDetails.szNQParentUri = NQParentURI;
                BasicInfo.NQParentURI = NQParentURI;

                NQLengthResult nqLengthRes = mezeoFileCloud.NQGetLength(BasicInfo.ServiceUrl + loginDetails.szNQParentUri, BasicInfo.GetQueueName(), ref nStatusCode);
                if (nStatusCode == 404)
                {
                    bool bRet = mezeoFileCloud.NQCreate(BasicInfo.ServiceUrl + NQParentURI, BasicInfo.GetQueueName(), NQParentURI, ref nStatusCode);
                }
                else
                {
                    int nNQLength = 0;
                    if(nqLengthRes.nEnd == -1 || nqLengthRes.nStart == -1)
                        nNQLength = 0;
                    else
                        nNQLength = (nqLengthRes.nEnd + 1) - nqLengthRes.nStart;
 
                    if (BasicInfo.IsInitialSync && nNQLength > 0)
                    {
                        bool bRet = mezeoFileCloud.NQDelete(BasicInfo.ServiceUrl + NQParentURI, BasicInfo.GetQueueName(), ref nStatusCode);
                        if(bRet && nStatusCode == 200)
                            bRet = mezeoFileCloud.NQCreate(BasicInfo.ServiceUrl + NQParentURI, BasicInfo.GetQueueName(), NQParentURI, ref nStatusCode);
                        //mezeoFileCloud.NQDeleteValue(BasicInfo.ServiceUrl + loginDetails.szNQParentUri, queueName, nNQLength, ref nStatusCode);
                    }
                }
            }
        }

        private void CheckAndCreateSyncDirectory()
        {
            DbHandler dbHandler = new DbHandler();
            bool isDbCreateNew = false;

            bool isDirectoryExists = false;
            if (BasicInfo.SyncDirPath.Trim().Length != 0)
                isDirectoryExists = System.IO.Directory.Exists(BasicInfo.SyncDirPath);
            string dirName = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\" + AboutBox.AssemblyTitle;

            //Modified code to fixed issue # 1417 Checking first is directory exists or not
            if (!isDirectoryExists)
            {
                if (showLogin == false && !isDirectoryExists)
                {
                    DialogResult checkDir;

                    string message = LanguageTranslator.GetValue("ExpectedLocation") + Environment.NewLine + BasicInfo.SyncDirPath + ". " + Environment.NewLine + Environment.NewLine + LanguageTranslator.GetValue("FolderMoved") + Environment.NewLine + Environment.NewLine + LanguageTranslator.GetValue("ClickNoExit") + Environment.NewLine + Environment.NewLine + LanguageTranslator.GetValue("ClickYesRestore");
                    string caption = LanguageTranslator.GetValue("CaptionFileMissingDialog");
                 
                    MessageBoxButtons buttons = MessageBoxButtons.YesNo;
                    MessageBoxDefaultButton defaultbutton = MessageBoxDefaultButton.Button2;

                    checkDir = MessageBox.Show(message, caption,
                       buttons, MessageBoxIcon.None, defaultbutton);

                    if (checkDir == DialogResult.Yes)
                    {
                        System.IO.Directory.CreateDirectory(dirName);
                        
                        //Adding Code to change folder icon
                        folderIcon(dirName);
                        
                        BasicInfo.IsInitialSync = true;
                        BasicInfo.SyncDirPath = dirName;
                        dbHandler.DeleteDb();
                        dbHandler.CreatedNewDatabase(); 
                        dbHandler.OpenConnection();
                    }
                    else
                    {
                        System.Environment.Exit(0);
                    }
                }
                else
                {
                    isDbCreateNew = dbHandler.CreatedNewDatabase(); 
                    if (!isDirectoryExists)
                    {
                        System.IO.Directory.CreateDirectory(dirName);

                        //Adding Code to change folder icon
                        folderIcon(dirName);
                     }
                    BasicInfo.IsInitialSync = true;
                    // Always set the BasicInfo.SyncDirPath value.
                    BasicInfo.SyncDirPath = dirName;
                }
            }
            else
            {
                //if directory exits checking whether we have new database or not 
                isDbCreateNew = dbHandler.CreatedNewDatabase();
                BasicInfo.AutoSync = true;
                BasicInfo.SyncDirPath = dirName;
            }

            System.IO.Directory.SetCurrentDirectory(BasicInfo.SyncDirPath);
        }

        # region FolderIconCode

        public void folderIcon(string dirName)
        {
            string IniPath = dirName + @"\desktop.ini";
            string iconfile = Application.StartupPath + @"\app.ico";
            CreateFolderIcon(iconfile, IniPath);
            SetIniFileAttributes(IniPath);
            SetFolderAttributes(dirName);
        }

        public void CreateFolderIcon(string iconFilePath, string IniPath)
        {
            CreateDesktopIniFile(iconFilePath, IniPath);
        }


        private void CreateDesktopIniFile(string iconFilePath, string IniPath)
        {

            int iconIndex = 0;
            IniWriter.WriteValue(".ShellClassInfo", "IconFile", iconFilePath, IniPath);
            IniWriter.WriteValue(".ShellClassInfo", "IconIndex", iconIndex.ToString(), IniPath);
        }


        //Function to change attributes for folder icon code 
        private bool SetIniFileAttributes(string IniPath)
        {

            if ((System.IO.File.GetAttributes(IniPath) & FileAttributes.Hidden) != FileAttributes.Hidden)
            {
                System.IO.File.SetAttributes(IniPath, System.IO.File.GetAttributes(IniPath) | FileAttributes.Hidden);
            }

            if ((System.IO.File.GetAttributes(IniPath) & FileAttributes.System) != FileAttributes.System)
            {
                System.IO.File.SetAttributes(IniPath, System.IO.File.GetAttributes(IniPath) | FileAttributes.System);
            }

            return true;

        }

        //Function to change attributes for folder icon code 
        private bool SetFolderAttributes(string dirName)
        {

            if ((System.IO.File.GetAttributes(dirName) & FileAttributes.System) != FileAttributes.System)
            {
                System.IO.File.SetAttributes(dirName, System.IO.File.GetAttributes(dirName) | FileAttributes.System);
            }

            return true;
        }

        #endregion 

        private void HandleUpdates()
        {
            // See what version ran last.
            string strCurVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string dirName = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\" + AboutBox.AssemblyTitle;

            // If this is a newer version, handle any update logic.
            if (0 != strCurVersion.CompareTo(BasicInfo.LastExecutedVersion))
            {
                // Since this is the first time we've run, add the sync folder to
                // the users favorites list.
                 AddFarvoritesLinkToFolder();
                 folderIcon(dirName);
            }

            // Save out the version that is running.
            BasicInfo.LastExecutedVersion = strCurVersion;
        }

        private static void AddFarvoritesLinkToFolder()
        {
            // Add the folder to the IE Favorites.
            // Call Environment.GetFolderPath() to get the full path to "Favourites".
            // Add on the folder name and .lnk file extension.
            string favorites = Environment.GetFolderPath(Environment.SpecialFolder.Favorites) + "\\" + AboutBox.AssemblyTitle + ".lnk";
            favorites = favorites.Replace("Favorites", "Links");
            FileInfo LinkFile = new FileInfo(favorites);

            if (LinkFile.Exists) return;
            
            try
            {
                // This creates a Folder Shortcut 
                IWshShell wsh = new WshShellClass();
                IWshShortcut shortcut = (IWshShortcut)wsh.CreateShortcut(LinkFile.FullName);
                shortcut.IconLocation = Application.StartupPath + @"\app.ico"; // Adding icon at startup location
                shortcut.TargetPath = BasicInfo.SyncDirPath;  // The directory the link points to.
                shortcut.Save();
            }
            catch (Exception e)
            {
                LogWrapper.LogMessage("frmLogin - AddFarvoritesLinkToFolder", "Caught exception: " + e.Message);
            }
        }

        private void txtPasswrod_TextChanged(object sender, EventArgs e)
        {
        }

        public LoginDetails loginFromSyncManager()
        {
            int referenceCode = 0;
            loginDetails = mezeoFileCloud.Login(BasicInfo.UserName, BasicInfo.Password, validateServiceUrl(BasicInfo.ServiceUrl), ref referenceCode);
            if (referenceCode == ResponseCode.LOGINFAILED1 || referenceCode == ResponseCode.LOGINFAILED2)
            {
                if (syncManager != null)
                {
                    syncManager.Hide();
                }
                ShowLoginAgainFromSyncMgr();

                return null;
            }
            else if (referenceCode != ResponseCode.LOGIN)
            {
                if (syncManager != null)
                {
                    //syncManager.DisableSyncManager();
                    //syncManager.ShowSyncManagerOffline();
                    //syncManager.CheckServerStatus();
                    return null;
                }
                return null;
            }
            else
            {
                CheckAndCreateSyncDirectory();
                CheckAndCreateNotificationQueue();
                HandleUpdates();
                return loginDetails;
            }
        }

        private void txtUserName_TextChanged(object sender, EventArgs e)
        {
        }

        private void txtServerUrl_TextChanged(object sender, EventArgs e)
        {
        }

        private void txtServerUrl_Leave(object sender, EventArgs e)
        {
            if (txtServerUrl.Text.Trim().Length == 0)
            {
                txtServerUrl.Text = global::Mezeo.Properties.Resources.BrServerUrlCueText;
            }
        }

        private void menuItem7_Click(object sender, EventArgs e)
        {
        }

        private void toolStripMenuItem2_Paint(object sender, PaintEventArgs e)
        {
            Rectangle rect=new Rectangle();
            rect.X=0;
            rect.Y=0;
            rect.Width=((ToolStripMenuItem)sender).Width;
            rect.Height=((ToolStripMenuItem)sender).Height;
        }

        private void toolStripMenuItem5_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start(BasicInfo.ServiceUrl);
        }

        private void toolStripMenuItem3_Click(object sender, EventArgs e)
        {
            if (BasicInfo.updateAvailable == false)
                syncManager.checkForAppUpdate(true);

                //if (BasicInfo.updateAvailable == true)
                //{
                //    string RSSFeed = BasicInfo.GetUpdateURL();

                //    if (RSSFeed.Length != 0)
                //    {
                //        _sparkle = new Sparkle(RSSFeed);
                //        _sparkle.StartLoop(true);
                //    }
                //}
        }

        private void toolStripMenuItem7_Click(object sender, EventArgs e)
        {
            if (!syncManager.IsSyncPaused())
            {
                syncPausedOperation();
                syncManager.resetAllControls();
                return;
            }
            if (syncManager.IsSyncPaused())
            {
                syncResumeOperation();
                syncManager.resetAllControls();
                return;
            }
        }
        
        //Adding Function for click on context menu Pause button click
        public void syncResumeOperation()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    syncManager.SetSyncPaused(false);
                    changeResumeText();
                    syncManager.changePauseResumeBtnText();
                    syncManager.SetUpSync();
                    syncManager.SyncNow();
                    syncManager.SyncResumeBalloonMessage();
                });
            }
            else
            {
                syncManager.SetSyncPaused(false);
                changeResumeText();
                syncManager.changePauseResumeBtnText();
                syncManager.SetUpSync();
                syncManager.SyncNow();
                syncManager.SyncResumeBalloonMessage();
            }
        }

        //Adding Function for click on context menu Resume button click
        public void syncPausedOperation()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    syncManager.SetSyncPaused(true);
                    syncManager.StopSync();
                    changePauseText();
                    syncManager.ChangeUIOnPause();
                    syncManager.SyncPauseBalloonMessage();
                });
            }
            else
            {
                syncManager.SetSyncPaused(true);
                syncManager.StopSync();
                changePauseText();
                syncManager.ChangeUIOnPause();
                syncManager.SyncPauseBalloonMessage();
            }
        }

        private void SyncEvaluatingBalloonMessage()
        {
            notificationManager.NotificationHandler.Icon = Properties.Resources.mezeosyncstatus_syncing;
            notificationManager.NotificationHandler.ShowBalloonTip(1, LanguageTranslator.GetValue("SyncStartMessage"),
                                                                           LanguageTranslator.GetValue("EvaluatingLocalChanges"),
                                                                          ToolTipIcon.None);
            notificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion + "\n" + LanguageTranslator.GetValue("EvaluatingLocalChanges");
        }

        public void changeUpdatesText(string newVersion)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    toolStripMenuItem3.Text = LanguageTranslator.GetValue("InstallUpdateText") + newVersion;
                });
            }
            else
            {
                toolStripMenuItem3.Text = LanguageTranslator.GetValue("InstallUpdateText") + newVersion;
            }
        }

        public void changePauseText()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    toolStripMenuItem7.Text = LanguageTranslator.GetValue("ResumeSyncText");
                });
            }
            else
            {
                toolStripMenuItem7.Text = LanguageTranslator.GetValue("ResumeSyncText");
            }
        }


        public void DisableContextMenuText()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    toolStripMenuItem7.Enabled = false;
                });
            }
            else
            {
                toolStripMenuItem7.Enabled = false;
            }
        }

        public void EnableContextMenuText()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    toolStripMenuItem7.Enabled = true;
                });
            }
            else
            {
                toolStripMenuItem7.Enabled = true;
            }
        }


        public void changeResumeText()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    toolStripMenuItem7.Text = LanguageTranslator.GetValue("PauseSync");
                });
            }
            else
            {
                toolStripMenuItem7.Text = LanguageTranslator.GetValue("PauseSync");
            }
        }

        private void toolStripMenuItem6_Paint(object sender, PaintEventArgs e)
        {
            Rectangle rect = new Rectangle();
            rect.X = 0;
            rect.Y = 0;
            rect.Width = ((ToolStripMenuItem)sender).Width;
            rect.Height = ((ToolStripMenuItem)sender).Height;
        }

        private void loginToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Show();
                this.WindowState = FormWindowState.Normal;
            }
        }
    }
}
