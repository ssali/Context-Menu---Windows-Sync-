﻿using System;
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

            niSystemTray.ContextMenuStrip = cmSystemTrayLogin;

            mezeoFileCloud = new CloudService();

            LoadResources();

            string RSSFeed = BasicInfo.GetUpdateURL();

            if (RSSFeed.Length != 0)
            {
                _sparkle = new Sparkle(RSSFeed);
                _sparkle.StartLoop(true);
            }

            EventQueue.InitEventQueue();
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
          
            if (syncManager != null && (syncManager.isSyncInProgress || syncManager.isLocalEventInProgress || syncManager.isOfflineWorking))
            {
                DialogResult dResult = MessageBox.Show(LanguageTranslator.GetValue("MezeoExitString1") + "\n" + LanguageTranslator.GetValue("BrMezeoExitString2"), AboutBox.AssemblyTitle, MessageBoxButtons.OKCancel);
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
            TimeSpan time = new TimeSpan(0,5,0);
            while(!_Appexit.WaitOne(time))
                syncManager.CheckServerStatus();
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
            LanguageTranslator.SetLanguage("en");
            this.Text =   AboutBox.AssemblyTitle + " " + LanguageTranslator.GetValue("LoginFormTitle");
            this.txtUserName.CueText = LanguageTranslator.GetValue("UserIdCueText");
            this.txtPasswrod.CueText=LanguageTranslator.GetValue("PasswordCueText");
            this.txtServerUrl.CueText = LanguageTranslator.GetValue("BrServerUrlCueText");
            this.txtServerUrl.Text = LanguageTranslator.GetValue("BrServerUrlCueText");
            this.labelError.Text = "";

            isFromSyncMgrVerification = false;

            if (!BasicInfo.LoadRegistryValues())
            {
                showLogin = true;
            }
            else
            {
                if (!BasicInfo.IsCredentialsAvailable)
                {
                    showLogin = true;
                }
                else
                {
                    txtUserName.Text = BasicInfo.UserName;
                    txtUserName.Enabled = false;
   
                    txtPasswrod.Text = BasicInfo.Password;
                    
                    txtServerUrl.Text = BasicInfo.ServiceUrl;
                    txtServerUrl.Enabled = false;

                    showLogin = false;
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

            mezeoFileCloud.AppEventViewer(AboutBox.AssemblyTitle, LanguageTranslator.GetValue("TrayAppOfflineText"), 3);
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
                //syncManager.CheckServerStatus();
            }
            else
            {
                 syncManager.DisableSyncManager();
                 syncManager.ShowOfflineAtStartUpSyncManager();
                 syncManager.ShowSyncManagerOffline();
                 syncManager.SetIsSyncInProgress(false);
            }
            notificationManager.NotificationHandler.ShowBalloonTip(1, LanguageTranslator.GetValue("TrayBalloonSyncStatusText"),
                                                                                LanguageTranslator.GetValue("TrayAppOfflineText"), ToolTipIcon.None);

            notificationManager.HoverText = LanguageTranslator.GetValue("TrayAppOfflineText");
            notificationManager.NotifyIcon = Properties.Resources.app_offline;
            toolStripMenuItem4.Text = LanguageTranslator.GetValue("AppOfflineMenu");
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
                bwCheckServerStatus.RunWorkerAsync();
                //syncManager.EnableSyncManager();
                if (BasicInfo.IsInitialSync)
                {
                    syncManager.InitializeSync();
                }
                else
                {
                    syncManager.SetUpSync();
                    syncManager.SetUpSyncNowNotification();
                    syncManager.ProcessOfflineEvents();
                }
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
            else
            {
                BasicInfo.UserName = txtUserName.Text;
                BasicInfo.Password = txtPasswrod.Text;
                BasicInfo.ServiceUrl = txtServerUrl.Text;

                isLoginSuccess = true;
            }
             
            if (showLogin)
            {
                this.Close();
            }
            mezeoFileCloud.AppEventViewer(AboutBox.AssemblyTitle, LanguageTranslator.GetValue("LoginSuccess"), 3);
            niSystemTray.ContextMenuStrip = cmSystemTraySyncMgr;

            CheckAndCreateSyncDirectory();
            CheckAndCreateNotificationQueue();
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
                bwCheckServerStatus.RunWorkerAsync();
                if (BasicInfo.IsInitialSync)
                {
                    syncManager.InitializeSync();
                }
                else
                {
                    syncManager.SetUpSync();
                    syncManager.SetUpSyncNowNotification();
                    syncManager.ProcessOfflineEvents();
                }
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

                    string message = LanguageTranslator.GetValue("BrExpectedLocation") + Environment.NewLine + BasicInfo.SyncDirPath + ". " + Environment.NewLine + Environment.NewLine + LanguageTranslator.GetValue("FolderMoved") + Environment.NewLine + Environment.NewLine + LanguageTranslator.GetValue("ClickNoExit") + Environment.NewLine + Environment.NewLine + LanguageTranslator.GetValue("ClickYesRestore");
                    //string caption = "MezeoFile Setup";
                    string caption = LanguageTranslator.GetValue("BrCaptionFileMissingDialog");
                    MessageBoxButtons buttons = MessageBoxButtons.YesNo;
                    MessageBoxDefaultButton defaultbutton = MessageBoxDefaultButton.Button2;

                    checkDir = MessageBox.Show(message, caption,
                       buttons, MessageBoxIcon.None, defaultbutton);

                    if (checkDir == DialogResult.Yes)
                    {
                        System.IO.Directory.CreateDirectory(dirName);
                        BasicInfo.IsInitialSync = true;
                        BasicInfo.SyncDirPath = dirName;
                        dbHandler.DeleteDb();
                        dbHandler.OpenConnection();
                    }
                    else
                    {
                        System.Environment.Exit(0);
                    }
                }
                else
                {
                    isDbCreateNew = dbHandler.OpenConnection();
                    if (!isDirectoryExists)
                        System.IO.Directory.CreateDirectory(dirName);

                    BasicInfo.IsInitialSync = true;
                    // Always set the BasicInfo.SyncDirPath value.
                    BasicInfo.SyncDirPath = dirName;
                }
            }
            else
            {
                //if directory exits checking whether we have new database or not 
                isDbCreateNew = dbHandler.OpenConnection();
                BasicInfo.AutoSync = true;
                BasicInfo.SyncDirPath = dirName;
            }

            System.IO.Directory.SetCurrentDirectory(BasicInfo.SyncDirPath);

            if (true == isDbCreateNew)
            {
                // Since this is the first time we've run, add the sync folder to
                // the users favorites list.
                AddFarvoritesLinkToFolder();
            }
        }

        private static void AddFarvoritesLinkToFolder()
        {
            // Add the folder to the IE Favorites.
            // Call Environment.GetFolderPath() to get the full path to "Favourites".
            // Add on the folder name and .lnk file extension.
            string favorites = Environment.GetFolderPath(Environment.SpecialFolder.Favorites) + "\\" + AboutBox.AssemblyTitle + ".lnk";
            favorites = favorites.Replace("Favorites", "Links");

            // This creates a Folder Shortcut 
            IWshShell wsh = new WshShellClass();
            IWshShortcut shortcut = (IWshShortcut)wsh.CreateShortcut(favorites);
            shortcut.TargetPath = BasicInfo.SyncDirPath;  // The directory the link points to.
            shortcut.Save();

            //// Add the folder to the 'Save As' dialog Favorites.
            //string Key_Policies = @"Software\Microsoft\Windows\CurrentVersion\Policies";
            //string Key_PlacesBar = @"Software\Microsoft\Windows\CurrentVersion\Policies\ComDlg32\PlacesBar";
            //Microsoft.Win32.RegistryKey reg = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key_PlacesBar, Microsoft.Win32.RegistryKeyPermissionCheck.ReadWriteSubTree);


            //int index = 0;
            //// Find the first key that doesn't exist.
            //while (index > -1)
            //{
            //    string key = "Place" + index.ToString();
            //    try
            //    {
            //        reg.GetValue(key);
            //    }
            //    catch (Exception e)
            //    {
            //        //LogWrapper.LogMessage("frmLogin - AddFarvoritesLinkToFolder", "Add sync folder to favorite index: " + e.Message);
            //        LogWrapper.LogMessage("frmLogin - AddFarvoritesLinkToFolder", "Add sync folder to favorite index: " + index.ToString());
            //        reg.SetValue(key, BasicInfo.SyncDirPath);
            //    }
            //}
            //reg.Close();
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
                txtServerUrl.Text = LanguageTranslator.GetValue("BrServerUrlCueText"); ;
            }
        }

        private void menuItem7_Click(object sender, EventArgs e)
        {
        }

        private void toolStripMenuItem2_Paint(object sender, PaintEventArgs e)
        {
            //Point pt = new Point(0, 0);
            Rectangle rect=new Rectangle();
            rect.X=0;
            rect.Y=0;
            rect.Width=((ToolStripMenuItem)sender).Width;
            rect.Height=((ToolStripMenuItem)sender).Height;

            Image img = Properties.Resources.mezeo_menu_logo_large;
            //e.Graphics.DrawImage(img, pt);
            GraphicsUnit gu = new GraphicsUnit();
            e.Graphics.DrawImage(img, rect, img.GetBounds(ref gu), gu);
            //e.Graphics.DrawImageUnscaled(img, rect);
        }

        private void toolStripMenuItem5_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start(BasicInfo.ServiceUrl);
        }

        private void toolStripMenuItem6_Paint(object sender, PaintEventArgs e)
        {
            //Point pt = new Point(0, 0);
            Rectangle rect = new Rectangle();
            rect.X = 0;
            rect.Y = 0;
            rect.Width = ((ToolStripMenuItem)sender).Width;
            rect.Height = ((ToolStripMenuItem)sender).Height;

            Image img = Properties.Resources.meze_menu_logo_small;
            //e.Graphics.DrawImage(img, pt);
            GraphicsUnit gu = new GraphicsUnit();
            e.Graphics.DrawImage(img, rect, img.GetBounds(ref gu), gu);
            //e.Graphics.DrawImageUnscaled(img, rect);
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
