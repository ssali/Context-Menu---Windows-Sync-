using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using MezeoFileSupport;
using System.Threading;
using System.Runtime.InteropServices;
using System.Net.NetworkInformation;
using System.Net;
using System.Xml;
using System.Reflection;
using Mezeo;
using System.Web;

namespace Mezeo
{
    public partial class frmSyncManager : Form
    {
       
        # region Private Static Variables

        private static int SYNC_STARTED = 1;
        private static int PROCESS_LOCAL_EVENTS_STARTED = SYNC_STARTED + 1;
        private static int PROGRESS_CHANGED_WITH_FILE_NAME = PROCESS_LOCAL_EVENTS_STARTED + 1;
        private static int LOCAL_EVENTS_COMPLETED = PROGRESS_CHANGED_WITH_FILE_NAME + 1;
        private static int LOCAL_EVENTS_STOPPED = LOCAL_EVENTS_COMPLETED + 1;

        private static int ONE_SECOND = 1000;
        private static int ONE_MINUTE = ONE_SECOND * 60;
        private static int FIVE_SECONDS = ONE_SECOND * 5;

        private static int INITIAL_NQ_SYNC = LOCAL_EVENTS_COMPLETED + 1;
        private static int UPDATE_NQ_PROGRESS = INITIAL_NQ_SYNC + 1;
        private static int UPDATE_NQ_CANCELED = UPDATE_NQ_PROGRESS + 1;
        private static int UPDATE_NQ_MAXIMUM = UPDATE_NQ_CANCELED + 1;

        private static int USER_CANCELLED = -3;
        private static int LOGIN_FAILED = -4;
        private static int ITEM_NOT_FOUND = -5;
        private static int SERVER_INACCESSIBLE = 6;

        private static string DB_STATUS_SUCCESS = "SUCCESS";
        private static string DB_STATUS_IN_PROGRESS = "INPROGRESS";

        #endregion

        #region Delegates for callback

        public MezeoFileSupport.CallbackIncrementProgress myDelegate;

        public MezeoFileSupport.CallbackContinueRunning ContinueRunningDelegate;

        #endregion

        #region Private Members and objects

        private CloudService cMezeoFileCloud;
        private LoginDetails cLoginDetails;
        private frmLogin frmParent;
        private string[] statusMessages = new string[3];
        private int statusMessageCounter = 0;
        private bool analysisIsComplete = true;
        public bool isSyncPause = false; // Check sync is pause or not
        public bool isSyncGenerateLocalEvents = false;
        private bool isDownloadingFile = false;
        private StructureDownloader stDownloader;
        private DateTime lastSync;
        private OfflineWatcher offlineWatcher;
        private bool canNotTalktoTheServer = false;
        private int messageMax;
        private int messageValue;
        private int SynctmrDelay = -1;
        private int pbScale = 1;

        Queue<LocalItemDetails> queue;
        frmIssues frmIssuesFound;
        Watcher watcher;
        NotificationManager cnotificationManager;

        Thread analyseThread;
        ThreadLockObject lockObject;

        DbHandler dbHandler;

        public frmLogin ParentForm
        {
            get
            {
                return frmParent;
            }
            set
            {
                frmParent = value;
            }
        }

        #endregion


        #region Constructors and Properties
        
        public frmSyncManager()
        {
            InitializeComponent();
            LoadResources();
        }
        public frmSyncManager(CloudService mezeoFileCloud, LoginDetails loginDetails, NotificationManager notificationManager)
        {
            InitializeComponent();
           
            cMezeoFileCloud = mezeoFileCloud;

            cMezeoFileCloud.fileCloud.downloadStoppedEvent += new MezeoFileCloud.FileDownloadStoppedEvent(cMezeoFileCloud_downloadStoppedEvent);

            cMezeoFileCloud.fileCloud.uploadStoppedEvent += new MezeoFileCloud.FileUploadStoppedEvent(cMezeoFileCloud_uploadStoppedEvent);

            cLoginDetails = loginDetails;
            cnotificationManager = notificationManager;

            LoadResources();

            watcher = new Watcher(lockObject, BasicInfo.SyncDirPath);
            EventQueue.WatchCompletedEvent += new EventQueue.WatchCompleted(queue_WatchCompletedEvent);
            CheckForIllegalCrossThreadCalls = false;

            watcher.StartMonitor();
            dbHandler = new DbHandler();
            dbHandler.OpenConnection();

            frmIssuesFound = new frmIssues(mezeoFileCloud, this);
            offlineWatcher = new OfflineWatcher(dbHandler);
            
            myDelegate = new MezeoFileSupport.CallbackIncrementProgress(this.CallbackSyncProgress);
            ContinueRunningDelegate = new MezeoFileSupport.CallbackContinueRunning(this.CallbackContinueRun);
        }

        public int getSynNextCycleTimer()
        {
            if (SynctmrDelay == -1)
            {
                int syncTime = Convert.ToInt32(global::Mezeo.Properties.Resources.BrSyncTimer);
                SynctmrDelay = ONE_MINUTE * syncTime;
            }

            return SynctmrDelay;
        }

        void cMezeoFileCloud_uploadStoppedEvent(string szSourceFileName, string szContantURI)
        {
            // See the comment in cMezeoFileCloud_downloadStoppedEvent.
            // Related to defects 2314 and 2322.
            // Deleting an incomplete/partial file (either in the cloud or locally) generates a delete event.
            // Whether the delete event is local or server side, it will eventually end up causing all
            // versions of the file to be deleted from the system.  The upload delete starts the process
            // via a notification queue.  The app gets the nq event and then deletes the local file.
            // For a download, the event starts locally via the local delete event.  If a priority queue exists,
            // then resuming the download causes the local events to be processed first resulting in the create,
            // modified (1 or more) and then delete action after which the download gets a 404.  Without the priority,
            // the download finished, then the create, modified, and delete events are processed ending with the delete.
            //DeleteUploadedIncompleteFile(szContantURI);
        }

        void DeleteUploadedIncompleteFile(string szContantURI)
        {
            // See the comment in cMezeoFileCloud_uploadStoppedEvent.
            if (szContantURI.Trim().Length != 0)
            {
                int nStatusCode = 0;
                bool bRet = cMezeoFileCloud.Delete(szContantURI, ref nStatusCode, "");
            }
        }

        void cMezeoFileCloud_downloadStoppedEvent(string fileName)
        {
            // See the comment in cMezeoFileCloud_uploadStoppedEvent.
            DeleteCurrentIncompleteFile(fileName);
        }

        public LoginDetails LoginDetail
        {
            get
            {
                return cLoginDetails;
            }
            set
            {
                cLoginDetails = value;
            }
        }

        #endregion


        #region Form Drawing Events
        
        private void panel1_Paint(object sender, PaintEventArgs e)
        {
        }
        
        private void DrawRoundedRectangle(Graphics gfx, Rectangle Bounds, int CornerRadius, Pen DrawPen, Color FillColor)
        {
            int strokeOffset = Convert.ToInt32(Math.Ceiling(DrawPen.Width));
            Bounds = Rectangle.Inflate(Bounds, -strokeOffset, -strokeOffset);

            DrawPen.EndCap = DrawPen.StartCap = LineCap.Round;

            GraphicsPath gfxPath = new GraphicsPath();
            gfxPath.AddArc(Bounds.X, Bounds.Y, CornerRadius, CornerRadius, 180, 90);
            gfxPath.AddArc(Bounds.X + Bounds.Width - CornerRadius, Bounds.Y, CornerRadius, CornerRadius, 270, 90);
            gfxPath.AddArc(Bounds.X + Bounds.Width - CornerRadius, Bounds.Y + Bounds.Height - CornerRadius, CornerRadius, CornerRadius, 0, 90);
            gfxPath.AddArc(Bounds.X, Bounds.Y + Bounds.Height - CornerRadius, CornerRadius, CornerRadius, 90, 90);
            gfxPath.CloseAllFigures();

            gfx.FillPath(new SolidBrush(FillColor), gfxPath);
            gfx.DrawPath(DrawPen, gfxPath);
        }

        private void panel_Paint(object sender, PaintEventArgs e)
        {
            try
            {
                Panel pnl = sender as Panel;
                Rectangle bounds = new Rectangle(0, 0, pnl.Width, pnl.Height);
                Pen pen = new Pen(Color.FromArgb(206, 207, 188));

                DrawRoundedRectangle(e.Graphics, bounds, 10, pen, Color.Transparent);
            }
            catch (Exception ex)
            {
                //do nothing
                LogWrapper.LogMessage("frmSyncManager - panel_Paint", "Caught exception: " + ex.Message);
            }
        }

        #endregion


        #region Flag Events

        public bool IsAnalysisCompleted()
        {
            return analysisIsComplete;
        }

        public void SetAnalysisIsCompleted(bool analysis)
        {
            analysisIsComplete = analysis;
        }

        /*This will check sync is in progress or not 
        (It will tell offlineEvents or localevents or notification queue is in progress) 
        Sync Thread is in progress
        */
        public bool IsSyncThreadInProgress()
        {
            return bwSyncThread.IsBusy;
        }

        // It will tell Sync Manager is in pause state or not 
        public bool IsSyncPaused()
        {
            return isSyncPause;
        }

        public void SetSyncPaused(bool syncisPause)
        {
            isSyncPause = syncisPause;
        }

        public bool CanNotTalkToServer()
        {
            return canNotTalktoTheServer;
        }

        public void SetCanNotTalkToServer(bool talkToTheServer)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    if ((canNotTalktoTheServer == true) && (talkToTheServer == false))
                    {
                        // Moving from offline to online.
                        canNotTalktoTheServer = talkToTheServer;
                        SyncOnlineMessage();
                        resetAllControls();
                    }
                    else if ((canNotTalktoTheServer == false) && (talkToTheServer == true))
                    {
                        // Moving from online to offline.
                        canNotTalktoTheServer = talkToTheServer;
                        SyncOfflineMessage();
                        resetAllControls();
                    }
                });
            }
            else
            {
                if ((canNotTalktoTheServer == true) && (talkToTheServer == false))
                {
                    // Moving from offline to online.
                    canNotTalktoTheServer = talkToTheServer;
                    SyncOnlineMessage();
                    resetAllControls();
                }
                else if ((canNotTalktoTheServer == false) && (talkToTheServer == true))
                {
                    // Moving from online to offline.
                    canNotTalktoTheServer = talkToTheServer;
                    SyncOfflineMessage();
                    resetAllControls();
                }
            }
        }

        public bool IsSyncGenerateLocalEvent()
        {
            return isSyncGenerateLocalEvents;
        }

        public void SetSyncGenerateLocalEvent(bool talkToTheServer)
        {
            isSyncGenerateLocalEvents = talkToTheServer;
        }

        public bool IsInIdleState()
        {
            if (!IsSyncThreadInProgress() && !IsSyncPaused() && IsAnalysisCompleted())
                return true;
            return false;
        }
      

        #endregion


        #region Downloader Events

        void fileDownloder_fileDownloadCompleted()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    BasicInfo.IsInitialSync = false;
                    ShowSyncMessage();
                    InitialSyncUptodateMessage();
                    queue_WatchCompletedEvent();
                });
            }
            else
            {
                BasicInfo.IsInitialSync = false;
                InitialSyncUptodateMessage();
                queue_WatchCompletedEvent();
            }
        }

        void ShowNextSyncLabel(bool bIsShow)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    label1.Visible = bIsShow;
                    label1.BringToFront();
                });
            }
            else
            {
                label1.Visible = bIsShow;
                label1.BringToFront();
            }
        }

        void stDownloader_downloadEvent(object sender, StructureDownloaderEvent e)
        {
            if (e.IsCompleted && !lockObject.StopThread)
            {
                if (this.InvokeRequired)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        tmrSwapStatusMessage.Enabled = false;
                        BasicInfo.IsInitialSync = false;
                        SetAnalysisIsCompleted(true);
                        resetAllControls();
                        SetUpSync();
                        SyncNow();
                    });
                }
                else
                {
                    tmrSwapStatusMessage.Enabled = false;
                    BasicInfo.IsInitialSync = false;
                    SetAnalysisIsCompleted(true);
                    resetAllControls();
                    SetUpSync();
                    SyncNow();
                }
            }
           
        }

        void fileDownloder_cancelDownloadEvent(CancelReason reason)
        {
            isDownloadingFile = false;

            OnThreadCancel(reason);

            if (reason == CancelReason.INSUFFICIENT_STORAGE)
            {
                ShowInsufficientStorageMessage();
            }
        }

        void stDownloader_cancelDownloadEvent(CancelReason reason)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    tmrSwapStatusMessage.Enabled = false;
                });
            }
            else
            {
                tmrSwapStatusMessage.Enabled = false;
            }
            //isAnalysingStructure = false;
            OnThreadCancel(reason);
        }

        public void ApplicationExit()
        {
            if (lockObject != null)
                lockObject.ExitApplication = true;
            StopSync();
        }

        #endregion


        #region Update GUI Functons

        public void resetAllControls()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                     if (IsInIdleState() && !CanNotTalkToServer())
                     {
                        this.Text = AboutBox.AssemblyTitle;
                        this.lblFolder.Text = LanguageTranslator.GetValue("SyncManagerFolderLabel");
                        this.lblStatus.Text = LanguageTranslator.GetValue("SyncManagerStatusLabel");
                        this.lblUsage.Text = LanguageTranslator.GetValue("SyncManagerUsageLabel");

                        this.btnSyncNow.Text = LanguageTranslator.GetValue("SyncManagerSyncNowButtonText");
                        this.btnSyncNow.Enabled = true;
                        
                        this.pbSyncProgress.Visible = false;
                        this.lblPercentDone.Visible = false;

                        if (frmIssuesFound != null && frmIssuesFound.GetItemsInList() > 0)
                            btnIssuesFound.Visible = true;
                        else
                            this.btnIssuesFound.Visible = false;
                        

                        this.lblUserName.Text = BasicInfo.UserName;
                        this.lnkServerUrl.Text = BasicInfo.ServiceUrl;
                        this.lnkFolderPath.Text = BasicInfo.SyncDirPath;

                        lastSync = DateTime.Now;
                        BasicInfo.LastSyncAt = lastSync;

                        lblStatusL1.Text = LanguageTranslator.GetValue("SyncManagerStatusAllFilesInSyncLabel");

                        lblStatusL3.Text = LanguageTranslator.GetValue("SyncManagerStatusLastSyncLabel") + " " + lastSync.ToString("MMM d, yyyy h:mm tt");
                        label1.Text = LanguageTranslator.GetValue("SyncManagerStatusNextSyncAtLabel") + " " + lastSync.AddMinutes(Convert.ToInt32(global::Mezeo.Properties.Resources.BrSyncTimer)).ToString("h:mm tt");
                        label1.BringToFront();
                        label1.Visible = true;
                        label1.Show();

                        

                        InitialSyncBalloonMessage();
             
                        UpdateUsageLabel();
                     }
                     else if (CanNotTalkToServer())
                         ShowSyncManagerOffline();
                     else if(!IsInIdleState())
                     {
                         btnSyncNow.Enabled = false;
                     }

                     if (IsSyncPaused())
                     {
                         btnPauseResume.Enabled = true;
                         frmParent.EnableContextMenuText();
                     }

                     CheckIcon();
                });
            }
            else
            {
                if (IsInIdleState() && !CanNotTalkToServer())
                {
                    this.Text = AboutBox.AssemblyTitle;
                    this.lblFolder.Text = LanguageTranslator.GetValue("SyncManagerFolderLabel");
                    this.lblStatus.Text = LanguageTranslator.GetValue("SyncManagerStatusLabel");
                    this.lblUsage.Text = LanguageTranslator.GetValue("SyncManagerUsageLabel");

                    this.btnSyncNow.Text = LanguageTranslator.GetValue("SyncManagerSyncNowButtonText");
                    this.btnSyncNow.Enabled = true;

                    this.pbSyncProgress.Visible = false;
                    this.lblPercentDone.Visible = false;

                    if (frmIssuesFound != null && frmIssuesFound.GetItemsInList() > 0)
                        btnIssuesFound.Visible = true;
                    else
                        this.btnIssuesFound.Visible = false;

                    this.lblUserName.Text = BasicInfo.UserName;
                    this.lnkServerUrl.Text = BasicInfo.ServiceUrl;
                    this.lnkFolderPath.Text = BasicInfo.SyncDirPath;

                    lastSync = DateTime.Now;
                    BasicInfo.LastSyncAt = lastSync;

                    lblStatusL1.Text = LanguageTranslator.GetValue("SyncManagerStatusAllFilesInSyncLabel");

                    lblStatusL3.Text = LanguageTranslator.GetValue("SyncManagerStatusLastSyncLabel") + " " + lastSync.ToString("MMM d, yyyy h:mm tt");
                    label1.Text = LanguageTranslator.GetValue("SyncManagerStatusNextSyncAtLabel") + " " + lastSync.AddMinutes(Convert.ToInt32(global::Mezeo.Properties.Resources.BrSyncTimer)).ToString("h:mm tt");
                    label1.BringToFront();
                    label1.Visible = true;
                    label1.Show();

                    InitialSyncBalloonMessage();
                  
                    UpdateUsageLabel();
                }
                else if (CanNotTalkToServer())
                    ShowSyncManagerOffline();
                else if (!IsInIdleState())
                {
                    btnSyncNow.Enabled = false;
                }
                
                if (IsSyncPaused())
                {
                    btnPauseResume.Enabled = true;
                    frmParent.EnableContextMenuText();
                }
                
                CheckIcon();
            }
        }

        private void CheckIcon()
        {
            if (bwSyncThread.IsBusy)
            {
                cnotificationManager.NotificationHandler.Icon = Properties.Resources.mezeosyncstatus_syncing;
            }
            else if (IsSyncPaused())
            {
                cnotificationManager.NotificationHandler.Icon = Properties.Resources.app_icon_disabled;               
            }
            else if (CanNotTalkToServer())
            {
                cnotificationManager.NotifyIcon = Properties.Resources.app_offline;
            }
            else if (frmIssuesFound != null && frmIssuesFound.GetItemsInList() > 0)
            {
                cnotificationManager.NotificationHandler.Icon = Properties.Resources.app_icon_warning;
                cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion + "\n" + LanguageTranslator.GetValue("SyncConflictFoundText");
                frmParent.toolStripMenuItem4.Text = LanguageTranslator.GetValue("SyncManagerMenuIssueFoundText");
            }
            //else if (BasicInfo.UpdateAvailable)
            //{
            //    cnotificationManager.NotificationHandler.Icon = Properties.Resources.app_icon_upgrade;
            //}
            else
            {
                cnotificationManager.NotificationHandler.Icon = Properties.Resources.MezeoVault;
            }       
        }

        private void UpdateUsageLabel()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    if (cLoginDetails != null)
                    {
                        if (!bwUpdateUsage.IsBusy)
                            bwUpdateUsage.RunWorkerAsync();
                    }
                    else
                    {
                        this.lblUsageDetails.Text = LanguageTranslator.GetValue("UsageNotAvailable");
                    }
                });
            }
            else
            {
                if (cLoginDetails != null)
                {
                    if (!bwUpdateUsage.IsBusy)
                        bwUpdateUsage.RunWorkerAsync();
                }
                else
                {
                    this.lblUsageDetails.Text = LanguageTranslator.GetValue("UsageNotAvailable");
                }
            }
        }

        public void SetUpSync()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    lblPercentDone.Visible = true;
                    lblPercentDone.Text = "";
                    if (frmIssuesFound != null && frmIssuesFound.GetItemsInList() > 0)
                        btnIssuesFound.Visible = true;
                    //this.btnSyncNow.Text = LanguageTranslator.GetValue("PauseSync");
                    this.btnSyncNow.Enabled = false;
                    btnSyncNow.Refresh();
                });
            }
            else
            {
                lblPercentDone.Visible = true;
                lblPercentDone.Text = "";
                if (frmIssuesFound != null && frmIssuesFound.GetItemsInList() > 0)
                    btnIssuesFound.Visible = true;
                // this.btnSyncNow.Text = LanguageTranslator.GetValue("PauseSync");
                this.btnSyncNow.Enabled = false;
                btnSyncNow.Refresh();
            }
        }

        private void SetUpControlForSync()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    SetIssueFound(false);
                    //this.btnSyncNow.Text = LanguageTranslator.GetValue("PauseSync");
                    this.btnSyncNow.Enabled = false;
                    btnSyncNow.Refresh();
                    isDownloadingFile = true;
                    ShowNextSyncLabel(false);
                });
            }
            else
            {
                SetIssueFound(false);
               // this.btnSyncNow.Text = LanguageTranslator.GetValue("PauseSync");
                this.btnSyncNow.Enabled = false;
                btnSyncNow.Refresh();
                isDownloadingFile = true;
                ShowNextSyncLabel(false);
            }
        }

        private void tmrSwapStatusMessage_Tick(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    statusMessageCounter++;

                    if (statusMessageCounter >= statusMessages.Length)
                    {
                        statusMessageCounter = 0;
                    }

                    lblStatusL1.Text = statusMessages[statusMessageCounter];
                });
            }
            else
            {
                statusMessageCounter++;

                if (statusMessageCounter >= statusMessages.Length)
                {
                    statusMessageCounter = 0;
                }

                lblStatusL1.Text = statusMessages[statusMessageCounter];
            }
        }

        private void showProgress()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    //LogWrapper.LogMessage("frmSyncManager - showProgress", "enter");
                    double progress = 100.0;
                    if (pbSyncProgress.Maximum > 0)
                        progress = ((double)pbSyncProgress.Value / pbSyncProgress.Maximum) * 100.0;
         //            cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion;
                    cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion + "\n" + LanguageTranslator.GetValue("TrayHoverSyncProgressText"); 
        
                    if (messageValue > messageMax)
                        messageMax = messageValue;

                    lblStatusL1.Text = LanguageTranslator.GetValue("SyncManagerDownloading") + " " + messageValue + " " + LanguageTranslator.GetValue("SyncManagerUsageOfLabel") + " " + messageMax;
                    //LogWrapper.LogMessage("frmSyncManager - showProgress", "leave");
                });
            }
            else
            {
                //LogWrapper.LogMessage("frmSyncManager - showProgress", "enter");
                double progress = 100.0;
                if (pbSyncProgress.Maximum > 0)
                    progress = ((double)pbSyncProgress.Value / pbSyncProgress.Maximum) * 100.0;
        //        cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion;
                cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion + "\n" + LanguageTranslator.GetValue("TrayHoverSyncProgressText"); 
        

                if (messageValue > messageMax)
                    messageMax = messageValue;

                lblStatusL1.Text = LanguageTranslator.GetValue("SyncManagerDownloading") + " " + messageValue + " " + LanguageTranslator.GetValue("SyncManagerUsageOfLabel") + " " + messageMax;
                //LogWrapper.LogMessage("frmSyncManager - showProgress", "leave");
            }
        }

        private void setUpControls()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    lblStatusL1.Text = statusMessages[0];
                    lblStatusL1.Visible = true;

                    lblStatusL3.Text = "";
                    lblStatusL3.Visible = true;
                    pbSyncProgress.Visible = false;
                    tmrSwapStatusMessage.Enabled = true;
                });
            }
            else
            {
                lblStatusL1.Text = statusMessages[0];
                lblStatusL1.Visible = true;

                lblStatusL3.Text = "";
                lblStatusL3.Visible = true;
                pbSyncProgress.Visible = false;
                tmrSwapStatusMessage.Enabled = true;
            }
        }

        private void ShowSyncMessage(bool IsStopped = false, bool IsLocalEvents = false)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    //LogWrapper.LogMessage("frmSyncManager - ShowSyncMessage", "enter");
                    lastSync = DateTime.Now;
                    BasicInfo.LastSyncAt = lastSync;

                    // UpdateUsageLabel();

                    DisableProgress();
                    // this.btnSyncNow.Text = LanguageTranslator.GetValue("SyncManagerSyncNowButtonText");
                    this.btnSyncNow.Enabled = true;

                    if (BasicInfo.AutoSync)
                    {
                        ShowAutoSyncMessage(IsStopped);
                    }
                    else
                    {
                        ShowSyncDisabledMessage();
                    }

                    //LogWrapper.LogMessage("frmSyncManager - ShowSyncMessage", "leave");
                });
            }
            else
            {
                //LogWrapper.LogMessage("frmSyncManager - ShowSyncMessage", "enter");
                lastSync = DateTime.Now;
                BasicInfo.LastSyncAt = lastSync;

                // UpdateUsageLabel();

                DisableProgress();
                // this.btnSyncNow.Text = LanguageTranslator.GetValue("SyncManagerSyncNowButtonText");
                this.btnSyncNow.Enabled = true;

                if (BasicInfo.AutoSync)
                {
                    ShowAutoSyncMessage(IsStopped);
                }
                else
                {
                    ShowSyncDisabledMessage();
                }

                //LogWrapper.LogMessage("frmSyncManager - ShowSyncMessage", "leave");
            }
        }

        private void ShowAutoSyncMessage(bool IsStopped)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    //LogWrapper.LogMessage("frmSyncManager - ShowAutoSyncMessage", "enter");
                    if (IsStopped)
                    {
                        if (frmIssuesFound != null && frmIssuesFound.GetItemsInList() > 0)
                        {
                            IssueFoundBalloonMessage();
                        }
                        else
                        {
                            //SyncStoppedBalloonMessage();
                            SyncPauseBalloonMessage();
                            lblStatusL1.Text = LanguageTranslator.GetValue("TrayBalloonSyncPauseText");
                        }
                    }
                    else
                    {
                        if (frmIssuesFound != null && frmIssuesFound.GetItemsInList() > 0)
                        {
                            IssueFoundBalloonMessage();
                        }
                        else
                        {
                            InitialSyncBalloonMessage();
                        }
                    }

                    lblStatusL3.Text = LanguageTranslator.GetValue("SyncManagerStatusLastSyncLabel") + " " + lastSync.ToString("MMM d, yyyy h:mm tt");
                    label1.Text = LanguageTranslator.GetValue("SyncManagerStatusNextSyncAtLabel") + " " + lastSync.AddMinutes(Convert.ToInt32(global::Mezeo.Properties.Resources.BrSyncTimer)).ToString("h:mm tt");
                    label1.BringToFront();
                    label1.Visible = true;
                    label1.Show();
                    //LogWrapper.LogMessage("frmSyncManager - ShowAutoSyncMessage", "leave");
                });
            }
            else
            {
                //LogWrapper.LogMessage("frmSyncManager - ShowAutoSyncMessage", "enter");
                if (IsStopped)
                {
                    if (frmIssuesFound != null && frmIssuesFound.GetItemsInList() > 0)
                    {
                        IssueFoundBalloonMessage();
                    }
                    else
                    {
                        //SyncStoppedBalloonMessage();
                        SyncPauseBalloonMessage();
                        lblStatusL1.Text = LanguageTranslator.GetValue("TrayBalloonSyncPauseText");
                    }
                }
                else
                {
                    if (frmIssuesFound != null && frmIssuesFound.GetItemsInList() > 0)
                    {
                        IssueFoundBalloonMessage();
                    }
                    else
                    {
                        InitialSyncBalloonMessage();
                    }
                }

                lblStatusL3.Text = LanguageTranslator.GetValue("SyncManagerStatusLastSyncLabel") + " " + lastSync.ToString("MMM d, yyyy h:mm tt");
                label1.Text = LanguageTranslator.GetValue("SyncManagerStatusNextSyncAtLabel") + " " + lastSync.AddMinutes(Convert.ToInt32(global::Mezeo.Properties.Resources.BrSyncTimer)).ToString("h:mm tt");
                label1.BringToFront();
                label1.Visible = true;
                label1.Show();
                //LogWrapper.LogMessage("frmSyncManager - ShowAutoSyncMessage", "leave");
            }
        }

        private void ShowSyncDisabledMessage()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    //frmParent.menuItem7.Text = LanguageTranslator.GetValue("SyncManagerSyncDisabled");
                    //LogWrapper.LogMessage("frmSyncManager - ShowSyncDisabledMessage", "enter");

                    cnotificationManager.NotificationHandler.Icon = Properties.Resources.app_icon_disabled;

                    cnotificationManager.NotificationHandler.ShowBalloonTip(1, LanguageTranslator.GetValue("TrayBalloonSyncStatusText"),
                                                                                  LanguageTranslator.GetValue("SyncManagerSyncDisabled") + ". " + LanguageTranslator.GetValue("SyncManagerTrayEnableOnText"),
                                                                                 ToolTipIcon.None);

                    cnotificationManager.HoverText = LanguageTranslator.GetValue("SyncManagerSyncDisabled") + ". " + LanguageTranslator.GetValue("SyncManagerTrayEnableOnText");
                    frmParent.toolStripMenuItem4.Text = LanguageTranslator.GetValue("SyncManagerSyncDisabled");
                    lblStatusL1.Text = LanguageTranslator.GetValue("SyncManagerSyncDisabled");
                    label1.Text = LanguageTranslator.GetValue("SyncManagerResumeSync");
                    lblStatusL3.Text = LanguageTranslator.GetValue("SyncManagerStatusLastSyncLabel") + " " + lastSync.ToString("MMM d, yyyy h:mm tt");
                    ShowNextSyncLabel(true);
                    //LogWrapper.LogMessage("frmSyncManager - ShowSyncDisabledMessage", "leave");
                });
            }
            else
            {
                //frmParent.menuItem7.Text = LanguageTranslator.GetValue("SyncManagerSyncDisabled");
                //LogWrapper.LogMessage("frmSyncManager - ShowSyncDisabledMessage", "enter");

                cnotificationManager.NotificationHandler.Icon = Properties.Resources.app_icon_disabled;

                cnotificationManager.NotificationHandler.ShowBalloonTip(1, LanguageTranslator.GetValue("TrayBalloonSyncStatusText"),
                                                                              LanguageTranslator.GetValue("SyncManagerSyncDisabled") + ". " + LanguageTranslator.GetValue("SyncManagerTrayEnableOnText"),
                                                                             ToolTipIcon.None);

                cnotificationManager.HoverText = LanguageTranslator.GetValue("SyncManagerSyncDisabled") + ". " + LanguageTranslator.GetValue("SyncManagerTrayEnableOnText");
                frmParent.toolStripMenuItem4.Text = LanguageTranslator.GetValue("SyncManagerSyncDisabled");
                lblStatusL1.Text = LanguageTranslator.GetValue("SyncManagerSyncDisabled");
                label1.Text = LanguageTranslator.GetValue("SyncManagerResumeSync");
                lblStatusL3.Text = LanguageTranslator.GetValue("SyncManagerStatusLastSyncLabel") + " " + lastSync.ToString("MMM d, yyyy h:mm tt");
                ShowNextSyncLabel(true);
                //LogWrapper.LogMessage("frmSyncManager - ShowSyncDisabledMessage", "leave");
            }
        }

        private void DisableProgress()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    //LogWrapper.LogMessage("frmSyncManager - DisableProgress", "enter");
                    lblPercentDone.Visible = false;
                    lblPercentDone.Text = "";

                    lblStatusL1.Text = "";
                    lblStatusL3.Text = "";
                    pbSyncProgress.Visible = false;
                    // btnMoveFolder.Enabled = true;
                    // Commeted above line as move folder functinality disable 
                    ShowNextSyncLabel(true);
                    //LogWrapper.LogMessage("frmSyncManager - DisableProgress", "leave");
                });
            }
            else
            {
                //LogWrapper.LogMessage("frmSyncManager - DisableProgress", "enter");
                lblPercentDone.Visible = false;
                lblPercentDone.Text = "";

                lblStatusL1.Text = "";
                lblStatusL3.Text = "";
                pbSyncProgress.Visible = false;
                // btnMoveFolder.Enabled = true;
                // Commeted above line as move folder functinality disable 
                ShowNextSyncLabel(true);
                //LogWrapper.LogMessage("frmSyncManager - DisableProgress", "leave");
            }
        }

        private void EnableProgress()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    //LogWrapper.LogMessage("frmSyncManager - EnableProgress", "enter");
                    ShowNextSyncLabel(false);
                    Application.DoEvents();
                    //LogWrapper.LogMessage("frmSyncManager - EnableProgress", "leave");
                });
            }
            else
            {
                //LogWrapper.LogMessage("frmSyncManager - EnableProgress", "enter");
                ShowNextSyncLabel(false);
                Application.DoEvents();
                //LogWrapper.LogMessage("frmSyncManager - EnableProgress", "leave");
            }
        }

        private void OpenFolder()
        {
            string argument = BasicInfo.SyncDirPath;
            System.Diagnostics.Process.Start(argument);
        }

        public void DisableSyncManager()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    //LogWrapper.LogMessage("frmSyncManager - DisableSyncManager", "enter");
                    SetCanNotTalkToServer(true);
                    StopSync();
                    btnSyncNow.Enabled = false;
                    //LogWrapper.LogMessage("frmSyncManager - DisableSyncManager", "leave");
                });
            }
            else
            {
                //LogWrapper.LogMessage("frmSyncManager - DisableSyncManager", "enter");
                SetCanNotTalkToServer(true);
                StopSync();
                btnSyncNow.Enabled = false;
                //LogWrapper.LogMessage("frmSyncManager - DisableSyncManager", "leave");
            }
        }

        public void changePauseResumeBtnText()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    this.btnPauseResume.Text = LanguageTranslator.GetValue("PauseSync");
                });
            }
            else
            {
                this.btnPauseResume.Text = LanguageTranslator.GetValue("PauseSync");
            }
        }

        public void EnableSyncManager()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    //LogWrapper.LogMessage("frmSyncManager - EnableSyncManager", "enter");
                    //SyncOnlineMessage();
                  //  SetCanNotTalkToServer(false);
                  //  btnSyncNow.Enabled = true;
                    if (lockObject != null)
                        lockObject.StopThread = false;
                });
            }
            else
            {
                //LogWrapper.LogMessage("frmSyncManager - EnableSyncManager", "enter");
                //SyncOnlineMessage();
             //   SetCanNotTalkToServer(false);
             //   btnSyncNow.Enabled = true;
                if (lockObject != null)
                    lockObject.StopThread = false;
            }
        }

        public void ChangeUIOnPause()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    lblStatusL1.Text = LanguageTranslator.GetValue("TrayBalloonSyncPauseText");
                    if (lastSync.ToString("MMM d, yyyy h:mm tt") == "Jan 1, 0001 12:00 AM")
                        lblStatusL3.Text = "";
                    else
                        lblStatusL3.Text = lblStatusL3.Text = LanguageTranslator.GetValue("SyncManagerStatusLastSyncLabel") + " " + lastSync.ToString("MMM d, yyyy h:mm tt"); 
                    
                    label1.Text = LanguageTranslator.GetValue("ResumeSyncOprationText");
                   // btnSyncNow.Text = this.btnSyncNow.Text = LanguageTranslator.GetValue("ResumeSyncText");
                    btnSyncNow.Enabled = false;
                    btnPauseResume.Text = LanguageTranslator.GetValue("ResumeSyncText");
                    if (bwSyncThread.IsBusy)
                    {
                        btnPauseResume.Enabled = false;
                        frmParent.DisableContextMenuText();
                    }
                    pbSyncProgress.Hide();
                    pbSyncProgress.Visible = false;
                    lblPercentDone.Hide();
                    lblPercentDone.Visible = false;
                    label1.BringToFront();
                    label1.Visible = true;
                    label1.Show();
                });
            }
            else
            {
                lblStatusL1.Text = LanguageTranslator.GetValue("TrayBalloonSyncPauseText");
                if (lastSync.ToString("MMM d, yyyy h:mm tt") == "Jan 1, 0001 12:00 AM")
                    lblStatusL3.Text = "";
                else
                    lblStatusL3.Text = lblStatusL3.Text = LanguageTranslator.GetValue("SyncManagerStatusLastSyncLabel") + " " + lastSync.ToString("MMM d, yyyy h:mm tt"); 
                
                label1.Text = LanguageTranslator.GetValue("ResumeSyncOprationText");
                // btnSyncNow.Text = this.btnSyncNow.Text = LanguageTranslator.GetValue("ResumeSyncText");
                btnSyncNow.Enabled = false;
                btnPauseResume.Text = LanguageTranslator.GetValue("ResumeSyncText");
                if (bwSyncThread.IsBusy)
                {
                    btnPauseResume.Enabled = false;
                    frmParent.DisableContextMenuText();
                }
                pbSyncProgress.Hide();
                pbSyncProgress.Visible = false;
                lblPercentDone.Hide();
                lblPercentDone.Visible = false;
                label1.BringToFront();
                label1.Visible = true;
                label1.Show();
            }
        }

        public void ShowSyncManagerOffline()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    //LogWrapper.LogMessage("frmSyncManager - ShowSyncManagerOffline", "enter");
                    //SyncOfflineMessage();
                    lblStatusL1.Text = LanguageTranslator.GetValue("AppOfflineMenu");
                    label1.Text = "";

                    //Adding following line for fogbugzid: 1489
                    if (lastSync.ToString("MMM d, yyyy h:mm tt") == "Jan 1, 0001 12:00 AM")
                        lblStatusL3.Text = "";
                    else
                        lblStatusL3.Text = lblStatusL3.Text = LanguageTranslator.GetValue("SyncManagerStatusLastSyncLabel") + " " + lastSync.ToString("MMM d, yyyy h:mm tt");

                    //btnMoveFolder.Enabled = false;
                    //Commeted above line as move folder functinality disable 

                    lblPercentDone.Text = "";
                    pbSyncProgress.Visible = false;
                    pbSyncProgress.Hide();
                    //LogWrapper.LogMessage("frmSyncManager - ShowSyncManagerOffline", "leave");
                });
            }
            else
            {
                //LogWrapper.LogMessage("frmSyncManager - ShowSyncManagerOffline", "enter");
                //SyncOfflineMessage();
                lblStatusL1.Text = LanguageTranslator.GetValue("AppOfflineMenu");
                label1.Text = "";

                //Adding following line for fogbugzid: 1489
                if (lastSync.ToString("MMM d, yyyy h:mm tt") == "Jan 1, 0001 12:00 AM")
                    lblStatusL3.Text = "";
                else
                    lblStatusL3.Text = lblStatusL3.Text = LanguageTranslator.GetValue("SyncManagerStatusLastSyncLabel") + " " + lastSync.ToString("MMM d, yyyy h:mm tt");

                //btnMoveFolder.Enabled = false;
                //Commeted above line as move folder functinality disable 

                lblPercentDone.Text = "";
                pbSyncProgress.Visible = false;
                pbSyncProgress.Hide();
                //LogWrapper.LogMessage("frmSyncManager - ShowSyncManagerOffline", "leave");
            }
        }

        private void ShowLocalEventsCompletedMessage()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    ShowSyncMessage(EventQueue.QueueNotEmpty());

                    if (frmIssuesFound != null && frmIssuesFound.GetItemsInList() > 0)
                    {
                        IssueFoundBalloonMessage();
                    }
                    else
                    {
                        if (BasicInfo.AutoSync)
                            cnotificationManager.NotificationHandler.Icon = Properties.Resources.MezeoVault;
                        else
                            cnotificationManager.NotificationHandler.Icon = Properties.Resources.app_icon_disabled;

                        SyncFolderUpToDateMessage();
                    }
                    //LogWrapper.LogMessage("frmSyncManager - ShowLocalEventsCompletedMessage", "leave");
                });
            }
            else
            {
                ShowSyncMessage(EventQueue.QueueNotEmpty());

                if (frmIssuesFound != null && frmIssuesFound.GetItemsInList() > 0)
                {
                    IssueFoundBalloonMessage();
                }
                else
                {
                    if (BasicInfo.AutoSync)
                        cnotificationManager.NotificationHandler.Icon = Properties.Resources.MezeoVault;
                    else
                        cnotificationManager.NotificationHandler.Icon = Properties.Resources.app_icon_disabled;

                    SyncFolderUpToDateMessage();
                }
                //LogWrapper.LogMessage("frmSyncManager - ShowLocalEventsCompletedMessage", "leave");
            }
        }

        #endregion


        #region OnClick Events from Form 
        
        private void btnIssuesFound_Click(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    frmIssuesFound.ClearList();
                    frmIssuesFound.AddIssuesToList(dbHandler.GetConflicts());
                    frmIssuesFound.Show();
                    frmIssuesFound.BringToFront();
                });
            }
            else
            {
                frmIssuesFound.ClearList();
                frmIssuesFound.AddIssuesToList(dbHandler.GetConflicts());
                frmIssuesFound.Show();
                frmIssuesFound.BringToFront();
            }
        }

        private void frmSyncManager_Load(object sender, EventArgs e)
        {
            Hide();
            UpdateUsageLabel();
        }

        private void lnkHelp_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start(BasicInfo.ServiceUrl + "/help/sync");
        }

        private void lnkServerUrl_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start(BasicInfo.ServiceUrl);
        }

        private void linkLabel4_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            AboutBox aboutBox = new AboutBox();
            aboutBox.ShowDialog();
        }

        private void btnSyncNow_Click(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    //// SetIsEventCanceled(false);
                    //// Is the Sync paused?
                    //if (IsSyncPaused())
                    //{
                    //    // Then resume the sync if events are in the queue.
                    //    SyncResumeBalloonMessage();
                    //    SetSyncPaused(false);
                    //    if (!bwSyncThread.IsBusy)
                    //        bwSyncThread.RunWorkerAsync();
                    //}
                    if (!IsSyncThreadInProgress())
                    {
                        // If no sync was in progress, then start a sync operation if events exist in the queue.
                        InitializeSync();
                    }
                    //else
                    //{
                    //    // It wasn't paused, and it wasn't idle, so we must have been performing a sync.  Pause it.
                    //    StopSync();
                    //    SetSyncPaused(true);
                    //    SyncPauseBalloonMessage();
                    //}

                    resetAllControls();
                });
            }
            else
            {
                // SetIsEventCanceled(false);
                //if (IsSyncPaused())
                //{
                //    SyncResumeBalloonMessage();
                //    SetSyncPaused(false);

                //    if (!bwSyncThread.IsBusy)
                //        bwSyncThread.RunWorkerAsync();
                //}
                if (!IsSyncThreadInProgress())
                {
                    InitializeSync();
                }
                //else
                //{
                //    StopSync();
                //    SetSyncPaused(true);
                //    SyncPauseBalloonMessage();
                //}

                resetAllControls();
            }
        }

        private void lnkFolderPath_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            OpenFolder();
        }

        private void btnPauseResume_Click(object sender, EventArgs e)
        {
            if (!IsSyncPaused())
            {
                frmParent.syncPausedOperation();
                return;
            }
            if (IsSyncPaused())
            {
                frmParent.syncResumeOperation();
                return;
            }
        }

        private void frmSyncManager_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
        }

        #endregion

        #region Functions and Methods

        // Read in the response for an HTTP request.
        private String OnGetResponseString(Stream responseStream)
        {
            StringBuilder responseString = new StringBuilder();
            byte[] buffer = new byte[1024 * 64];
            int bytes_read = 0;
            while ((bytes_read = responseStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                responseString.Append(Encoding.UTF8.GetString(buffer, 0, bytes_read));
            }
            responseStream.Close();
            return responseString.ToString();
        }

        private void tmrNextSync_Tick(object sender, EventArgs e)
        {
            if(!CanNotTalkToServer())
                checkForAppUpdate(false);
            
            // See if I need to kick off a sync action.
            if (IsInIdleState())
            {
                if (this.InvokeRequired)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        tmrNextSync.Interval = getSynNextCycleTimer();
                    });
                }
                else
                {
                    tmrNextSync.Interval = getSynNextCycleTimer();
                }

                InitializeSync();
            }
            else
            {
                if (this.InvokeRequired)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        tmrNextSync.Interval = FIVE_SECONDS;
                    });
                }
                else
                {
                    tmrNextSync.Interval = FIVE_SECONDS;
                }
            }
        }

        public void checkForAppUpdate(bool ignoreTime)
        {
            int updateTimer = Convert.ToInt32(global::Mezeo.Properties.Resources.BrUpdateTimer);

            // Don't check for updates the first time the app runs....  Leave that to Sparkle.
            if (1 == BasicInfo.LastUpdateCheckAt.Year)
            {
                // Update the time we last checked for an update.
                BasicInfo.LastUpdateCheckAt = DateTime.Now;
            }
            else
            {
                // Only look for updates once a day.
                // TODO: Make the timespan (in hours) a string that can be part of branding or configuration.
                // TODO: Put this on a different thread since it makes a network call.  Possibly on the CheckServerStatus thread.
                TimeSpan diff = DateTime.Now - BasicInfo.LastUpdateCheckAt;
                if (updateTimer < diff.TotalHours || ignoreTime)
                {
                    // See if an update is available.
                    string strURL = BasicInfo.GetUpdateURL();
                    string strCurVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                    string strNewVersion = "";

                    // Remove the 4th field from the version since that doesn't exist in the sparkle version.
                    string[] strSubVersion = strCurVersion.Split('.');
                    strCurVersion = strSubVersion[0] + "." + strSubVersion[1] + "." + strSubVersion[2];

                    // Put the version check inside of a try/catch block in case an exception
                    // is thrown (ex 404 or network problem).  This keeps the app from crashing.
                    try
                    {
                        // Check to see what versions are available.
                        HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(strURL);
                        webRequest.Method = "GET";
                        webRequest.KeepAlive = false;
                        webRequest.Timeout = 30000;

                        HttpWebResponse response = (HttpWebResponse)(webRequest.GetResponse());
                        string strTemp = OnGetResponseString(response.GetResponseStream());

                        XmlDocument m_xmlVersionList = new XmlDocument();
                        m_xmlVersionList.LoadXml(strTemp);

                        XmlNodeList nodes = m_xmlVersionList.SelectNodes("/rss/channel/item");
                        if (null != nodes)
                        {
                            foreach (XmlNode node in nodes)
                            {
                                if (node.HasChildNodes)
                                {
                                    // See what the most recent version is and if it is newer than the current version.
                                    XmlNode enclosure = node.SelectSingleNode("enclosure");
                                    if (null != enclosure)
                                    {
                                        XmlNode xmlVersion = enclosure.Attributes.GetNamedItem("sparkle:version");
                                        if (null != xmlVersion)
                                        {
                                            if (-1 == strCurVersion.CompareTo(xmlVersion.Value))
                                                strNewVersion = xmlVersion.Value;

                                        }
                                    }
                                }
                            }
                        }

                        // If an update is available, the show a pop
                        if ((null != strNewVersion) && (0 != strNewVersion.Length))
                        {
                          //  ShowUpdateAvailableBalloonMessage(strNewVersion);
                            BasicInfo.updateAvailable = true;

                            //if (BasicInfo.updateAvailable == true)
                            //{
                            //    string RSSFeed = BasicInfo.GetUpdateURL();

                            //    if (RSSFeed.Length != 0)
                            //    {
                            //        _sparkle = new Sparkle(RSSFeed);
                            //        _sparkle.StartLoop(true);
                            //    }
                            //}

                           // frmParent.changeUpdatesText(strNewVersion);
                            // cnotificationManager.HoverText = "Install Update";
                        }
                        else
                        {
                            // The only time ignoreTime is true is when the user has clicked on 'check for new versions'.
                            // Otherwise, we don't want to pop up a message box every time the timer activates.
                            if (ignoreTime)
                                ShowCurrentVersionBalloonMessage(strCurVersion);
                        }

                        // Update the time we last checked for an update.
                        BasicInfo.LastUpdateCheckAt = DateTime.Now;
                    }
                    catch (Exception ex)
                    {
                        LogWrapper.LogMessage("frmSyncManager - tmrNextSync_Tick (checking for updates)", "Caught exception: " + ex.Message);
                    }
                }
            }
        }

        private void OnThreadCancel(CancelReason reason)
        {
            if (!IsAnalysisCompleted() && !isDownloadingFile)
            {
                if (lockObject.ExitApplication)
                    System.Environment.Exit(0);
                else
                {
                    ShowSyncMessage(true);
                    btnSyncNow.Enabled = true;

                    if (reason == CancelReason.LOGIN_FAILED)
                    {
                        this.Hide();
                        frmParent.ShowLoginAgainFromSyncMgr();
                    }
                    else if (reason == CancelReason.SERVER_INACCESSIBLE)
                    {
                        // CheckServerStatus();TODO:check for offline (Modified for server status thread)
                    }
                }
            }
        }

        private void LoadResources()
        {
            this.Text = AboutBox.AssemblyTitle; 
            this.lblFolder.Text = LanguageTranslator.GetValue("SyncManagerFolderLabel");
            this.lblStatus.Text = LanguageTranslator.GetValue("SyncManagerStatusLabel");
            this.lblUsage.Text = LanguageTranslator.GetValue("SyncManagerUsageLabel");

            this.btnPauseResume.Text = LanguageTranslator.GetValue("PauseSync");
            this.btnSyncNow.Text = LanguageTranslator.GetValue("SyncManagerSyncNowButtonText");
            this.btnIssuesFound.Text = LanguageTranslator.GetValue("SyncManagerConflictsButtonText");
            this.lnkAbout.Text = LanguageTranslator.GetValue("SyncManagerAboutLinkText");
            this.lnkHelp.Text = LanguageTranslator.GetValue("SyncManagerHelpLinkText");
            this.btnIssuesFound.Visible = false;

            this.lblUserName.Text = BasicInfo.UserName;
            this.lnkServerUrl.Text = BasicInfo.ServiceUrl;
            this.lnkFolderPath.Text = BasicInfo.SyncDirPath;

            statusMessages[0] = LanguageTranslator.GetValue("SyncManagerAnalyseMessage1");
            statusMessages[1]= LanguageTranslator.GetValue("SyncManagerAnalyseMessage2");
            statusMessages[2] = LanguageTranslator.GetValue("SyncManagerAnalyseMessage3");

            if (cLoginDetails != null)
            {
                string usedSize = FormatSizeString(cLoginDetails.dblStorage_Used);
                string allocatedSize = "";

                if (cLoginDetails.dblStorage_Allocated == -1)
                {
                    allocatedSize = LanguageTranslator.GetValue("SyncManagerUsageUnlimited");
                }
                else
                {
                    allocatedSize = FormatSizeString(cLoginDetails.dblStorage_Allocated);
                }

                allocatedSize += " " + LanguageTranslator.GetValue("SyncManagerUsageUsed");

                this.lblUsageDetails.Text = usedSize + " " + LanguageTranslator.GetValue("SyncManagerUsageOfLabel") + " " + allocatedSize;
            }
            else
                this.lblUsageDetails.Text = LanguageTranslator.GetValue("UsageNotAvailable");
        }
    
        public void InitializeSync()
        {
            if (!IsSyncThreadInProgress())
            {
                SetUpSync();
                SyncNow();
            }
            else
            {
                //SetIsEventCanceled(true);
                StopSync();
            }
        }

        public void StopSync()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    if (this.InvokeRequired)
                    {
                        this.Invoke((MethodInvoker)delegate
                        {
                            //tmrNextSync.Start();
                            tmrNextSync.Interval = getSynNextCycleTimer();
                        });
                    }
                    else
                    {
                        //tmrNextSync.Start();
                        tmrNextSync.Interval = getSynNextCycleTimer();
                    }

                    if (IsSyncThreadInProgress())
                    {
                        //SetIsEventCanceled(true);
                        bwSyncThread.CancelAsync();
                        // btnSyncNow.Enabled = false;
                        if (lockObject != null)
                            lockObject.StopThread = true;

                        if (frmIssuesFound != null && frmIssuesFound.GetItemsInList() > 0)
                        {
                            IssueFoundBalloonMessage();
                        }
                        else
                        {
                            //SyncStoppedBalloonMessage();
                        }
                        cMezeoFileCloud.StopSyncProcess();
                    }
                });
            }
            else
            {
                if (this.InvokeRequired)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        //tmrNextSync.Start();
                        tmrNextSync.Interval = getSynNextCycleTimer();
                    });
                }
                else
                {
                    //tmrNextSync.Start();
                    tmrNextSync.Interval = getSynNextCycleTimer();
                }

                if (IsSyncThreadInProgress())
                {
                    //SetIsEventCanceled(true);
                    bwSyncThread.CancelAsync();
                    // btnSyncNow.Enabled = false;
                    if (lockObject != null)
                        lockObject.StopThread = true;

                    if (frmIssuesFound != null && frmIssuesFound.GetItemsInList() > 0)
                    {
                        IssueFoundBalloonMessage();
                    }
                    else
                    {
                        //SyncStoppedBalloonMessage();
                    }
                    cMezeoFileCloud.StopSyncProcess();
                }
            }
        }

        private void DeleteCurrentIncompleteFile(string fileName)
        {
            String strOriginal = ".original." + BasicInfo.MacAddress;
            String strPartial = ".partial." + BasicInfo.MacAddress;
            bool isFile = System.IO.File.Exists(fileName);
            
            if (isFile)
            {
                string key = fileName.Substring(BasicInfo.SyncDirPath.Length+1);
                string status = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.STATUS, new string[] { DbHandler.KEY }, new string[] { key }, new DbType[] { DbType.String });

                if (status != DB_STATUS_SUCCESS)
                {
                    dbHandler.Delete(DbHandler.TABLE_NAME, DbHandler.KEY ,key);
                    //System.IO.File.Delete(fileName);
                }
                // Only delete stuff that has .partial.m_strMacAdd or .original.m_strMacAdd in the name.
                if (fileName.Contains(strOriginal) || fileName.Contains(strPartial))
                    System.IO.File.Delete(fileName);
            }
        }

        private void StopLocalSync()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    lblPercentDone.Text = "";
                    pbSyncProgress.Visible = false;
                    pbSyncProgress.Hide();
                    ShowNextSyncLabel(true);
                    btnSyncNow.Enabled = true;

                    if (frmIssuesFound != null && frmIssuesFound.GetItemsInList() > 0)
                    {
                        IssueFoundBalloonMessage();
                    }
                    else
                    {
                        if (BasicInfo.AutoSync)
                            cnotificationManager.NotificationHandler.Icon = Properties.Resources.MezeoVault;
                        else
                            cnotificationManager.NotificationHandler.Icon = Properties.Resources.app_icon_disabled;

                        SyncStoppedBalloonMessage();
                    }
                });
            }
            else
            {
                lblPercentDone.Text = "";
                pbSyncProgress.Visible = false;
                pbSyncProgress.Hide();
                ShowNextSyncLabel(true);
                btnSyncNow.Enabled = true;

                if (frmIssuesFound != null && frmIssuesFound.GetItemsInList() > 0)
                {
                    IssueFoundBalloonMessage();
                }
                else
                {
                    if (BasicInfo.AutoSync)
                        cnotificationManager.NotificationHandler.Icon = Properties.Resources.MezeoVault;
                    else
                        cnotificationManager.NotificationHandler.Icon = Properties.Resources.app_icon_disabled;

                    SyncStoppedBalloonMessage();
                }
            }
        }

        public int CheckServerStatus()
        {   
            int nStatusCode = 0;

            if (cLoginDetails == null)
            {
                cLoginDetails = frmParent.loginFromSyncManager();
                if (cLoginDetails == null)
                    return -1;
            }

            NQLengthResult nqLengthRes = cMezeoFileCloud.NQGetLength(BasicInfo.ServiceUrl + cLoginDetails.szNQParentUri, BasicInfo.GetQueueName(), ref nStatusCode);
            if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
            {
                StopSync();
                this.Hide();
                SetCanNotTalkToServer(true);
                frmParent.ShowLoginAgainFromSyncMgr();
                return -1;
            }
            else if (nStatusCode != ResponseCode.NQGETLENGTH)
            {
                StopSync();
                DisableSyncManager();
                SetCanNotTalkToServer(true);
                ShowSyncManagerOffline();
                return -2;
            }

            SetCanNotTalkToServer(false);

            return 1;
        }

        public void SyncNow()
        {
           // TODO:check for offline (Modified for server status thread)

            if (!CanNotTalkToServer())
                EnableSyncManager();

            SetUpSyncNowNotification();

            if (BasicInfo.IsInitialSync)
            {
                ShowNextSyncLabel(false);
                SetUpControlForSync();
                cnotificationManager.NotificationHandler.ShowBalloonTip(1, LanguageTranslator.GetValue("TrayBalloonInitialSyncStartedTitleText"),
                                                                        LanguageTranslator.GetValue("TrayBalloonInitialSyncStartedText"),
                                                                        ToolTipIcon.None);

                queue = new Queue<LocalItemDetails>();
                lockObject = new ThreadLockObject();
                lockObject.StopThread = false;
                stDownloader = new StructureDownloader(queue, lockObject, cLoginDetails.szContainerContentsUri, cMezeoFileCloud);
                stDownloader.downloadEvent += new StructureDownloader.StructureDownloadEvent(stDownloader_downloadEvent);

                analyseThread = new Thread(stDownloader.startAnalyseItemDetails);

                setUpControls();
                SetAnalysisIsCompleted(false);
                analyseThread.Start();
            }
            else
            {
                if (false == EventQueue.QueueNotEmpty())
                {
                    ShowNextSyncLabel(false);
                    PopulateNQEvents();
                }

                if (EventQueue.QueueNotEmpty())
                {
                    ShowNextSyncLabel(false);
                    if (!bwSyncThread.IsBusy)
                        bwSyncThread.RunWorkerAsync();
                }
                else
                {
                   resetAllControls();
                }
            }
            //LogWrapper.LogMessage("frmSyncManager - SyncNow", "leave");
        }

        public void ProcessOfflineEvents()
        {
            //LogWrapper.LogMessage("frmSyncManager - ProcessOfflineEvents", "enter");
            // See if there are any offline events since the last time we ran.
            SetSyncGenerateLocalEvent(true);
            offlineWatcher.PrepareStructureList();
            SetSyncGenerateLocalEvent(false);
        
            if (false == EventQueue.QueueNotEmpty())
            {
                PopulateNQEvents();
            }

            if (EventQueue.QueueNotEmpty())
            {          
                if (!bwSyncThread.IsBusy)
                    bwSyncThread.RunWorkerAsync();
            }
            else
            {
                resetAllControls();
                //UpdateNQ();
            }
            //LogWrapper.LogMessage("frmSyncManager - ProcessOfflineEvents", "leave");
        }

        private int UpdateFromNQ(NQDetails UpdateQ)
        {
            //LogWrapper.LogMessage("frmSyncManager - UpdateFromNQ - ", "Enter"); 

            NQDetails nqDetail = UpdateQ;
            int nStatus = 0;

            int nStatusCode = 0;
            NSResult nsResult = null;

            //Encoding uri for special character and then replacing "+" with "%20" if we have spaces in file name 
            string uri = cLoginDetails.szNamespaceUri + "/" + HttpUtility.UrlEncode(nqDetail.StrMezeoExportedPath);
            uri = uri.Replace("+", "%20");
            
            //nsResult = cMezeoFileCloud.GetNamespaceResult(cLoginDetails.szNamespaceUri + "/" +
            //                                                nqDetail.StrMezeoExportedPath,
            //                                                nqDetail.StrObjectType, ref nStatusCode);

            nsResult = cMezeoFileCloud.GetNamespaceResult(uri, nqDetail.StrObjectType, ref nStatusCode);
            if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
            {
                return nStatusCode;
            }
            else if (nStatusCode != ResponseCode.GETNAMESPACERESULT && nStatusCode != ResponseCode.NOTFOUND)
            {
                return nStatusCode;
            }
            
            if (nsResult == null && nqDetail.StrEvent != "cdmi_delete")
            {
                //LogWrapper.LogMessage("frmSyncManager - UpdateFromNQ - ", "nsResult Null");
                nStatus = 1;
                return nStatus;
            }

            if (nqDetail.StrObjectName == "csp_recyclebin")
            {
                //LogWrapper.LogMessage("frmSyncManager - UpdateFromNQ - skipping csp_recyclebin notification.", "csp_recyclebin notification skipped");
                nStatus = 1;
                return nStatus;
            }

            int startIndex = Math.Min(cLoginDetails.szNQParentUri.Length + 1, nqDetail.StrParentUri.Length);
            string strPath = nqDetail.StrParentUri.Substring(startIndex);
            string strKey = strPath.Replace("/" , "\\");
            
            if(nsResult == null)
                strKey += nqDetail.StrObjectName;
            else
                strKey += nsResult.StrName;

            strPath = BasicInfo.SyncDirPath + "\\" + strKey;
            //lblStatusL3.Text = strPath;

            if (nqDetail.StrEvent == "cdmi_create_complete")
            {
                //LogWrapper.LogMessage("frmSyncManager - UpdateFromNQ - ", nqDetail.StrEvent + " - " + strKey + " - Enter"); 

                string strDBKey = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.KEY, new string[] { DbHandler.CONTENT_URL }, new string[] { nsResult.StrContentsUri }, new DbType[] { DbType.String });

                // Just because the length is 0 doesn't make it the only reason to download the file.
                // Check the file size against the event.  If the size differs, then download the file
                // again since the local one is a partial...probably from a pause/resume.
                // The pause/resume can also result in a file that doesn't exist even though the database
                // entry does.  That condition should also be marked as a difference so that the event
                // is processed and the file is downloaded.
                bool bLocalFileDiffers = false;
                if (strDBKey.Trim().Length != 0)
                {
                    FileInfo fileInfo = new FileInfo(strPath);
                    if ((fileInfo != null) && (0 == (fileInfo.Attributes & FileAttributes.Directory)) && fileInfo.Exists && (fileInfo.Length != nqDetail.lSize))
                        bLocalFileDiffers = true;
                    if ((0 == (fileInfo.Attributes & FileAttributes.Directory)) && (!System.IO.File.Exists(strPath)))
                        bLocalFileDiffers = true;
                }

                if ((strDBKey.Trim().Length == 0) || bLocalFileDiffers)
                {
                    nStatus = nqEventCdmiCreate(nqDetail, nsResult, strKey, strPath);
                    if (nStatus == ResponseCode.LOGINFAILED1 || nStatus == ResponseCode.LOGINFAILED2)
                    {
                        return nStatus;
                    }
                    else if (nStatus != ResponseCode.GETETAG && nStatus != ResponseCode.DOWNLOADFILE && nStatus != ResponseCode.DOWNLOADITEMDETAILS && nStatus != 1)
                    {
                        return nStatus;
                    }
                }
                else
                    nStatus = 1;

                //LogWrapper.LogMessage("frmSyncManager - UpdateFromNQ - ", nqDetail.StrEvent + " - " + strKey + " - Leave"); 
            }
            else if (nqDetail.StrEvent == "cdmi_modify_complete")
            {
                //LogWrapper.LogMessage("frmSyncManager - UpdateFromNQ - ", nqDetail.StrEvent + " - " + strKey + " - Enter"); 

                string strDBKey = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.KEY, new string[] { DbHandler.CONTENT_URL }, new string[] { nsResult.StrContentsUri }, new DbType[] { DbType.String });
                string strDBEtag = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.E_TAG, new string[] { DbHandler.KEY }, new string[] { strDBKey }, new DbType[] { DbType.String });
                string strEtagCloud = cMezeoFileCloud.GetETag(nsResult.StrContentsUri, ref nStatusCode);
                if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
                {
                    return nStatusCode;
                }
                else if (nStatusCode != ResponseCode.GETETAG)
                {
                    return nStatusCode;
                }
                if (strDBEtag == strEtagCloud)
                {
                    if (strKey != strDBKey)
                    {
                        if (File.Exists(strPath))
                        {
                            nqEventCdmiDelete(BasicInfo.SyncDirPath + "\\" + strDBKey, strDBKey);
                        }
                        else
                        {
                            if(File.Exists(BasicInfo.SyncDirPath + "\\" + strDBKey))
                                File.Move(BasicInfo.SyncDirPath + "\\" + strDBKey, strPath);
                        }

                        dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.KEY , strKey , DbHandler.KEY , strDBKey );
                    }
                }
                else
                {
                    if ("FILE" == nsResult.StrType)
                    {
                        bool bRet = cMezeoFileCloud.DownloadFile(nsResult.StrContentsUri + '/' + nsResult.StrName, strPath, nsResult.dblSizeInBytes, ref nStatusCode);
                        if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
                        {
                            return nStatusCode;
                        }
                        else if (nStatusCode != ResponseCode.DOWNLOADFILE)
                        {
                            return nStatusCode;
                        }
                        if (bRet)
                        {
                            dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.E_TAG, strEtagCloud, DbHandler.KEY, strKey);
                            bool isFileExist = File.Exists(strPath);
                            if (isFileExist)
                            {
                                FileInfo fInfo = new FileInfo(strPath);
                                dbHandler.UpdateModifiedDate(fInfo.LastWriteTime, strKey);
                            }
                        }
                    }
                }
                nStatus = 1;
                //LogWrapper.LogMessage("frmSyncManager - UpdateFromNQ - ", nqDetail.StrEvent + " - " + strKey + " - Leave"); 
            }
            else if (nqDetail.StrEvent == "cdmi_delete")
            {
                //LogWrapper.LogMessage("frmSyncManager - UpdateFromNQ - ", nqDetail.StrEvent + " - " + strKey + " - Enter");

                // bugzid:2352 - file is open when app gets cloud event to delete same file
                // If the file can't be deleted (due to being in use), then just keep retrying
                // until the file is released.
                if (nqEventCdmiDelete(strPath, strKey))
                    nStatus = 1;
                else
                    nStatus = 0;

                //LogWrapper.LogMessage("frmSyncManager - UpdateFromNQ - ", nqDetail.StrEvent + " - " + strKey + " - Leave"); 
            }
            else if (nqDetail.StrEvent == "cdmi_rename")
            {
                //LogWrapper.LogMessage("frmSyncManager - UpdateFromNQ - ", nqDetail.StrEvent + " - " + strKey + " - Enter"); 

                string strDBKey = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.KEY, new string[] { DbHandler.CONTENT_URL }, new string[] { nsResult.StrContentsUri }, new DbType[] { DbType.String });
                if (strDBKey.Trim().Length != 0 && strDBKey != strKey)
                {
                    if(Directory.Exists(strPath))
                    {
                        nqEventCdmiDelete(BasicInfo.SyncDirPath + "\\" + strDBKey, strDBKey);
                    }
                    else
                    {
                        Directory.Move(BasicInfo.SyncDirPath + "\\" + strDBKey, strPath);
                        dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.KEY , strKey , DbHandler.KEY , strDBKey );

                        DirectoryInfo rootDir = new DirectoryInfo(strPath);
                        WalkDirectoryTree(rootDir, BasicInfo.SyncDirPath + "\\" + strDBKey);
                    }
                }
                else if(strDBKey.Trim().Length == 0)
                {
                    nStatus = nqEventCdmiCreate(nqDetail, nsResult, strKey, strPath);
                    if (nStatus == ResponseCode.LOGINFAILED1 || nStatus == ResponseCode.LOGINFAILED2)
                    {
                        return nStatus;
                    }
                    else if (nStatus != ResponseCode.GETETAG && nStatus != ResponseCode.DOWNLOADFILE && nStatus != ResponseCode.DOWNLOADITEMDETAILS && nStatus != 1)
                    {
                        return nStatus;
                    }
                }
                nStatus = 1;
                //LogWrapper.LogMessage("frmSyncManager - UpdateFromNQ - ", nqDetail.StrEvent + " - " + strKey + " - Leave"); 
            }
            else if (nqDetail.StrEvent == "cdmi_copy")
            {
                //LogWrapper.LogMessage("frmSyncManager - UpdateFromNQ - ", nqDetail.StrEvent + " - " + strKey + " - Enter"); 

                if (strKey.LastIndexOf("\\") != -1)
                    CheckAndCreateForParentDir(strKey.Substring(0, strKey.LastIndexOf("\\")));

                FileFolderInfo fileFolderInfo = new FileFolderInfo();

                fileFolderInfo.IsPublic = nsResult.bPublic;
                fileFolderInfo.IsShared = nsResult.bShared;
                fileFolderInfo.ContentUrl = nsResult.StrContentsUri;
                fileFolderInfo.CreatedDate = nsResult.dtCreated;
                fileFolderInfo.FileName = nsResult.StrName;
                fileFolderInfo.FileSize = nsResult.dblSizeInBytes;
                fileFolderInfo.MimeType = nsResult.StrMimeType;
                fileFolderInfo.ModifiedDate = nsResult.dtModified;
                fileFolderInfo.ParentUrl = nsResult.StrParentUri;
                fileFolderInfo.Status = DB_STATUS_SUCCESS;
                fileFolderInfo.Type = nsResult.StrType;
                fileFolderInfo.Key = strKey;

                int lastSepIndex = strKey.LastIndexOf("\\");
                string parentDirPath = "";

                if (lastSepIndex != -1)
                {
                    parentDirPath = strKey.Substring(0, strKey.LastIndexOf("\\"));
                    parentDirPath = parentDirPath.Substring(parentDirPath.LastIndexOf("\\") + 1);
                }

                fileFolderInfo.ParentDir = parentDirPath;
                bool bRet = false; 
                if (nqDetail.StrObjectType == "DIRECTORY")
                {
                    if (Directory.Exists(strPath))
                    {
                        //string strCheck = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.KEY, DbHandler.KEY + "='" + strKey + "'");
                        string strCheck = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.KEY, new string[] { DbHandler.KEY }, new string[] { strKey }, new DbType[] { DbType.String });
                        if(strCheck == strKey)
                            bRet = false;
                        else
                            bRet = true;
                    }
                    else
                    {
                        Directory.CreateDirectory(strPath);
                        bRet = true;
                    }
                }
                else
                {
                    if (File.Exists(strPath))
                    {
                        bRet = false;
                    }
                    else
                    {
                        string strEtagCloud = cMezeoFileCloud.GetETag(nsResult.StrContentsUri, ref nStatusCode);
                        if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
                        {
                            return nStatusCode;
                        }
                        else if (nStatusCode != ResponseCode.GETETAG)
                        {
                            return nStatusCode;
                        }
                        //string strCheck = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.KEY, DbHandler.E_TAG + "='" + strEtagCloud + "'");
                        string strCheck = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.KEY, new string[] { DbHandler.E_TAG }, new string[] { strEtagCloud }, new DbType[] { DbType.String });
                        if (strCheck.Trim().Length != 0)
                        {
                            File.Copy(BasicInfo.SyncDirPath + "\\" + strCheck, strPath);
                            fileFolderInfo.ETag = strEtagCloud;
                            bRet = true;
                        }
                        else
                        {
                            bRet = cMezeoFileCloud.DownloadFile(nsResult.StrContentsUri + '/' + nsResult.StrName, strPath, nsResult.dblSizeInBytes, ref nStatusCode);
                            if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
                            {
                                return nStatusCode;
                            }
                            else if (nStatusCode != ResponseCode.DOWNLOADFILE)
                            {
                                return nStatusCode;
                            }
                        }
                    }
                }

                if (bRet)
                {
                    if (fileFolderInfo.ETag == null)
                    {
                        fileFolderInfo.ETag = "";
                    }

                    if (fileFolderInfo.ETag.Trim().Length == 0)
                        fileFolderInfo.ETag = cMezeoFileCloud.GetETag(nsResult.StrContentsUri, ref nStatusCode);

                    if (fileFolderInfo.ETag == null)
                    {
                        fileFolderInfo.ETag = "";
                    }
                    if (fileFolderInfo.MimeType == null)
                    {
                        fileFolderInfo.MimeType = "";
                    }

                    dbHandler.Write(fileFolderInfo);
                }
                nStatus = 1;
                //LogWrapper.LogMessage("frmSyncManager - UpdateFromNQ - ", nqDetail.StrEvent + " - " + strKey + " - Leave"); 
            }

            //LogWrapper.LogMessage("frmSyncManager - UpdateFromNQ - ", "Leave");

            return nStatus;
        }

        private bool nqEventCdmiDelete(string strPath, string strKey)
        {
            //LogWrapper.LogMessage("frmSyncManager - nqEventCdmiDelete", "enter");
            bool success = true;
            bool isDirectory = false;
            bool isFile = File.Exists(strPath);
            if (!isFile)
            {
                isDirectory = Directory.Exists(strPath);
                if (isDirectory)
                {
                    DirectoryInfo rootDir = new DirectoryInfo(strPath);
                    WalkDirectoryTreeForDelete(rootDir);
                    success = SendToRecycleBin(strPath, false);
                    //Directory.Delete(strPath);
                    dbHandler.Delete(DbHandler.TABLE_NAME, DbHandler.KEY, strKey);
                }
            }
            else
            {
                success = SendToRecycleBin(strPath, true);
                //File.Delete(strPath);
                dbHandler.Delete(DbHandler.TABLE_NAME, DbHandler.KEY, strKey);
            }
            //LogWrapper.LogMessage("frmSyncManager - nqEventCdmiDelete", "leave");
            return success;
        }

        private int nqEventCdmiCreate(NQDetails nqDetail, NSResult nsResult, string strKey, string strPath)
        {
            //LogWrapper.LogMessage("frmSyncManager - nqEventCdmiCreate", "enter");
            //bool bIssuccess = false;
            int nStatus = 0;
            FileFolderInfo fileFolderInfo = new FileFolderInfo();

            fileFolderInfo.IsPublic = nsResult.bPublic;
            fileFolderInfo.IsShared = nsResult.bShared;
            fileFolderInfo.ContentUrl = nsResult.StrContentsUri;
            fileFolderInfo.CreatedDate = nsResult.dtCreated;
            fileFolderInfo.FileName = nsResult.StrName;
            fileFolderInfo.FileSize = nsResult.dblSizeInBytes;
            fileFolderInfo.MimeType = nsResult.StrMimeType;
            fileFolderInfo.ModifiedDate = nsResult.dtModified;
            fileFolderInfo.ParentUrl = nsResult.StrParentUri;
            fileFolderInfo.Status = DB_STATUS_IN_PROGRESS;
            fileFolderInfo.Type = nsResult.StrType;
            fileFolderInfo.Key = strKey;

            int lastSepIndex = strKey.LastIndexOf("\\");
            string parentDirPath = "";

            if (lastSepIndex != -1)
            {
                parentDirPath = strKey.Substring(0, strKey.LastIndexOf("\\"));
                parentDirPath = parentDirPath.Substring(parentDirPath.LastIndexOf("\\") + 1);
            }

            fileFolderInfo.ParentDir = parentDirPath;

            if (fileFolderInfo.ETag == null) { fileFolderInfo.ETag = ""; }
            if (fileFolderInfo.MimeType == null) { fileFolderInfo.MimeType = ""; }

            dbHandler.Write(fileFolderInfo);

            bool bRet = false;
            string strEtag = "";
            int refCode = 0;
            int nStatusCode = 0;

            if (nqDetail.StrObjectType == "FILE")
            {
                MarkParentsStatus(strPath, DB_STATUS_IN_PROGRESS);
                bRet = cMezeoFileCloud.DownloadFile(nsResult.StrContentsUri + '/' + nsResult.StrName, strPath,nsResult.dblSizeInBytes, ref nStatusCode);
                if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
                {
                    return nStatusCode;
                }
                else if (nStatusCode != ResponseCode.DOWNLOADFILE)
                {
                    return nStatusCode;
                }
                strEtag = cMezeoFileCloud.GetETag(nsResult.StrContentsUri, ref refCode);
                if (refCode == ResponseCode.LOGINFAILED1 || refCode == ResponseCode.LOGINFAILED2)
                {
                    return refCode;
                }
                else if (refCode != ResponseCode.GETETAG)
                {
                    return refCode;
                }
            }
            else
            {
                MarkParentsStatus(strPath, DB_STATUS_IN_PROGRESS);
                Directory.CreateDirectory(strPath);

                strEtag = cMezeoFileCloud.GetETag(nsResult.StrContentsUri, ref refCode);
                if (refCode == ResponseCode.LOGINFAILED1 || refCode == ResponseCode.LOGINFAILED2)
                {
                    return refCode;
                }
                else if (refCode != ResponseCode.GETETAG)
                {
                    return refCode;
                }

                bRet = true;
            }

            if (bRet)
            {
                MarkParentsStatus(strPath, DB_STATUS_SUCCESS);
                if (nqDetail.StrObjectType == "DIRECTORY")
                {
                    DirectoryInfo dInfo = new DirectoryInfo(strPath);
                    dbHandler.UpdateModifiedDate(dInfo.LastWriteTime, fileFolderInfo.Key);
                    dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.E_TAG , strEtag , DbHandler.KEY , fileFolderInfo.Key );
                    dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.STATUS ,"SUCCESS", DbHandler.KEY ,fileFolderInfo.Key);
                }
                else
                {
                    FileInfo fInfo = new FileInfo(strPath);
                    dbHandler.UpdateModifiedDate(fInfo.LastWriteTime, fileFolderInfo.Key);
                    dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.E_TAG , strEtag , DbHandler.KEY , fileFolderInfo.Key );
                    dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.STATUS , "SUCCESS", DbHandler.KEY ,fileFolderInfo.Key );
                    nStatus = 1;
                }
            }

            if (nqDetail.StrObjectType == "DIRECTORY")
            {
                ItemDetails[] iDetails = cMezeoFileCloud.DownloadItemDetails(nsResult.StrContentsUri, ref nStatusCode, null);
                if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
                {
                    return nStatusCode;
                }
                else if (nStatusCode == ResponseCode.INTERNAL_SERVER_ERROR)
                {
                    // Don't do anything, just keep on chugging.
                }
                else if (nStatusCode != ResponseCode.DOWNLOADITEMDETAILS)
                {
                    return nStatusCode;
                }
                if (iDetails != null)
                {
                    for (int num = 0; num < iDetails[0].nTotalItem; num++)
                    {
                        nStatus = DownloadFolderStructureForNQ(iDetails[num], strKey);
                        if (nStatus == ResponseCode.LOGINFAILED1 || nStatus == ResponseCode.LOGINFAILED2)
                        {
                            return nStatus;
                        }
                        else if (nStatus != ResponseCode.GETETAG && nStatus != ResponseCode.DOWNLOADFILE && nStatus != ResponseCode.DOWNLOADITEMDETAILS && nStatus != 1)
                        {
                            return nStatus;
                        }
                    }
                }
                else
                    nStatus = 1;
            }

            //LogWrapper.LogMessage("frmSyncManager - nqEventCdmiCreate", "leave");
            return nStatus;
        }

        private void CheckAndCreateForParentDir(string strKey)
        {
            string strpath = BasicInfo.SyncDirPath + "\\" + strKey;

            bool bIsDir = Directory.Exists(strpath);
            if (!bIsDir)
            {
                if (strKey.LastIndexOf("\\") != -1)
                    CheckAndCreateForParentDir(strKey.Substring(0, strKey.LastIndexOf("\\")));
                Directory.CreateDirectory(strpath);
            }
        }

        private int DownloadFolderStructureForNQ(ItemDetails iDetail,string strParentKey)
        {
            //LogWrapper.LogMessage("frmSyncManager - DownloadFolderStructureForNQ", "enter");
            //bool bIssuccess = false;
            int nStatus = 0;
            FileFolderInfo fileFolderInfo = new FileFolderInfo();

            fileFolderInfo.IsPublic = iDetail.bPublic;
            fileFolderInfo.IsShared = iDetail.bShared;
            fileFolderInfo.ContentUrl = iDetail.szContentUrl;
            fileFolderInfo.CreatedDate = iDetail.dtCreated;
            fileFolderInfo.FileName = iDetail.strName;
            fileFolderInfo.FileSize = iDetail.dblSizeInBytes;
            fileFolderInfo.MimeType = iDetail.szMimeType;
            fileFolderInfo.ModifiedDate = iDetail.dtModified;
            fileFolderInfo.ParentUrl = iDetail.szParentUrl;
            fileFolderInfo.Status = DB_STATUS_IN_PROGRESS;
            fileFolderInfo.Type = iDetail.szItemType;
            fileFolderInfo.Key = strParentKey + "\\" + iDetail.strName;

            string strPath = BasicInfo.SyncDirPath + "\\" + fileFolderInfo.Key;

            int nStatusCode = 0;
            bool bRet = false;
            int lastSepIndex = fileFolderInfo.Key.LastIndexOf("\\");
            string parentDirPath = "";

            if (lastSepIndex != -1)
            {
                parentDirPath = fileFolderInfo.Key.Substring(0, fileFolderInfo.Key.LastIndexOf("\\"));
                parentDirPath = parentDirPath.Substring(parentDirPath.LastIndexOf("\\") + 1);
            }

            fileFolderInfo.ParentDir = parentDirPath;

            if (fileFolderInfo.ETag == null) { fileFolderInfo.ETag = ""; }
            if (fileFolderInfo.MimeType == null) { fileFolderInfo.MimeType = ""; }

            dbHandler.Write(fileFolderInfo);

            string strEtag = "";
            int refCode = 0;

            if (iDetail.szItemType == "FILE")
            {
                MarkParentsStatus(strPath, DB_STATUS_IN_PROGRESS);
                bRet = cMezeoFileCloud.DownloadFile(iDetail.szContentUrl + '/' + iDetail.strName, strPath,iDetail.dblSizeInBytes, ref nStatusCode);
                if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
                {
                    return nStatusCode;
                }
                else if (nStatusCode != ResponseCode.DOWNLOADFILE)
                {
                    return nStatusCode;
                }
                if (bRet)
                {
                    strEtag = cMezeoFileCloud.GetETag(iDetail.szContentUrl, ref refCode);
                    if (refCode == ResponseCode.LOGINFAILED1 || refCode == ResponseCode.LOGINFAILED2)
                    {
                        return refCode;
                    }
                    else if (refCode != ResponseCode.GETETAG)
                    {
                        return refCode;
                    }
                    FileInfo fInfo = new FileInfo(strPath);
                    dbHandler.UpdateModifiedDate(fInfo.LastWriteTime, fileFolderInfo.Key);
                    dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.E_TAG , strEtag , DbHandler.KEY , fileFolderInfo.Key );
                    dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.STATUS , "SUCCESS", DbHandler.KEY ,fileFolderInfo.Key );
                }
                MarkParentsStatus(strPath, DB_STATUS_SUCCESS);
                nStatus = 1;
            }
            else
            {
                MarkParentsStatus(strPath, DB_STATUS_IN_PROGRESS);
                Directory.CreateDirectory(strPath);

                strEtag = cMezeoFileCloud.GetETag(iDetail.szContentUrl, ref refCode);
                if (refCode == ResponseCode.LOGINFAILED1 || refCode == ResponseCode.LOGINFAILED2)
                {
                    return refCode;
                }
                else if (refCode != ResponseCode.GETETAG)
                {
                    return refCode;
                }

                DirectoryInfo dInfo = new DirectoryInfo(strPath);
                dbHandler.UpdateModifiedDate(dInfo.LastWriteTime, fileFolderInfo.Key);
                dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.E_TAG , strEtag , DbHandler.KEY , fileFolderInfo.Key );
                dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.STATUS , "SUCCESS", DbHandler.KEY ,fileFolderInfo.Key );

                MarkParentsStatus(strPath, DB_STATUS_SUCCESS);

                ItemDetails[] iDetails = cMezeoFileCloud.DownloadItemDetails(iDetail.szContentUrl, ref nStatusCode, null);
                if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
                {
                    return nStatusCode;
                }
                else if (nStatusCode == ResponseCode.INTERNAL_SERVER_ERROR)
                {
                    // Don't do anything, just keep on chugging.
                }
                else if (nStatusCode != ResponseCode.DOWNLOADITEMDETAILS)
                {
                    return nStatusCode;
                }
                if (iDetails != null)
                {
                    for (int num = 0; num < iDetails[0].nTotalItem; num++)
                    {
                        nStatus = DownloadFolderStructureForNQ(iDetails[num], fileFolderInfo.Key);
                        if (nStatus == ResponseCode.LOGINFAILED1 || nStatus == ResponseCode.LOGINFAILED2)
                        {
                            return nStatus;
                        }
                        else if (nStatus != ResponseCode.GETETAG && nStatus != ResponseCode.DOWNLOADFILE && nStatus != ResponseCode.DOWNLOADITEMDETAILS)
                        {
                            return nStatus;
                        }
                    }
                }
                else
                    nStatus = 1;
            }

            //LogWrapper.LogMessage("frmSyncManager - DownloadFolderStructureForNQ", "leave");
            return nStatus;
        }

        private string FormatSizeString(double dblSize)
        {
	        int n = 0;
	        double dblUsage = ((dblSize / 1000) / 1000)/ 1000 ;
	        if(dblUsage > 0 && dblUsage < 1)
	        {
		        n++;
		        dblUsage = (dblSize / 1000) / 1000;
		        if(dblUsage > 0 && dblUsage < 1)
		        {
			        n++;
			        dblUsage = (dblSize / 1000);
			        if(dblUsage > 0 && dblUsage < 1)
			        {
				        n++;
				        dblUsage = dblSize;
			        }
		        }
	        }

            dblUsage = Math.Truncate(dblUsage * 100) / 100;
            string strDesc = string.Format("{0:N2}", dblUsage);

	        switch(n)
	        {
	        case 0:
		        strDesc += " GB";
		        break;
	        case 1:
		        strDesc += " MB";
		        break;
	        case 2:
		        strDesc += " KB";
		        break;
	        case 3:
		        strDesc += " Bytes";
		        break;
	        default:
		        break;
	        }

            return strDesc;
        }

        private string GetUsageString()
        {
            int nStatusCode = 0;
            
            double usageSize = cMezeoFileCloud.GetStorageUsed(BasicInfo.ServiceUrl + "/v2", ref nStatusCode);
            if(usageSize == 0)
                usageSize = cLoginDetails.dblStorage_Used;

            string usedSize = FormatSizeString(usageSize);
            string allocatedSize = "";

            if (cLoginDetails.dblStorage_Allocated == -1)
            {
                allocatedSize = LanguageTranslator.GetValue("SyncManagerUsageUnlimited");
            }
            else
            {
                allocatedSize = FormatSizeString(cLoginDetails.dblStorage_Allocated);
            }

            allocatedSize += " " + LanguageTranslator.GetValue("SyncManagerUsageUsed");

            return usedSize + " " + LanguageTranslator.GetValue("SyncManagerUsageOfLabel") + " " + allocatedSize;
        }

        public string GetParentURI(string strPath)
        {
            if (strPath.IndexOf("\\") == -1)
                return cLoginDetails.szContainerContentsUri;

            strPath = strPath.Substring(0, (strPath.LastIndexOf("\\")));
            return GetContentURI(strPath);
        }

        public string GetContentURI(string strPath)
        {
            return dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.CONTENT_URL, new string[] { DbHandler.KEY }, new string[] { strPath }, new DbType[] { DbType.String });
        }

        public string GetETag(string strPath)
        {
            return dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.E_TAG, new string[] { DbHandler.KEY }, new string[] { strPath }, new DbType[] { DbType.String });
        }

        private int CheckForModifyEvent(LocalEvents lEvent)
        {
            LogWrapper.LogMessage("frmSyncManager - CheckForModifyEvent", "enter");
            string strCheck = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.CONTENT_URL, new string[] { DbHandler.KEY }, new string[] { lEvent.FileName }, new DbType[] { DbType.String });
            if (strCheck.Trim().Length == 0)
            {
                LogWrapper.LogMessage("frmSyncManager - CheckForModifyEvent", "leave");
                return 2;
            }
            else
            {
                if (false == File.Exists(lEvent.FullPath))
                {
                    LogWrapper.LogMessage("frmSyncManager - CheckForModifyEvent", "leave - physical file " + lEvent.FullPath + " no longer exists.");
                    return 0;
                }

                DateTime DBModTime = dbHandler.GetDateTime(DbHandler.TABLE_NAME, DbHandler.MODIFIED_DATE, DbHandler.KEY, lEvent.FileName);

                DateTime ActualModTime = File.GetLastWriteTime(lEvent.FullPath);
                ActualModTime = ActualModTime.AddMilliseconds(-ActualModTime.Millisecond);
                TimeSpan diff = ActualModTime - DBModTime;
                if (ActualModTime < DBModTime)
                    diff = DBModTime - ActualModTime;

                if (diff >= TimeSpan.FromSeconds(1) || diff.CompareTo(TimeSpan.Zero) < 0)
                {
                    LogWrapper.LogMessage("frmSyncManager - CheckForModifyEvent", "leave");
                    return 1;
                }
                else
                {
                    LogWrapper.LogMessage("frmSyncManager - CheckForModifyEvent", "leave");
                    return 0;
                }
            }
        }

        protected virtual bool IsFileLocked(FileInfo file)
        {
            FileStream stream = null;

            try
            {
                if (File.Exists(file.FullName))
                    stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                else
                    return false;
            }
            catch (IOException)
            {
                //the file is unavailable because it is:
                //still being written to
                //or being processed by another thread
                //or does not exist (has already been processed)
                return true;
            }
            catch (Exception)
            {
                return File.Exists(file.FullName);
            }
            finally
            {
                if (stream != null)
                    stream.Close();
            }

            //file is not locked
            return false;
        }

        private void MarkParentsStatus(string path, string status)
        {
            string syncPath = path.Substring(BasicInfo.SyncDirPath.Length + 1);
            ChangeParentStatus(syncPath, status);
        }

        private void ChangeParentStatus(string syncPath, string status)
        {
            int sepIndex = syncPath.LastIndexOf("\\");

            if (sepIndex != -1)
            {
                string parentKey = syncPath.Substring(0, sepIndex);
                dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.STATUS, status, DbHandler.KEY, parentKey);
                ChangeParentStatus(parentKey, status);
            }
        }

        private string CheckAndCreateForEventsParentDir(string strKeyEvent)
        {
            string strKey = strKeyEvent.Substring(0, strKeyEvent.LastIndexOf("\\"));

            //bool bIsDir = Directory.Exists(strpath);
            string strCheck = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.CONTENT_URL, new string[] { DbHandler.KEY }, new string[] { strKey }, new DbType[] { DbType.String });
            if (strCheck.Trim().Length == 0)
            {
                string strpath = BasicInfo.SyncDirPath + "\\" + strKey;
                LocalEvents levent = new LocalEvents();
                levent.FullPath = strpath;
                levent.FileName = strKey;
                levent.EventType = LocalEvents.EventsType.FILE_ACTION_ADDED;

                AddInDBForAdded(levent);

                string strUrl = "";
                int nStatusCode = 0;

                string strParentURi = GetParentURI(levent.FileName);
                string folderName = levent.FullPath.Substring((levent.FullPath.LastIndexOf("\\") + 1));
                strUrl = cMezeoFileCloud.NewContainer(folderName, strParentURi, ref nStatusCode);
                strUrl += "/contents";

                if ((strUrl.Trim().Length != 0) && (nStatusCode == 201))
                {
                    UpdateDBForAddedSuccess(strUrl, levent);

                    dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.PARENT_URL, strUrl, DbHandler.KEY, strKeyEvent);
                }

                return strUrl;
            }

            return "";
        }

        /* 250MB Need to keep this function for future reference */
        //Adding function to put limit on file upload 
        private bool checkFileTooLarge(string filePath)
        {
            //try
            //{
            //    FileInfo iFileDetails = new FileInfo(filePath);
            //    long fileSize;
            //    if (File.Exists(filePath))
            //    {
            //        fileSize = iFileDetails.Length;
            //        if (fileSize >= 250 * 1000 * 1000)
            //            return true;
            //    }
            //}
            //catch(Exception ex)
            //{
            //}
            return false;
        }

        public void SetIssueFound(bool bIsIssueFound)
        {
            //LogWrapper.LogMessage("frmSyncManager - SetIssueFound", "enter");
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    this.btnIssuesFound.Visible = bIsIssueFound;
                });
            }
            else
            {
                this.btnIssuesFound.Visible = bIsIssueFound;
            }
            //LogWrapper.LogMessage("frmSyncManager - SetIssueFound", "leave");
        }

        private void ReportConflict(LocalEvents lEvent, IssueFound.ConflictType cType)
        {
            //LogWrapper.LogMessage("SyncManager - ReportConflict", "Enter");
            FileInfo fInfo = new FileInfo(lEvent.FullPath);

            IssueFound iFound = new IssueFound();

            iFound.LocalFilePath = lEvent.FullPath;
            iFound.LocalIssueDT = fInfo.LastWriteTime;
            iFound.LocalSize = FormatSizeString(fInfo.Length);
            iFound.ConflictTimeStamp = DateTime.Now;
            iFound.cType = cType;

            string Description = "";
            switch (cType)
            {
                case IssueFound.ConflictType.CONFLICT_MODIFIED:
                    {
                        iFound.IssueTitle = LanguageTranslator.GetValue("ConflictDetectedModified");

                        Description += LanguageTranslator.GetValue("ErrorBlurbConflict1");
                        Description += LanguageTranslator.GetValue("ErrorBlurbConflict2") + "\n";
                        Description += LanguageTranslator.GetValue("ErrorBlurbConflict3") + "\n";
                        Description += LanguageTranslator.GetValue("ErrorBlurbConflict4");
                        Description += LanguageTranslator.GetValue("ErrorBlurbConflict5");

                        iFound.IssueDescripation = Description;
                    }
                    break;
                case IssueFound.ConflictType.CONFLICT_UPLOAD:
                    {
                        iFound.IssueTitle = LanguageTranslator.GetValue("ConflictDetectedError");

                        Description += LanguageTranslator.GetValue("ErrorBlurbUpload1");
                        Description += LanguageTranslator.GetValue("ErrorBlurbUpload2");
                        Description += LanguageTranslator.GetValue("ErrorBlurbUpload3");

                        iFound.IssueDescripation = Description;
                    }
                    break;
            }

            // cMezeoFileCloud.AppEventViewer(AboutBox.AssemblyTitle, Description, 3);

            int nStatusCode = 0;
            string strContentURi = GetContentURI(lEvent.FileName);
            if (strContentURi.Substring(strContentURi.Length - 9).Equals("/contents") ||
               strContentURi.Substring(strContentURi.Length - 8).Equals("/content"))
            {
                strContentURi = strContentURi.Substring(0, strContentURi.LastIndexOf("/"));
            }

            ItemDetails iDetails = cMezeoFileCloud.GetContinerResult(strContentURi, ref nStatusCode);

            iFound.ServerSize = FormatSizeString(iDetails.dblSizeInBytes);
            iFound.ServerIssueDT = iDetails.dtModified;
            iFound.ServerFileInfo = lEvent.FileName;
            iFound.ServerFileUri = iDetails.szContentUrl;

           // frmIssuesFound.AddIssueToList(iFound);
            dbHandler.StoreConflict(iFound);
            // Issue Fix for Conflicts 
            IssueFoundBalloonMessage();

            //LogWrapper.LogMessage("SyncManager - ReportConflict", "Leave");
        }

        private bool CheckForConflicts(LocalEvents lEvent, string strContentUrl)
        {
            //LogWrapper.LogMessage("SyncManager - CheckForConflicts", "Enter, content uri " + strContentUrl);
            int nStatusCode = 0;
            bool bRet = false;
            string strEtag;
            switch (lEvent.EventType)
            {
                case LocalEvents.EventsType.FILE_ACTION_MODIFIED:
                    {
                        strEtag = cMezeoFileCloud.GetETag(strContentUrl, ref nStatusCode);
                        string strDBETag = GetETag(lEvent.FileName);
                        if (strEtag.Trim().Length != 0)
                        {
                            if (strEtag != strDBETag)
                            {
                                ReportConflict(lEvent, IssueFound.ConflictType.CONFLICT_MODIFIED);
                                return true;
                            }
                            else
                            {
                                string URL = "";
                                if (strContentUrl.Substring(strContentUrl.Length - 9).Equals("/contents") ||
                                    strContentUrl.Substring(strContentUrl.Length - 8).Equals("/content"))
                                {
                                    URL = strContentUrl.Substring(0, strContentUrl.LastIndexOf("/"));
                                }

                                ItemDetails IDetails = cMezeoFileCloud.GetContinerResult(URL, ref nStatusCode);
                                string FileName = lEvent.FileName.Substring((lEvent.FileName.LastIndexOf("\\") + 1));
                                if (FileName != IDetails.strName)
                                {
                                    string FileReName = lEvent.FullPath.Substring(0, lEvent.FullPath.LastIndexOf("\\") + 1);
                                    FileReName += IDetails.strName;

                                    bRet = cMezeoFileCloud.OverWriteFile(lEvent.FullPath, strContentUrl, ref nStatusCode);
                                    if (bRet)
                                    {
                                        UpdateDBForModifiedSuccess(lEvent, strContentUrl);
                                    }

                                    if (File.Exists(lEvent.FullPath))
                                        File.Move(lEvent.FullPath, FileReName);

                                    lEvent.OldFullPath = lEvent.FullPath;
                                    lEvent.FullPath = FileReName;
                                    lEvent.OldFileName = lEvent.FileName;
                                    lEvent.FileName = lEvent.FileName.Substring(0, lEvent.FileName.LastIndexOf("\\") + 1);
                                    lEvent.FileName += IDetails.strName;

                                    dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.KEY, lEvent.FileName, DbHandler.KEY, lEvent.OldFileName);
                                    UpdateDBForStatus(lEvent, DB_STATUS_SUCCESS);

                                    return false;
                                }
                                else
                                    return true;
                            }
                        }
                        else
                        {
                            string strParentURL = GetParentURI(lEvent.FileName);
                            string strURL = cMezeoFileCloud.UploadingFile(lEvent.FullPath, strParentURL, ref nStatusCode);
                            string strEtagNew = cMezeoFileCloud.GetETag(strURL, ref nStatusCode);

                            dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.CONTENT_URL, strURL, DbHandler.KEY, lEvent.FileName);
                            dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.E_TAG, strEtagNew, DbHandler.KEY, lEvent.FileName);

                            return false;
                        }
                    }
                // break;
                case LocalEvents.EventsType.FILE_ACTION_REMOVED:
                    {
                        strEtag = cMezeoFileCloud.GetETag(strContentUrl, ref nStatusCode);
                        string strDBETag = GetETag(lEvent.FileName);
                        string FileName = lEvent.FileName.Substring((lEvent.FileName.LastIndexOf("\\") + 1));

                        string URL = "";
                        if (strContentUrl.Substring(strContentUrl.Length - 9).Equals("/contents") ||
                            strContentUrl.Substring(strContentUrl.Length - 8).Equals("/content"))
                        {
                            URL = strContentUrl.Substring(0, strContentUrl.LastIndexOf("/"));
                        }

                        ItemDetails IDetails = cMezeoFileCloud.GetContinerResult(URL, ref nStatusCode);

                        if (strEtag.Trim().Length != 0)
                        {
                            if ((strEtag != strDBETag) || (FileName != IDetails.strName))
                            {
                                lEvent.FullPath = lEvent.FullPath.Substring(0, lEvent.FullPath.LastIndexOf("\\") + 1);
                                lEvent.FullPath += IDetails.strName;

                                bRet = cMezeoFileCloud.DownloadFile(strContentUrl + "/" + IDetails.strName, lEvent.FullPath, IDetails.dblSizeInBytes, ref nStatusCode);

                                if (bRet)
                                {
                                    UpdateDBForRemoveSuccess(lEvent);
                                    if (FileName != IDetails.strName)
                                    {
                                        lEvent.FileName = lEvent.FileName.Substring(0, lEvent.FileName.LastIndexOf("\\") + 1);
                                        lEvent.FileName += IDetails.strName;
                                    }
                                    AddInDBForAdded(lEvent);
                                    UpdateDBForAddedSuccess(strContentUrl, lEvent);

                                    return false;
                                }
                                return true;
                            }
                            return true;
                        }
                        else
                        {
                            UpdateDBForRemoveSuccess(lEvent);
                            return false;
                        }
                    }
                //  break;
                case LocalEvents.EventsType.FILE_ACTION_RENAMED:
                    {
                        strEtag = cMezeoFileCloud.GetETag(strContentUrl, ref nStatusCode);

                        // If the item doesn't exist, then it doesn't conflict.
                        if (nStatusCode == ResponseCode.NOTFOUND)
                            return true;

                        string strDBETag = GetETag(lEvent.FileName);
                        string FileName = lEvent.FileName.Substring((lEvent.FileName.LastIndexOf("\\") + 1));

                        string URL = "";
                        if (strContentUrl.Substring(strContentUrl.Length - 9).Equals("/contents") ||
                            strContentUrl.Substring(strContentUrl.Length - 8).Equals("/content"))
                        {
                            URL = strContentUrl.Substring(0, strContentUrl.LastIndexOf("/"));
                        }

                        ItemDetails IDetails = cMezeoFileCloud.GetContinerResult(URL, ref nStatusCode);

                        if (strEtag.Trim().Length != 0)
                        {
                            if (strEtag != strDBETag)
                            {
                                bRet = cMezeoFileCloud.DownloadFile(strContentUrl + "/" + lEvent.FileName, lEvent.FullPath, IDetails.dblSizeInBytes, ref nStatusCode);
                                if (bRet)
                                {
                                    string strEtagNew = cMezeoFileCloud.GetETag(strContentUrl, ref nStatusCode);
                                    dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.E_TAG, strEtagNew, DbHandler.KEY, lEvent.FileName);

                                    FileInfo fInfo = new FileInfo(lEvent.FullPath);
                                    dbHandler.UpdateModifiedDate(fInfo.LastWriteTime, lEvent.FileName);
                                    return true;
                                }
                            }
                            return true;
                        }
                        else
                        {
                            string strParentUri = GetParentURI(lEvent.FileName);
                            string strUri = cMezeoFileCloud.UploadingFile(lEvent.FullPath, strParentUri, ref nStatusCode);
                            if (strUri.Trim().Length != 0)
                            {
                                dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.KEY, lEvent.FileName, DbHandler.KEY, lEvent.OldFileName);
                                dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.CONTENT_URL, strUri, DbHandler.KEY, lEvent.FileName);

                                string strEtagUpload = cMezeoFileCloud.GetETag(strUri, ref nStatusCode);

                                dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.E_TAG, strEtagUpload, DbHandler.KEY, lEvent.FileName);

                                UpdateDBForStatus(lEvent, DB_STATUS_SUCCESS);
                                return false;
                            }
                            return true;
                        }
                    }
                //   break;
            }

            //LogWrapper.LogMessage("SyncManager - CheckForConflicts", "Leave");
            return true;
        }

        void queue_WatchCompletedEvent()
        {
            //LogWrapper.LogMessage("frmSyncManager - queue_WatchCompletedEvent", "enter");
            if (IsInIdleState() && EventQueue.QueueNotEmpty() && !BasicInfo.IsInitialSync && !CanNotTalkToServer())
            {
                if (!bwSyncThread.IsBusy)
                    bwSyncThread.RunWorkerAsync();
            }
            //LogWrapper.LogMessage("frmSyncManager - queue_WatchCompletedEvent", "leave");
        }

        private int HandleEvent(BackgroundWorker caller, LocalEvents localEvent)
        {
            int returnCode = 1;
            LogWrapper.LogMessage("frmSyncManager - HandleEvent", "Enter");

            if (null == localEvent)
            {
                LogWrapper.LogMessage("frmSyncManager - HandleEvent", "Leave (localEvent was null)");
                return returnCode;
            }

            bool RemoveIndexes = false;

            if (caller.CancellationPending)
            {
                LogWrapper.LogMessage("frmSyncManager - HandleEvent ", "Cancelled called");
                caller.CancelAsync();
                return USER_CANCELLED;
            }

            bool bRet = true;

            LogWrapper.LogMessage("frmSyncManager - HandleEvent - localEvent - ", localEvent.FullPath);

            FileAttributes attr = localEvent.Attributes;

            bool isDirectory = localEvent.IsDirectory;
            bool isFile = localEvent.IsFile;
            if (!isFile && !isDirectory)
            {
                // An optimization to keep from making disk calls all the time.
                isFile = File.Exists(localEvent.FullPath);
                if (!isFile)
                    isDirectory = Directory.Exists(localEvent.FullPath);
                if (isFile || isDirectory)
                    attr = File.GetAttributes(localEvent.FullPath);
            }

            if (!isFile && !isDirectory)
            {
                if ((localEvent.EventType != LocalEvents.EventsType.FILE_ACTION_REMOVED) && (localEvent.EventType != LocalEvents.EventsType.FILE_ACTION_RENAMED) && (localEvent.EventType != LocalEvents.EventsType.FILE_ACTION_MOVE))
                {
                    dbHandler.DeleteEvent(localEvent.EventDbId);
                    return 1;
                }
            }
            else if (isFile)
            {
                // Make sure the attributes are up to date.
                FileInfo fileInfo = new FileInfo(localEvent.FullPath);
                if (fileInfo.Exists)
                    attr = fileInfo.Attributes;
            }

            if (localEvent.EventType == LocalEvents.EventsType.FILE_ACTION_MODIFIED)
            {
                LogWrapper.LogMessage("frmSyncManager - HandleEvent - localEvent - ", localEvent.FullPath + " - " + localEvent.EventType.ToString() + " - Enter");

                if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                    bRet = false;
                else
                {
                    int nRet = CheckForModifyEvent(localEvent);
                    if (nRet == 0)
                        bRet = false;
                    else if (nRet == 1)
                        bRet = true;
                    else if (nRet == 2)
                    {
                        // If the event was queued up as a MODIFY instead
                        // of an ADDED by mistake, then change it back.
                        LogWrapper.LogMessage("frmSyncManager - HandleEvent - localEvent - ", localEvent.FullPath + " - Changed from FILE_ACTION_MODIFIED to " + localEvent.EventType.ToString());
                        localEvent.EventType = LocalEvents.EventsType.FILE_ACTION_ADDED;
                        bRet = false;
                    }
                }
                LogWrapper.LogMessage("frmSyncManager - HandleEvent - localEvent - ", localEvent.FullPath + " - " + localEvent.EventType.ToString() + " - Leave");
            }

            if (localEvent.EventType == LocalEvents.EventsType.FILE_ACTION_ADDED || localEvent.EventType == LocalEvents.EventsType.FILE_ACTION_RENAMED)
            {
                LogWrapper.LogMessage("frmSyncManager - HandleEvent - localEvent - ", localEvent.FullPath + " - " + localEvent.EventType.ToString() + " - Enter");

                string strCheck = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.CONTENT_URL, new string[] { DbHandler.KEY, DbHandler.STATUS }, new string[] { localEvent.FileName, DB_STATUS_SUCCESS }, new DbType[] { DbType.String, DbType.String });

                if (strCheck.Trim().Length == 0)
                    bRet = true;
                else
                    bRet = false;

                LogWrapper.LogMessage("frmSyncManager - HandleEvent - localEvent - ", localEvent.FullPath + " - " + localEvent.EventType.ToString() + " - Leave");
            }

            if (localEvent.EventType == LocalEvents.EventsType.FILE_ACTION_RENAMED)
            {
                LogWrapper.LogMessage("frmSyncManager - HandleEvent - localEvent - ", localEvent.FullPath + " - " + localEvent.EventType.ToString() + " - Enter");

                string strCheck = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.CONTENT_URL, new string[] { DbHandler.KEY }, new string[] { localEvent.OldFileName }, new DbType[] { DbType.String });
                if (strCheck.Trim().Length == 0)
                {
                    string strCheck1 = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.CONTENT_URL, new string[] { DbHandler.KEY, DbHandler.STATUS }, new string[] { localEvent.FileName, DB_STATUS_SUCCESS }, new DbType[] { DbType.String, DbType.String });
                    if (strCheck1.Trim().Length == 0)
                    {
                        localEvent.EventType = LocalEvents.EventsType.FILE_ACTION_ADDED;
                    }
                }

                LogWrapper.LogMessage("frmSyncManager - HandleEvent - localEvent - ", localEvent.FullPath + " - " + localEvent.EventType.ToString() + " - Leave");
            }

            if (localEvent.EventType == LocalEvents.EventsType.FILE_ACTION_REMOVED)
            {
                LogWrapper.LogMessage("frmSyncManager - HandleEvent - localEvent - ", localEvent.FullPath + " - " + localEvent.EventType.ToString() + " - Enter");

                string strCheck = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.CONTENT_URL, new string[] { DbHandler.KEY }, new string[] { localEvent.FileName }, new DbType[] { DbType.String });
                if (strCheck.Trim().Length == 0)
                    bRet = false;
                else
                    bRet = true;

                LogWrapper.LogMessage("frmSyncManager - HandleEvent - localEvent - ", localEvent.FullPath + " - " + localEvent.EventType.ToString() + " - Leave");
            }

            if ((attr & FileAttributes.Hidden) == FileAttributes.Hidden || (attr & FileAttributes.Temporary) == FileAttributes.Temporary)
                bRet = false;

            if (bRet)
            {
                LogWrapper.LogMessage("frmSyncManager - HandleEvent - localEvent - ", localEvent.FullPath + " - AddinDB Enter");

                if (localEvent.EventType == LocalEvents.EventsType.FILE_ACTION_MODIFIED)
                {
                    UpdateDBForStatus(localEvent, DB_STATUS_IN_PROGRESS);
                }
                else if (localEvent.EventType == LocalEvents.EventsType.FILE_ACTION_ADDED)
                {
                    AddInDBForAdded(localEvent);
                }
                else if (localEvent.EventType == LocalEvents.EventsType.FILE_ACTION_RENAMED)
                {
                    AddInDBForRename(localEvent);
                }

                LogWrapper.LogMessage("frmSyncManager - HandleEvent - localEvent - ", localEvent.FullPath + " - AddinDB Leave");
            }

            if (!bRet)
            {
                if (!RemoveIndexes)
                {
                    RemoveIndexes = true;
                    if (localEvent.EventType == LocalEvents.EventsType.FILE_ACTION_REMOVED)
                    {
                        MarkParentsStatus(localEvent.FullPath, DB_STATUS_SUCCESS);
                        UpdateDBForRemoveSuccess(localEvent);
                    }
                    dbHandler.DeleteEvent(localEvent.EventDbId);
                }
            }

            if (caller != null)
            {
                caller.ReportProgress(SYNC_STARTED);
            }

            LogWrapper.LogMessage("frmSyncManager - HandleEvent", " ProcessLocalEvent Going");

            // If we didn't remove the item, then process it.
            if (!RemoveIndexes)
                returnCode = ProcessLocalEvent(caller, ref localEvent);

            LogWrapper.LogMessage("frmSyncManager - HandleEvent", " ProcessLocalEvent Exit");

            LogWrapper.LogMessage("frmSyncManager - HandleEvent", "Leave");
            return returnCode;
        }

        private int ProcessLocalEvent(BackgroundWorker caller, ref LocalEvents lEvent)
        {
            LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "Enter");
            string strUrl = "";
            bool bRetConflicts = true;
            bool wasSuccessful = false;

            if (caller != null)
            {
                caller.ReportProgress(PROCESS_LOCAL_EVENTS_STARTED, 1);
            }

            if (caller.CancellationPending)
            {
                LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "Canceled Called");
                caller.CancelAsync();
                return USER_CANCELLED;
            }

            if (caller != null)
            {
                caller.ReportProgress(PROGRESS_CHANGED_WITH_FILE_NAME, lEvent.FullPath);
            }

            FileAttributes attr = lEvent.Attributes;
            bool isDirectory = lEvent.IsDirectory;
            bool isFile = lEvent.IsFile;

            if (isFile && lEvent.EventType == LocalEvents.EventsType.FILE_ACTION_ADDED)
            {
                LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "Check for file lock - Enter");
                FileInfo fInfo = new FileInfo(lEvent.FullPath);
                bool IsLocked = IsFileLocked(fInfo);
                while (IsLocked && fInfo.Exists)
                {
                    IsLocked = IsFileLocked(fInfo);
                }
                LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "Check for file lock - Leave");
            }

            if (!isFile && !isDirectory)
            {
                attr = FileAttributes.Normal;
                if (lEvent.EventType == LocalEvents.EventsType.FILE_ACTION_RENAMED)
                {
                    isFile = lEvent.IsFile;
                    isDirectory = lEvent.IsDirectory;
                }
                else if (lEvent.EventType != LocalEvents.EventsType.FILE_ACTION_REMOVED)
                {
                    dbHandler.DeleteEvent(lEvent.EventDbId);
                    LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "SKIPPING EVENT - For the path " + lEvent.FullPath + ".  Unable to find a file/directory at the given location for the event type " + lEvent.EventType + ".");
                    return 1;
                }
            }

            int nStatusCode = 0;
            bool bRet = true;

            switch (lEvent.EventType)
            {
                case LocalEvents.EventsType.FILE_ACTION_MOVE:
                    {
                        LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "FILE_ACTION_MOVE - Enter for file path " + lEvent.FullPath);
                        string strContentURi = GetContentURI(lEvent.FileName);
                        if (strContentURi.Trim().Length == 0)
                        {
                            strContentURi = GetContentURI(lEvent.OldFileName);
                        }
                        if (strContentURi.Trim().Length == 0)
                        {
                            LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "GetContentURI for length ZERO");
                            return 1;
                        }

                        if (strContentURi.Substring(strContentURi.Length - 9).Equals("/contents") ||
                            strContentURi.Substring(strContentURi.Length - 8).Equals("/content"))
                        {
                            strContentURi = strContentURi.Substring(0, strContentURi.LastIndexOf("/"));
                        }

                        string strParentUri = GetParentURI(lEvent.FileName);
                        if (strParentUri.Trim().Length == 0)
                        {
                            strParentUri = GetParentURI(lEvent.OldFileName);
                        }
                        if (strParentUri.Trim().Length == 0)
                        {
                            LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "GetParentURI for length ZERO");
                            return 1;
                        }

                        if (strParentUri.Substring(strParentUri.Length - 9).Equals("/contents") ||
                            strParentUri.Substring(strParentUri.Length - 8).Equals("/content"))
                        {
                            strParentUri = strParentUri.Substring(0, strParentUri.LastIndexOf("/"));
                        }

                        string strName = lEvent.FullPath.Substring(lEvent.FullPath.LastIndexOf("\\") + 1);
                        ItemDetails iDetails = cMezeoFileCloud.GetContinerResult(strContentURi, ref nStatusCode);

                        if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
                        {
                            return LOGIN_FAILED;
                        }
                        else if (nStatusCode == ResponseCode.NOTFOUND)
                        {
                            LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "Remote object no longer exists.  Remove local object.");
                            nqEventCdmiDelete(lEvent.FullPath, lEvent.OldFileName);
                            return 1;
                        }
                        else if (nStatusCode != ResponseCode.GETCONTINERRESULT)
                        {
                            if (ResponseCode.NOTFOUND == nStatusCode)
                                return ITEM_NOT_FOUND;
                            return SERVER_INACCESSIBLE;
                        }

                        dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.PUBLIC, iDetails.bPublic, DbHandler.KEY, lEvent.FileName);
                        AddInDBForRename(lEvent);
                        string mimeType = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.MIMIE_TYPE, new string[] { DbHandler.KEY }, new string[] { lEvent.FileName }, new DbType[] { DbType.String });
                        if (0 == mimeType.Length)
                            mimeType = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.MIMIE_TYPE, new string[] { DbHandler.KEY }, new string[] { lEvent.OldFileName }, new DbType[] { DbType.String });

                        if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                        {
                            bRet = cMezeoFileCloud.ContainerMove(strContentURi, strName, mimeType, iDetails.bPublic, strParentUri, ref nStatusCode);
                        }
                        else
                        {
                            if (!checkFileTooLarge(lEvent.FullPath))
                                bRet = cMezeoFileCloud.FileMove(strContentURi, strName, mimeType, iDetails.bPublic, strParentUri, ref nStatusCode);
                            else
                                nStatusCode = 200;
                        }

                        if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
                        {
                            return LOGIN_FAILED;
                        }
                        else if (nStatusCode != ResponseCode.CONTAINERMOVE)
                        {
                            if (ResponseCode.NOTFOUND == nStatusCode)
                                return ITEM_NOT_FOUND;
                            return SERVER_INACCESSIBLE;
                        }
                        else
                        {
                            // Update the key for the children if this was a container.
                            if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                                dbHandler.UpdateRenamedOrMovedKey(lEvent.FileName, lEvent.OldFileName);

                            // Since the item could have a new parent, it needs to be updated.
                            // Change this for renames as well as moves....
                            UpdateParent(lEvent.FileName, lEvent.FullPath);

                            wasSuccessful = true;
                            UpdateDBForStatus(lEvent, DB_STATUS_SUCCESS);
                            MarkParentsStatus(lEvent.FullPath, DB_STATUS_SUCCESS);
                            dbHandler.DeleteEvent(lEvent.EventDbId);
                        }

                        LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "FILE_ACTION_MOVE - Leave for file path " + lEvent.FullPath);
                    }
                    break;
                case LocalEvents.EventsType.FILE_ACTION_ADDED:
                    {
                        LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "FILE_ACTION_ADDED - Enter for file path " + lEvent.FullPath);
                        MarkParentsStatus(lEvent.FullPath, DB_STATUS_IN_PROGRESS);
                        string strParentURi = GetParentURI(lEvent.FileName);
                        if (strParentURi.Trim().Length == 0)
                        {
                            LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "GetParentURI for length ZERO");
                            return 1;
                        }

                        if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                        {
                            string folderName = lEvent.FullPath.Substring((lEvent.FullPath.LastIndexOf("\\") + 1));

                            LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "Create new container for folder " + folderName);

                            // Defect 273 - If somehow, someway the same folder name exists locally and on the cloud,
                            //              do not create a new folder in the cloud with this name.  Just use the existing
                            //              container and upload files/containers into it.
                            ItemDetails[] itemDetails;
                            bool bCreateCloudContainer = true;

                            // Grab a list of files and containers for the parent.
                            itemDetails = cMezeoFileCloud.DownloadItemDetails(strParentURi, ref nStatusCode, folderName);
                            if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
                            {
                                return LOGIN_FAILED;
                            }
                            else if (nStatusCode == ResponseCode.INTERNAL_SERVER_ERROR)
                            {
                                // Don't do anything, just keep on chugging.
                            }
                            else if (nStatusCode != ResponseCode.DOWNLOADITEMDETAILS)
                            {
                                if (ResponseCode.NOTFOUND == nStatusCode)
                                    return ITEM_NOT_FOUND;
                                return SERVER_INACCESSIBLE;
                            }
                            else if (nStatusCode == ResponseCode.DOWNLOADITEMDETAILS)
                            {
                                // Look through each item for a container with the same name.
                                if (itemDetails != null)
                                {
                                    foreach (ItemDetails item in itemDetails)
                                    {
                                        if ("DIRECTORY" == item.szItemType)
                                        {
                                            if (folderName == item.strName)
                                            {
                                                // Don't create a new/duplicate folder.
                                                bCreateCloudContainer = false;
                                                // Populate the url with this folder.
                                                strUrl = item.szContentUrl;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }

                            if (bCreateCloudContainer)
                            {
                                strUrl = cMezeoFileCloud.NewContainer(folderName, strParentURi, ref nStatusCode);
                                if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
                                {
                                    return LOGIN_FAILED;
                                }
                                else if (nStatusCode != ResponseCode.NEWCONTAINER)
                                {
                                    if (ResponseCode.NOTFOUND == nStatusCode)
                                        return ITEM_NOT_FOUND;
                                    return SERVER_INACCESSIBLE;
                                }
                                else if ((strUrl.Trim().Length != 0) && (nStatusCode == ResponseCode.NEWCONTAINER))
                                {
                                    strUrl += "/contents";
                                    wasSuccessful = true;
                                    dbHandler.DeleteEvent(lEvent.EventDbId);
                                    bRet = true;

                                    string strParent = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.PARENT_URL, new string[] { DbHandler.KEY }, new string[] { lEvent.FileName }, new DbType[] { DbType.String });
                                    if (strParent.Trim().Length == 0)
                                        dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.PARENT_URL, strParentURi, DbHandler.KEY, lEvent.FileName);

                                    MarkParentsStatus(lEvent.FullPath, DB_STATUS_SUCCESS);
                                    UpdateDBForAddedSuccess(strUrl, lEvent);
                                }
                            }
                            else
                            {
                                wasSuccessful = true;
                                dbHandler.DeleteEvent(lEvent.EventDbId);
                                bRet = true;

                                string strParent = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.PARENT_URL, new string[] { DbHandler.KEY }, new string[] { lEvent.FileName }, new DbType[] { DbType.String });
                                if (strParent.Trim().Length == 0)
                                    dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.PARENT_URL, strParentURi, DbHandler.KEY, lEvent.FileName);

                                MarkParentsStatus(lEvent.FullPath, DB_STATUS_SUCCESS);
                                UpdateDBForAddedSuccess(strUrl, lEvent);
                            }

                            LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "Container URI for folder " + folderName + " is " + strUrl);
                        }
                        else
                        {
                            //if (strParentURi.Trim().Length == 0)
                            //    strParentURi = CheckAndCreateForEventsParentDir(lEvent.FileName);

                            LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "Start uploading file for " + lEvent.FullPath + ", at parent URI " + strParentURi);

                            string fileName = lEvent.FullPath.Substring((lEvent.FullPath.LastIndexOf("\\") + 1));
                            ItemDetails[] itemDetailsfile;
                            bool buploadfileToCloud = true;

                            // Grab a list of files for the parent.
                            itemDetailsfile = cMezeoFileCloud.DownloadItemDetails(strParentURi, ref nStatusCode, fileName);

                            if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
                            {
                                return LOGIN_FAILED;
                            }
                            else if (nStatusCode == ResponseCode.INTERNAL_SERVER_ERROR)
                            {
                                // Don't do anything, just keep on chugging.
                            }
                            else if (nStatusCode != ResponseCode.DOWNLOADITEMDETAILS)
                            {
                                if (ResponseCode.NOTFOUND == nStatusCode)
                                    return ITEM_NOT_FOUND;
                                return SERVER_INACCESSIBLE;
                            }
                            else if (nStatusCode == ResponseCode.DOWNLOADITEMDETAILS)
                            {
                                // Look through each item for a file with the same name.
                                if (itemDetailsfile != null)
                                {
                                    foreach (ItemDetails item in itemDetailsfile)
                                    {
                                        if ("FILE" == item.szItemType)
                                        {
                                            if (fileName == item.strName)
                                            {
                                                // Don't create a new/duplicate file.
                                                buploadfileToCloud = false;
                                                // Populate the url with this file.
                                                strUrl = item.szContentUrl;
                                                checkfoOverwrite(item, lEvent);
                                                break;
                                            }
                                        }
                                    }
                                }
                            }

                            if (buploadfileToCloud)
                            {
                                if (!checkFileTooLarge(lEvent.FullPath))
                                    strUrl = cMezeoFileCloud.UploadingFile(lEvent.FullPath, strParentURi, ref nStatusCode);
                                else
                                    nStatusCode = 201;

                                if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
                                {
                                    return LOGIN_FAILED;
                                }
                                else if (nStatusCode != ResponseCode.UPLOADINGFILE)
                                {
                                    // Apparently, the -4 from a file upload is ONLY when
                                    // the upload was interrupted/canceled by the user.
                                    if ((ResponseCode.NOTFOUND == nStatusCode) || (nStatusCode == -4))
                                        return ITEM_NOT_FOUND;
                                    return SERVER_INACCESSIBLE;
                                }
                                else if ((strUrl.Trim().Length != 0) && (nStatusCode == ResponseCode.UPLOADINGFILE))
                                {
                                    strUrl += "/content";
                                    wasSuccessful = true;
                                    dbHandler.DeleteEvent(lEvent.EventDbId);
                                    bRet = true;

                                    string strParent = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.PARENT_URL, new string[] { DbHandler.KEY }, new string[] { lEvent.FileName }, new DbType[] { DbType.String });
                                    if (strParent.Trim().Length == 0)
                                        dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.PARENT_URL, strParentURi, DbHandler.KEY, lEvent.FileName);

                                    MarkParentsStatus(lEvent.FullPath, DB_STATUS_SUCCESS);
                                    UpdateDBForAddedSuccess(strUrl, lEvent);
                                }
                            }
                            else
                            {
                                wasSuccessful = true;
                                dbHandler.DeleteEvent(lEvent.EventDbId);
                                bRet = true;

                                string strParent = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.PARENT_URL, new string[] { DbHandler.KEY }, new string[] { lEvent.FileName }, new DbType[] { DbType.String });
                                if (strParent.Trim().Length == 0)
                                    dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.PARENT_URL, strParentURi, DbHandler.KEY, lEvent.FileName);

                                MarkParentsStatus(lEvent.FullPath, DB_STATUS_SUCCESS);
                                UpdateDBForAddedSuccess(strUrl, lEvent);
                            }
                        }

                        LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "FILE_ACTION_ADDED - Leave for file path " + lEvent.FullPath);
                    }
                    break;

                case LocalEvents.EventsType.FILE_ACTION_MODIFIED:
                    {
                        LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "FILE_ACTION_MODIFIED - Enter for file path " + lEvent.FullPath);

                        // If the physical file no longer exists, then skip this event.
                        if (false == File.Exists(lEvent.FullPath))
                        {
                            LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "Physical file is no longer available.");
                            return 1;
                        }

                        MarkParentsStatus(lEvent.FullPath, DB_STATUS_IN_PROGRESS);
                        string strContentURi = GetContentURI(lEvent.FileName);
                        if (strContentURi.Trim().Length == 0)
                        {
                            LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "GetContentURI for length ZERO");
                            return 1;
                        }

                        if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                            bRet = false;
                        else
                        {
                            bRetConflicts = CheckForConflicts(lEvent, strContentURi);
                            if (bRetConflicts)
                            {
                                if (!checkFileTooLarge(lEvent.FullPath))
                                    bRet = cMezeoFileCloud.OverWriteFile(lEvent.FullPath, strContentURi, ref nStatusCode);
                                else
                                    nStatusCode = 200;

                                if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
                                {
                                    return LOGIN_FAILED;
                                }
                                else if (nStatusCode != ResponseCode.OVERWRITEFILE)
                                {
                                    if (ResponseCode.NOTFOUND == nStatusCode)
                                        return ITEM_NOT_FOUND;
                                    return SERVER_INACCESSIBLE;
                                }
                                else
                                {
                                    wasSuccessful = true;
                                    MarkParentsStatus(lEvent.FullPath, DB_STATUS_SUCCESS);
                                    UpdateDBForModifiedSuccess(lEvent, strContentURi);
                                    dbHandler.DeleteEvent(lEvent.EventDbId);
                                }
                            }
                            else
                            {
                                wasSuccessful = true;
                                MarkParentsStatus(lEvent.FullPath, DB_STATUS_SUCCESS);
                                UpdateDBForModifiedSuccess(lEvent, strContentURi);
                                dbHandler.DeleteEvent(lEvent.EventDbId);
                            }
                        }

                        LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "FILE_ACTION_MODIFIED - Leave for file path " + lEvent.FullPath);
                    }
                    break;
                case LocalEvents.EventsType.FILE_ACTION_REMOVED:
                    {
                        LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "FILE_ACTION_REMOVED - Enter for file path " + lEvent.FullPath);

                        string strContentURi = GetContentURI(lEvent.FileName);
                        if (strContentURi.Trim().Length == 0)
                        {
                            LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "GetContentURI for length ZERO");
                            return 1;
                        }

                        MarkParentsStatus(lEvent.FullPath, DB_STATUS_IN_PROGRESS);

                        if (isFile)
                            bRetConflicts = CheckForConflicts(lEvent, strContentURi);

                        if (bRetConflicts)
                        {
                            if (strContentURi.Substring(strContentURi.Length - 9).Equals("/contents") ||
                                strContentURi.Substring(strContentURi.Length - 8).Equals("/content"))
                            {
                                strContentURi = strContentURi.Substring(0, strContentURi.LastIndexOf("/"));
                            }

                            if (!checkFileTooLarge(lEvent.FullPath))
                                bRet = cMezeoFileCloud.Delete(strContentURi, ref nStatusCode, lEvent.FullPath);
                            else
                                nStatusCode = 200;

                            if (nStatusCode == ResponseCode.LOGINFAILED1)
                            {
                                return LOGIN_FAILED;
                            }
                            else if (nStatusCode == ResponseCode.NOTFOUND || nStatusCode == ResponseCode.LOGINFAILED2)
                            {
                                // If it doesn't exist on the server, then it's the same thing as far as we're concerned.
                                wasSuccessful = true;
                                MarkParentsStatus(lEvent.FullPath, DB_STATUS_SUCCESS);
                                UpdateDBForRemoveSuccess(lEvent);
                                dbHandler.DeleteEvent(lEvent.EventDbId);
                            }
                            else if (nStatusCode != ResponseCode.DELETE)
                            {
                                if (ResponseCode.NOTFOUND == nStatusCode)
                                    return ITEM_NOT_FOUND;
                                return SERVER_INACCESSIBLE;
                            }
                            else
                            {
                                wasSuccessful = true;
                                MarkParentsStatus(lEvent.FullPath, DB_STATUS_SUCCESS);
                                UpdateDBForRemoveSuccess(lEvent);
                                dbHandler.DeleteEvent(lEvent.EventDbId);
                            }
                        }

                        LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "FILE_ACTION_REMOVED - Leave for file path " + lEvent.FullPath);
                    }
                    break;
                case LocalEvents.EventsType.FILE_ACTION_RENAMED:
                    {
                        LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "case FILE_ACTION_RENAMED");
                        LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "GetContentURI for " + lEvent.FileName);

                        string strContentURi = GetContentURI(lEvent.FileName);
                        if (strContentURi.Trim().Length == 0)
                        {
                            LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "GetContentURI for length ZERO");
                            return 1;
                        }

                        LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "MarkParentsStatus DB_STATUS_IN_PROGRESS for " + lEvent.FullPath);
                        MarkParentsStatus(lEvent.FullPath, DB_STATUS_IN_PROGRESS);

                        string changedName = lEvent.FileName.Substring((lEvent.FileName.LastIndexOf("\\") + 1));
                        LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "changedName " + changedName);

                        if (isFile)
                        {
                            bRetConflicts = CheckForConflicts(lEvent, strContentURi);
                            LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "isFile bRetConflicts " + bRetConflicts.ToString());
                        }
                        if (strContentURi.Substring(strContentURi.Length - 9).Equals("/contents") ||
                            strContentURi.Substring(strContentURi.Length - 8).Equals("/content"))
                        {
                            strContentURi = strContentURi.Substring(0, strContentURi.LastIndexOf("/"));
                        }

                        if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                        {
                            bRet = cMezeoFileCloud.ContainerRename(strContentURi, changedName, ref nStatusCode);
                            //dbHandler.UpdateRenamedOrMovedKey(lEvent.FileName, lEvent.OldFileName);

                            LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "Directory bRet " + bRet.ToString());
                            if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
                            {
                                return LOGIN_FAILED;
                            }
                            else if (nStatusCode != ResponseCode.CONTAINERRENAME)
                            {
                                if (ResponseCode.NOTFOUND == nStatusCode)
                                    return ITEM_NOT_FOUND;
                                return SERVER_INACCESSIBLE;
                            }
                            else
                            {
                                wasSuccessful = true;
                                LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "MarkParentsStatus DB_STATUS_SUCCESS for  " + lEvent.FullPath);
                                MarkParentsStatus(lEvent.FullPath, DB_STATUS_SUCCESS);
                                LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "Calling for UpdateDBForRenameSuccess");
                                UpdateDBForRenameSuccess(lEvent);
                                dbHandler.DeleteEvent(lEvent.EventDbId);
                            }
                        }
                        else
                        {
                            if (bRetConflicts)
                            {
                                LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "isFile bRetConflicts " + bRetConflicts.ToString());
                                ItemDetails iDetails = cMezeoFileCloud.GetContinerResult(strContentURi, ref nStatusCode);

                                if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
                                {
                                    return LOGIN_FAILED;
                                }
                                else if (nStatusCode != ResponseCode.GETCONTINERRESULT)
                                {
                                    if (ResponseCode.NOTFOUND == nStatusCode)
                                        return ITEM_NOT_FOUND;
                                    return SERVER_INACCESSIBLE;
                                }

                                LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "updating DB   DbHandler.PUBLIC to " + iDetails.bPublic + " for DbHandler.KEY " + lEvent.FileName);

                                dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.PUBLIC, iDetails.bPublic, DbHandler.KEY, lEvent.FileName);
                                //bool bPublic = dbHandler.GetBoolean(DbHandler.TABLE_NAME, DbHandler.PUBLIC, DbHandler.KEY + " = '" + lEvent.FileName + "'");

                                LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "getting mime type from DB");
                                string mimeType = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.MIMIE_TYPE, new string[] { DbHandler.KEY }, new string[] { lEvent.FileName }, new DbType[] { DbType.String });
                                LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "mime type " + mimeType);

                                LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "Calling cMezeoFileCloud.FileRename for content uri " + strContentURi + " with new name " + changedName);

                                if (!checkFileTooLarge(lEvent.FullPath))
                                    bRet = cMezeoFileCloud.FileRename(strContentURi, changedName, mimeType, iDetails.bPublic, ref nStatusCode);
                                else
                                    nStatusCode = 200;

                                if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
                                {
                                    return LOGIN_FAILED;
                                }
                                else if (nStatusCode != ResponseCode.FILERENAME)
                                {
                                    if (ResponseCode.NOTFOUND == nStatusCode)
                                        return ITEM_NOT_FOUND;
                                    return SERVER_INACCESSIBLE;
                                }
                                else
                                {
                                    wasSuccessful = true;
                                    LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "MarkParentsStatus " + lEvent.FullPath + " to DB_STATUS_SUCCESS");
                                    MarkParentsStatus(lEvent.FullPath, DB_STATUS_SUCCESS);
                                    LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "Calling UpdateDBForRenameSuccess");
                                    UpdateDBForRenameSuccess(lEvent);
                                    dbHandler.DeleteEvent(lEvent.EventDbId);
                                }
                            }
                        }
                    }
                    break;
            }

            int returnCode = 0;
            if (wasSuccessful)
                returnCode = 1;

            LogWrapper.LogMessage("SyncManager - ProcessLocalEvent", "Leave");

            return returnCode;
        }

        private int UpdateParent(String strPath, String strFullPath)
        {
            // Since the container could have a new parent, it needs to be updated.
            // Remember that the previous parent could have been empty/null/root.
            // Change this for renames as well as moves....
            // General logic:
            //   Grab the parent of this container (after the move).
            //   Set the parent_dir to the name of the parent container.
            String strParentName = "";
            int index = -1;

            // Make sure the parent is there in the path.
            if (0 != strPath.CompareTo(strFullPath))
                strParentName = strFullPath.Substring(0, strFullPath.LastIndexOf("\\"));
            index = strParentName.LastIndexOf("\\");
            if (-1 != index)
                strParentName = strParentName.Substring(index+1);

            return dbHandler.UpdateParentDir(strPath, strParentName);
        }

        private int checkfoOverwrite(ItemDetails item, LocalEvents lEvent)
        {
            int nStatusCode = 0;
            if (!checkFileTooLarge(lEvent.FullPath))
               //cMezeoFileCloud.OverWriteFile(lEvent.FullPath, item.szContentUrl, ref nStatusCode);
                cMezeoFileCloud.UploadingFileOnResume(lEvent.FullPath, item.szContentUrl, ref nStatusCode);
            else
                nStatusCode = 200;

            if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
            {
                return LOGIN_FAILED;
            }
            else if (nStatusCode != ResponseCode.OVERWRITEFILE)
            {
                if (ResponseCode.NOTFOUND == nStatusCode)
                    return ITEM_NOT_FOUND;
                return SERVER_INACCESSIBLE;
            }
            else
            {
                MarkParentsStatus(lEvent.FullPath, DB_STATUS_SUCCESS);
                UpdateDBForModifiedSuccess(lEvent, item.szContentUrl);
                dbHandler.DeleteEvent(lEvent.EventDbId);
            }

            return nStatusCode;                   
        }

        private int ConsumeLocalItemDetail(LocalItemDetails itemDetail)
        {
            int nResultStatus = 1;
            LogWrapper.LogMessage("frmSyncManager - consume", "Enter");
            ItemDetails id = itemDetail.ItemDetails;
            LogWrapper.LogMessage("frmSyncManager - consume", "creating file folder info for " + id.strName);

            using (FileFolderInfo fileFolderInfo = new FileFolderInfo())
            {
                fileFolderInfo.Key = itemDetail.Path;

                fileFolderInfo.IsPublic = id.bPublic;
                fileFolderInfo.IsShared = id.bShared;
                fileFolderInfo.ContentUrl = id.szContentUrl;
                fileFolderInfo.CreatedDate = id.dtCreated;
                fileFolderInfo.FileName = id.strName;
                fileFolderInfo.FileSize = id.dblSizeInBytes;
                fileFolderInfo.MimeType = id.szMimeType;
                fileFolderInfo.ModifiedDate = id.dtModified;
                fileFolderInfo.ParentUrl = id.szParentUrl;
                fileFolderInfo.Status = "INPROGRESS";
                fileFolderInfo.Type = id.szItemType;

                int lastSepIndex = itemDetail.Path.LastIndexOf("\\");
                string parentDirPath = "";

                if (lastSepIndex != -1)
                {
                    parentDirPath = itemDetail.Path.Substring(0, itemDetail.Path.LastIndexOf("\\"));
                    parentDirPath = parentDirPath.Substring(parentDirPath.LastIndexOf("\\") + 1);
                }
                //else
                //{
                //    parentDirPath = itemDetail.Path;
                //}

                fileFolderInfo.ParentDir = parentDirPath;

                if (fileFolderInfo.ETag == null)
                {
                    fileFolderInfo.ETag = "";
                }
                if (fileFolderInfo.MimeType == null)
                {
                    fileFolderInfo.MimeType = "";
                }

                LogWrapper.LogMessage("frmSyncManager - consume", "writing file folder info for " + id.strName + " in DB");
                //if (!dbHandler.Write(fileFolderInfo))
                //    return 1;
                dbHandler.Write(fileFolderInfo);

                string downloadObjectName = BasicInfo.SyncDirPath + "\\" + itemDetail.Path;

                LogWrapper.LogMessage("frmSyncManager - consume", "download object " + downloadObjectName);

                LogWrapper.LogMessage("frmSyncManager - consume", "setting parent folders status DB_STATUS_IN_PROGRESS, bRet FALSE");
                MarkParentsStatus(downloadObjectName, DB_STATUS_IN_PROGRESS);
                bool bRet = false;
                int refCode = 0;

                if (id.szItemType == "DIRECTORY")
                {
                    LogWrapper.LogMessage("frmSyncManager - consume", id.strName + " is DIRECTORY");
                    System.IO.Directory.CreateDirectory(downloadObjectName);

                    if (id.strETag.Trim().Length == 0)
                    {
                        LogWrapper.LogMessage("frmSyncManager - consume", "Getting eTag for " + id.strName);
                        id.strETag = cMezeoFileCloud.GetETag(id.szContentUrl, ref refCode);
                        if (refCode == ResponseCode.LOGINFAILED1 || refCode == ResponseCode.LOGINFAILED2)
                        {
                            lockObject.StopThread = true;
                            return ResponseCode.LOGINFAILED1;  // CancelReason.LOGINFAILED
                        }
                        else if (refCode != ResponseCode.GETETAG)
                        {
                            lockObject.StopThread = true;
                            if (ResponseCode.NOTFOUND == refCode)
                                return ResponseCode.NOTFOUND;
                            return ResponseCode.SERVER_INACCESSIBLE; // CancelReason.SERVER_INACCESSIBLE
                        }
                    }

                    LogWrapper.LogMessage("frmSyncManager - consume", "eTag for " + id.strName + ": " + id.strETag + ", bRet TRUE");
                    bRet = true;
                }
                else
                {
                    LogWrapper.LogMessage("frmSyncManager - consume", id.strName + " is NOT DIRECTORY");
                    bRet = cMezeoFileCloud.DownloadFile(id.szContentUrl + '/' + id.strName,
                                            downloadObjectName, id.dblSizeInBytes, ref refCode);

                    if (refCode == ResponseCode.LOGINFAILED1 || refCode == ResponseCode.LOGINFAILED2)
                    {
                        //lockObject.StopThread = true;
                        return ResponseCode.LOGINFAILED1; // CancelReason.LOGIN_FAILED
                    }
                    else if (refCode == ResponseCode.NOTFOUND)
                    {
                        //lockObject.StopThread = true;
                        return ResponseCode.NOTFOUND;
                    }
                    else if (refCode != ResponseCode.DOWNLOADFILE)
                    {
                        //lockObject.StopThread = true;
                        return ResponseCode.SERVER_INACCESSIBLE; // CancelReason.SERVER_INACCESSIBLE
                    }

                    LogWrapper.LogMessage("frmSyncManager - consume", "bRet for " + id.strName + " is " + bRet.ToString());
                    if (refCode == ResponseCode.INSUFFICIENT_STORAGE_AVAILABLE)
                    {
                        LogWrapper.LogMessage("frmSyncManager - consume", "INSUFFICIENT_STORAGE_AVAILABLE, calling CancelAndNotify with reason INSUFFICIENT_STORAGE");
                        return ResponseCode.INSUFFICIENT_STORAGE_AVAILABLE;
                    }

                    LogWrapper.LogMessage("frmSyncManager - consume", "Getting eTag for " + id.strName);
                    id.strETag = cMezeoFileCloud.GetETag(id.szContentUrl, ref refCode);

                    if (refCode == ResponseCode.LOGINFAILED1 || refCode == ResponseCode.LOGINFAILED2)
                    {
                        //lockObject.StopThread = true;
                        return ResponseCode.LOGINFAILED1; // CancelReason.LOGIN_FAILED
                    }
                    else if (refCode != ResponseCode.GETETAG)
                    {
                        //lockObject.StopThread = true;
                        if (refCode == ResponseCode.NOTFOUND)
                            return ResponseCode.NOTFOUND;
                        return ResponseCode.SERVER_INACCESSIBLE; // CancelReason.SERVER_INACCESSIBLE
                    }
                    LogWrapper.LogMessage("frmSyncManager - consume", "eTag for " + id.strName + ": " + id.strETag);
                }

                if (!bRet)
                {
                    LogWrapper.LogMessage("frmSyncManager - consume", "bRet FALSE, writing to cFileCloud.AppEventViewer");
                    string Description = "";
                    Description += LanguageTranslator.GetValue("ErrorBlurbDownload1");
                    Description += LanguageTranslator.GetValue("ErrorBlurbDownload2");
                    Description += LanguageTranslator.GetValue("ErrorBlurbDownload3");
                    // cFileCloud.AppEventViewer(AboutBox.AssemblyTitle, Description, 3);
                }
                else
                {
                    LogWrapper.LogMessage("frmSyncManager - consume", "setting parent folders status to DB_STATUS_SUCCESS for " + downloadObjectName);
                    MarkParentsStatus(downloadObjectName, DB_STATUS_SUCCESS);
                    //fileFolderInfo.ETag = id.strETag;
                    if (id.szItemType == "DIRECTORY")
                    {
                        LogWrapper.LogMessage("frmSyncManager - consume", "updating DB for folder " + downloadObjectName);
                        DirectoryInfo dInfo = new DirectoryInfo(downloadObjectName);
                        dbHandler.UpdateModifiedDate(dInfo.LastWriteTime, fileFolderInfo.Key);
                        dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.E_TAG, id.strETag, DbHandler.KEY, fileFolderInfo.Key);
                        dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.STATUS, "SUCCESS", DbHandler.KEY, fileFolderInfo.Key);
                    }
                    else
                    {
                        LogWrapper.LogMessage("frmSyncManager - consume", "updating DB for file " + downloadObjectName);
                        FileInfo fInfo = new FileInfo(downloadObjectName);
                        dbHandler.UpdateModifiedDate(fInfo.LastWriteTime, fileFolderInfo.Key);
                        dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.E_TAG, id.strETag, DbHandler.KEY, fileFolderInfo.Key);
                        dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.STATUS, "SUCCESS", DbHandler.KEY, fileFolderInfo.Key);
                    }

                    //if (downloadEvent != null)
                    //{
                    //    LogWrapper.LogMessage("frmSyncManager - consume", "calling  downloadEvent with " + downloadObjectName);
                    //    downloadEvent(this, new FileDownloaderEvents(downloadObjectName, 0));
                    //}
                }
            }

            //if (IsAnalysisCompleted && queue.Count == 0)
            //{
            //    LogWrapper.LogMessage("FileDownloader - consume", "Analysis completed and queue lenth is ZERO");
            //    if (fileDownloadCompletedEvent != null)
            //    {
            //        LogWrapper.LogMessage("FileDownloader - consume", "calling fileDownloadCompletedEvent");
            //        done = true;
            //        fileDownloadCompletedEvent();
            //    }
            //}

            LogWrapper.LogMessage("frmSyncManager - consume", "Leave");
            return nResultStatus;
        }

        private void GetNextEvent(ref LocalEvents lEvent, ref NQDetails nqEvent, ref LocalItemDetails localItemDetails)
        {
            bool usePriorityQueueLogic = false;
            if (usePriorityQueueLogic)
            {
                // See if there are any events in the queue.
                // Local events take priority over NQ.
                // localItemDetails (initial sync events) take priority over local events.
                localItemDetails = dbHandler.GetLocalItemDetailsEvent();
                if (localItemDetails == null)
                {
                    lEvent = dbHandler.GetLocalEvent();
                    if (lEvent == null)
                        nqEvent = dbHandler.GetNQEvent();
                }
            }
            else
            {
                // See if there are any events in the queue.
                // localItemDetails (initial sync events) take priority over other events.
                // If there are no 'I'nitial sync events, just get the item with the
                // lowest index, determine the type, populate it, and return it.
                // The GetXEvent() functions sort by index so we just have to know
                // the type of event before calling the appropriate function.
                localItemDetails = dbHandler.GetLocalItemDetailsEvent();
                if (localItemDetails == null)
                {
                    string eventType = "";
                    Int64 eventId = dbHandler.GetNextEventId(ref eventType);
                    if (-1 != eventId)
                    {
                        switch (eventType)
                        {
                            case "I":    // The 'I'nitial events should already be handled.
                                break;

                            case "L":    // 'L'ocal event.
                                lEvent = dbHandler.GetLocalEvent();
                                break;

                            case "N":    // 'N'otification queue event.
                                nqEvent = dbHandler.GetNQEvent();
                                break;
                        }
                    }
                }
            }
        }

        private bool PopulateNQEvents()
        {
            int newEvents = 0;
            int nStatusCode = 0;
            NQDetails[] pNQDetails = null;

            if (IsSyncPaused() || (cLoginDetails == null))
                return false;

            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    this.lblStatusL1.Text = LanguageTranslator.GetValue("SyncManagerCheckingServer");
                    this.lblStatusL3.Text = "";
                    ShowNextSyncLabel(false);

                    lblStatusL1.Refresh();
                    lblStatusL3.Refresh();
                    label1.Refresh();
                });
            }
            else
            {
                this.lblStatusL1.Text = LanguageTranslator.GetValue("SyncManagerCheckingServer");
                this.lblStatusL3.Text = "";
                ShowNextSyncLabel(false);

                lblStatusL1.Refresh();
                lblStatusL3.Refresh();
                label1.Refresh();
            }

            NQLengthResult nqLengthRes = cMezeoFileCloud.NQGetLength(BasicInfo.ServiceUrl + cLoginDetails.szNQParentUri, BasicInfo.GetQueueName(), ref nStatusCode);
            if (nStatusCode == ResponseCode.LOGINFAILED1 || nStatusCode == ResponseCode.LOGINFAILED2)
            {
                this.Hide();
                frmParent.ShowLoginAgainFromSyncMgr();
                return false;
            }

            // Grab some events from the notification queue if any exist.
            pNQDetails = cMezeoFileCloud.NQGetData(BasicInfo.ServiceUrl + cLoginDetails.szNQParentUri, BasicInfo.GetQueueName(), 10, ref nStatusCode);
            if (nStatusCode == ResponseCode.NQGETDATA)
            {
                if (pNQDetails != null)
                {
                    foreach (NQDetails nq in pNQDetails)
                    {
                        // Populate the queue with any we got.
                        dbHandler.AddNQEvent(nq);
                        newEvents++;
                    }
                    cMezeoFileCloud.NQDeleteValue(BasicInfo.ServiceUrl + cLoginDetails.szNQParentUri, BasicInfo.GetQueueName(), newEvents, ref nStatusCode);
                    messageMax = (int)dbHandler.GetJobCount();
                }
            }

            return (newEvents > 0);
        }

        private int RunSyncLoop(object sender, DoWorkEventArgs e)
        {
            LocalEvents lEvent = null;
            NQDetails nqEvent = null;
            LocalItemDetails localItemDetails = null;
            int nStatus = 0;
            int previousInitialCount = (int)dbHandler.GetInitialSyncEventCount(); 
            int currentInitialCount = 0;

            // Initialize any counters.
            messageValue = 0;
            messageMax = (int)dbHandler.GetJobCount();

            // Set the GUI to reflect that a sync is going on.
            //SetSyncThreadInProgress(true);
            // See if there are any events in the queue.  Local events take priority over NQ.
            // LocalItemDetails (initial sync events) take priority over local events.
            resetAllControls();
            GetNextEvent(ref lEvent, ref nqEvent, ref localItemDetails);

            // If there are no more events in the queue, then see if the server has any more that need processing.
            if (((null == lEvent) && (null == nqEvent) && (null == localItemDetails)) && !IsSyncPaused())
            {
                PopulateNQEvents();
                GetNextEvent(ref lEvent, ref nqEvent, ref localItemDetails);
            }
            
            // Process the events 1 at a time in priority order.
            while (((lEvent != null) || (nqEvent != null) || (null != localItemDetails)) && !IsSyncPaused() && (((BackgroundWorker)sender).CancellationPending == false) && !CanNotTalkToServer() && !IsSyncGenerateLocalEvent())
            {
                // Increment the counter for the message text.
                messageValue++;
                messageMax = (int)dbHandler.GetJobCount();
                showProgress();

                if (localItemDetails != null)
                {
                    nStatus = ConsumeLocalItemDetail(localItemDetails);
                    if ((nStatus == 1) || (ResponseCode.NOTFOUND == nStatus))
                    {
                        dbHandler.DeleteEvent(localItemDetails.EventDbId);
                    }
                    else if (nStatus == ResponseCode.LOGINFAILED1 || nStatus == ResponseCode.LOGINFAILED2)
                    {
                        //e.Result = CancelReason.LOGIN_FAILED;
                        break;
                    }
                    else if (nStatus != ResponseCode.GETETAG && nStatus != ResponseCode.DOWNLOADFILE && nStatus != ResponseCode.DOWNLOADITEMDETAILS && nStatus != 1)
                    {
                        if (nStatus == SERVER_INACCESSIBLE)
                        {
                            SetCanNotTalkToServer(true);
                            StopSync();
                        }
                        //e.Result = CancelReason.SERVER_INACCESSIBLE;
                        break;
                    }
                }
                else if (lEvent != null)
                {
                    nStatus = HandleEvent((BackgroundWorker)sender, lEvent);
                    //nStatus = HandleEvent(null, lEvent);
                    if (1 == nStatus)
                        dbHandler.DeleteEvent(lEvent.EventDbId);
                    if (nStatus == SERVER_INACCESSIBLE)
                    {
                        SetCanNotTalkToServer(true);
                        StopSync();
                    }
                }
                else if (nqEvent != null)
                {
                    ShowOtherProgressBar(nqEvent.StrObjectName);
                    showProgress();

                    // Look for an NQ event.
                    nStatus = UpdateFromNQ(nqEvent);
                    if (nStatus == 1)
                    {
                        dbHandler.DeleteEvent(nqEvent.EventDbId);
                    }
                    else if (nStatus == ResponseCode.LOGINFAILED1 || nStatus == ResponseCode.LOGINFAILED2)
                    {
                        //e.Result = CancelReason.LOGIN_FAILED;
                        break;
                    }
                    else if (nStatus == -3)
                    {
                        // Something is going on.  Forget about it and continue on.
                        dbHandler.DeleteEvent(nqEvent.EventDbId);
                    }
                    else if (nStatus != ResponseCode.GETETAG && nStatus != ResponseCode.DOWNLOADFILE && nStatus != ResponseCode.DOWNLOADITEMDETAILS && nStatus != 1)
                    {
                        if (nStatus == SERVER_INACCESSIBLE)
                        {
                            SetCanNotTalkToServer(true);
                            StopSync();
                        }
                        //e.Result = CancelReason.SERVER_INACCESSIBLE;
                        break;
                    }
                }

                // Release the items so we can look for new ones.
                lEvent = null;
                nqEvent = null;
                localItemDetails = null;
                GetNextEvent(ref lEvent, ref nqEvent, ref localItemDetails);

                // If there are no more events in the queue, then see if the server has any more that need processing.
                if ((lEvent == null) && (nqEvent == null) && (null == localItemDetails))
                {
                    if (!IsSyncPaused() && !CanNotTalkToServer())
                    {
                        PopulateNQEvents();
                        GetNextEvent(ref lEvent, ref nqEvent, ref localItemDetails);
                    }
                }
                currentInitialCount = (int)dbHandler.GetInitialSyncEventCount();

                if (previousInitialCount > 0 && currentInitialCount == 0)
                {
                    ProcessOfflineEvents();
                    /* NOTE: Initial sync is done and Sync manager has processed all 'I' from 
                    database and now it is ready to perform local sync */ 
                }
             
                    previousInitialCount = currentInitialCount;
            }

            // Update the GUI and any flags now that we're done with the sync.

            return nStatus;
        }

        private void InitializeLocalEventsProcess(int progressMax)
        {
            //LogWrapper.LogMessage("frmSyncManager - InitializeLocalEventsProcess", "enter");
            if (frmIssuesFound != null && frmIssuesFound.GetItemsInList() > 0)
                SetIssueFound(true);
            else
                SetIssueFound(false);
            
            ShowNextSyncLabel(false);
            //LogWrapper.LogMessage("frmSyncManager - InitializeLocalEventsProcess", "leave");
        }

        private bool SendToRecycleBin(string strPath, bool isFile)
        {
            bool deleted = true;
            try
            {
                if (isFile)
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(strPath, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                                                       Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                else
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(strPath, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                                                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
            }
            catch (IOException ioException)
            {
                LogWrapper.LogMessage("frmSyncManager - SendToRecycleBin", "Caught exception: " + ioException.Message);
                deleted = false;
            }
            catch (Exception ex)
            {
                LogWrapper.LogMessage("frmSyncManager - SendToRecycleBin", "Caught exception: " + ex.Message);
            }

            return deleted;
        }

        #endregion


        #region Balloon message functions

        private void ShowUpdateAvailableBalloonMessage(string strNewVersion)
        {
            string strUpdate;
            strUpdate = LanguageTranslator.GetValue("VersionText") + strNewVersion + LanguageTranslator.GetValue("AppAvailableMessage");
            cnotificationManager.NotificationHandler.Icon = Properties.Resources.app_icon_upgrade;
            cnotificationManager.NotificationHandler.ShowBalloonTip(1, LanguageTranslator.GetValue("UpdateAvailable"),
                                                                      strUpdate,
                                                                     ToolTipIcon.None);

            cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion + "\n" + LanguageTranslator.GetValue("UpdateAvailable");
        }

        private void ShowCurrentVersionBalloonMessage(string strNewVersion)
        {
            string strNoUpdateAvailable;
            strNoUpdateAvailable = LanguageTranslator.GetValue("VersionText") + strNewVersion + LanguageTranslator.GetValue("AppAvailableMessage");
            //cnotificationManager.NotificationHandler.Icon = Properties.Resources.app_icon_upgrade;
            //cnotificationManager.NotificationHandler.ShowBalloonTip(1, LanguageTranslator.GetValue("NoUpdateAvailable"),
            //                                                          strNoUpdateAvailable,
            //                                                         ToolTipIcon.None);
            cnotificationManager.NotificationHandler.ShowBalloonTip(1, LanguageTranslator.GetValue("NoUpdateAvailable"),
                                                                      " ",
                                                                     ToolTipIcon.None);
        }

        private void IssueFoundBalloonMessage()
        {
            SetIssueFound(true);
            cnotificationManager.NotificationHandler.Icon = Properties.Resources.app_icon_warning;
            cnotificationManager.NotificationHandler.ShowBalloonTip(1, LanguageTranslator.GetValue("TrayBalloonSyncStatusText"),
                                                                      LanguageTranslator.GetValue("SyncConflictFoundText"),
                                                                     ToolTipIcon.None);

            cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion + "\n" + LanguageTranslator.GetValue("SyncConflictFoundText");

            frmParent.toolStripMenuItem4.Text = LanguageTranslator.GetValue("SyncManagerMenuIssueFoundText");
        }

        private void SyncStoppedBalloonMessage()
        {
            cnotificationManager.NotificationHandler.ShowBalloonTip(1, LanguageTranslator.GetValue("TrayBalloonSyncStatusText"),
                                                                           LanguageTranslator.GetValue("TrayBalloonSyncStopText"),
                                                                          ToolTipIcon.None);
            cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion + "\n" + LanguageTranslator.GetValue("TrayBalloonSyncStopText");
            frmParent.toolStripMenuItem4.Text = LanguageTranslator.GetValue("TrayBalloonSyncStopText");
        }

        public void SyncPauseBalloonMessage()
        {
            cnotificationManager.NotificationHandler.Icon = Properties.Resources.app_icon_disabled;
            cnotificationManager.NotificationHandler.ShowBalloonTip(1, LanguageTranslator.GetValue("TrayBalloonSyncStatusText"),
                                                                           LanguageTranslator.GetValue("TrayBalloonSyncPauseText"),
                                                                          ToolTipIcon.None);
            cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion + "\n" + LanguageTranslator.GetValue("TrayBalloonSyncPauseText");
            frmParent.toolStripMenuItem4.Text = LanguageTranslator.GetValue("TrayBalloonSyncPauseText");
        }

        public void SyncResumeBalloonMessage()
        {
            if (IsInIdleState())
                cnotificationManager.NotificationHandler.Icon = Properties.Resources.MezeoVault;
            else
                cnotificationManager.NotificationHandler.Icon = Properties.Resources.mezeosyncstatus_syncing;

            cnotificationManager.NotificationHandler.ShowBalloonTip(1, LanguageTranslator.GetValue("TrayBalloonSyncStatusText"),
                                                                           LanguageTranslator.GetValue("TrayBalloonSyncResumeText"),
                                                                          ToolTipIcon.None);
            cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion + "\n" + LanguageTranslator.GetValue("TrayBalloonSyncResumeText");
            frmParent.toolStripMenuItem4.Text = LanguageTranslator.GetValue("TrayBalloonSyncResumeText");
        }

        private void InitialSyncBalloonMessage()
        {
            lblStatusL1.Text = LanguageTranslator.GetValue("SyncManagerStatusAllFilesInSyncLabel");
            cnotificationManager.NotificationHandler.Icon = Properties.Resources.MezeoVault;
            cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion + "\n" + LanguageTranslator.GetValue("TrayBalloonInitialSyncFilesUpToDateText");
            frmParent.toolStripMenuItem4.Text = LanguageTranslator.GetValue("TrayBalloonInitialSyncFilesUpToDateText");
        }

        void ShowInsufficientStorageMessage()
        {
            lblStatusL1.Text = LanguageTranslator.GetValue("InsufficientStorageMessage");
            cnotificationManager.NotificationHandler.ShowBalloonTip(1, LanguageTranslator.GetValue("TrayBalloonSyncStatusText"),
                                                                         LanguageTranslator.GetValue("InsufficientStorageMessage"),
                                                                        ToolTipIcon.None);
            cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion + "\n" + LanguageTranslator.GetValue("InsufficientStorageMessage");
            frmParent.toolStripMenuItem4.Text = LanguageTranslator.GetValue("TrayBalloonSyncStopText");
        }

        private void InitialSyncUptodateMessage()
        {
            cnotificationManager.NotificationHandler.Icon = Properties.Resources.MezeoVault;
            cnotificationManager.NotificationHandler.ShowBalloonTip(1, LanguageTranslator.GetValue("TrayBalloonSyncStatusText"),
                                                                        LanguageTranslator.GetValue("TrayBalloonInitialSyncText") + "\n" + LanguageTranslator.GetValue("TrayBalloonInitialSyncFilesUpToDateText"),
                                                                    ToolTipIcon.None);
            cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion + "\n" + LanguageTranslator.GetValue("TrayHoverInitialSyncUpToDateText");            
            frmParent.toolStripMenuItem4.Text = LanguageTranslator.GetValue("TrayHoverInitialSyncUpToDateText");
        }

        public void SetUpSyncNowNotification()
        {
            cnotificationManager.NotificationHandler.Icon = Properties.Resources.mezeosyncstatus_syncing;
            cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion + "\n" + LanguageTranslator.GetValue("TrayHoverSyncProgressText"); //+ (int)0 + LanguageTranslator.GetValue("TrayHoverSyncProgressInitialText");
            frmParent.toolStripMenuItem4.Text = LanguageTranslator.GetValue("TrayHoverSyncProgressText");
        }

        private void SyncFolderUpToDateMessage()
        {
            if (messageValue > 0)
            {
                cnotificationManager.NotificationHandler.ShowBalloonTip(1, LanguageTranslator.GetValue("TrayBalloonSyncStatusText"),
                                                                                        LanguageTranslator.GetValue("TrayBalloonSyncFolderUpToDate"),
                                                                                       ToolTipIcon.None);

                cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion + "\n" + LanguageTranslator.GetValue("TrayBalloonSyncFolderUpToDate");
                frmParent.toolStripMenuItem4.Text = LanguageTranslator.GetValue("TrayHoverInitialSyncUpToDateText");
            }
        }

        private void ShowInitialSyncMessage()
        {
            //btnSyncNow.Text = LanguageTranslator.GetValue("PauseSync");
            cnotificationManager.NotificationHandler.Icon = Properties.Resources.mezeosyncstatus_syncing;
            cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion + "\n" + LanguageTranslator.GetValue("TrayHoverSyncProgressText"); 
            frmParent.toolStripMenuItem4.Text = LanguageTranslator.GetValue("TrayHoverSyncProgressText");
        }

        public void SyncOfflineMessage()
        {
            cnotificationManager.NotificationHandler.ShowBalloonTip(1, LanguageTranslator.GetValue("TrayBalloonSyncStatusText"),
                                                                                    LanguageTranslator.GetValue("TrayAppOfflineText"), ToolTipIcon.None);

            cnotificationManager.HoverText = LanguageTranslator.GetValue("TrayAppOfflineText");
            cnotificationManager.NotifyIcon = Properties.Resources.app_offline;
            frmParent.toolStripMenuItem4.Text = LanguageTranslator.GetValue("AppOfflineMenu");

        }

        public void SyncOnlineMessage()
        {
            cnotificationManager.NotificationHandler.ShowBalloonTip(1, LanguageTranslator.GetValue("TrayBalloonSyncStatusText"),
                                                                            LanguageTranslator.GetValue("TrayAppOnlineText"), ToolTipIcon.None);

            cnotificationManager.HoverText = LanguageTranslator.GetValue("TrayAppOnlineText");
            cnotificationManager.NotifyIcon = Properties.Resources.MezeoVault;
        }
             
        #endregion


        #region ProgressBar Events

        public void SetMaxProgress(double fileSize, string fileName)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    if (!IsSyncPaused())
                    {
                        //LogWrapper.LogMessage("frmSyncManager - SetMaxProgress", "enter");
                        string syncPath;
                        if (string.IsNullOrEmpty(fileName))
                            syncPath = "";
                        else
                            syncPath = AboutBox.AssemblyTitle + "\\" + fileName.Substring(BasicInfo.SyncDirPath.Length + 1);

                        lblStatusL3.Text = syncPath;
                        pbScale = 1;

                        while (fileSize > Int32.MaxValue)
                        {
                            fileSize = fileSize / 10;
                            pbScale = pbScale * 10;
                        }

                        pbSyncProgress.Maximum = (int)fileSize;
                        pbSyncProgress.Value = 0;

                        if (pbSyncProgress.Style != ProgressBarStyle.Continuous)
                            pbSyncProgress.Style = ProgressBarStyle.Continuous;

                        pbSyncProgress.BringToFront();
                        pbSyncProgress.Visible = true;
                        pbSyncProgress.Show();
                        lblPercentDone.Visible = true;
                        lblPercentDone.Show();
                        //LogWrapper.LogMessage("frmSyncManager - SetMaxProgress", "leave");
                    }
                });
            }
            else
            {
                if (!IsSyncPaused())
                {
                    //LogWrapper.LogMessage("frmSyncManager - SetMaxProgress", "enter");
                    string syncPath;
                    if (string.IsNullOrEmpty(fileName))
                        syncPath = "";
                    else
                        syncPath = AboutBox.AssemblyTitle + "\\" + fileName.Substring(BasicInfo.SyncDirPath.Length + 1);

                    lblStatusL3.Text = syncPath;

                    pbScale = 1;

                    while (fileSize > Int32.MaxValue)
                    {
                        fileSize = fileSize / 10;
                        pbScale = pbScale * 10;
                    }

                    pbSyncProgress.Maximum = (int)fileSize;
                    pbSyncProgress.Value = 0;

                    if (pbSyncProgress.Style != ProgressBarStyle.Continuous)
                        pbSyncProgress.Style = ProgressBarStyle.Continuous;

                    pbSyncProgress.BringToFront();
                    pbSyncProgress.Visible = true;
                    pbSyncProgress.Show();
                    lblPercentDone.Visible = true;
                    lblPercentDone.Show();
                    //LogWrapper.LogMessage("frmSyncManager - SetMaxProgress", "leave");
                }
            }
        }

        public void ShowOtherProgressBar(string fileName)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    if (!IsSyncPaused())
                    {
                        //LogWrapper.LogMessage("frmSyncManager - ShowOtherProgressBar", "enter");
                        lblStatusL3.Text = fileName;

                        pbSyncProgress.Maximum = 1;
                        pbSyncProgress.Value = 0;

                        if (false == pbSyncProgress.Visible)
                        {
                            pbSyncProgress.Visible = true;
                            pbSyncProgress.Show();
                        }
                        pbSyncProgress.BringToFront();
                        pbSyncProgress.Refresh();
                        lblPercentDone.Text = "100%";
                        lblPercentDone.Visible = true;
                        lblPercentDone.Show();
                        //LogWrapper.LogMessage("frmSyncManager - ShowOtherProgressBar", "leave");
                    }
                });
            }
            else
            {
                if (!IsSyncPaused())
                {
                    //LogWrapper.LogMessage("frmSyncManager - ShowOtherProgressBar", "enter");
                    lblStatusL3.Text = fileName;

                    pbSyncProgress.Maximum = 1;
                    pbSyncProgress.Value = 1;

                    //if (pbSyncProgress.Style != ProgressBarStyle.Marquee)
                    //{
                    //    pbSyncProgress.Style = ProgressBarStyle.Marquee;
                    //    pbSyncProgress.MarqueeAnimationSpeed = 200;
                    //}

                    if (false == pbSyncProgress.Visible)
                    {
                        pbSyncProgress.Visible = true;
                        pbSyncProgress.Show();
                    }
                    pbSyncProgress.BringToFront();
                    pbSyncProgress.Refresh();
                    lblPercentDone.Text = "100%";
                    lblPercentDone.Visible = true;
                    lblPercentDone.Show();
                    //LogWrapper.LogMessage("frmSyncManager - ShowOtherProgressBar", "leave");
                }
            }
        }

        //Callback function for contiue running application
        public bool CallbackContinueRun()
        {
            if (IsInIdleState())
                return false;
            return true;
        }

        //To increment progress bar this is a call back function 
        public void CallbackSyncProgress(double filesize)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    //LogWrapper.LogMessage("frmSyncManager - CallbackSyncProgress", "enter");
                    try
                    {
                        if (IsSyncPaused() || CallbackContinueRun() == false)
                            return;

                        filesize = filesize / pbScale;

                        if (pbSyncProgress.Value + filesize > pbSyncProgress.Maximum)
                            pbSyncProgress.Value = pbSyncProgress.Maximum;
                        else
                            pbSyncProgress.Value += (int)filesize;
                        if (0 == pbSyncProgress.Maximum)
                        {
                            // For 0 byte transfers and operations such as delete, copy, move, etc....
                            pbSyncProgress.Maximum = 1;
                            pbSyncProgress.Value = 1;
                        }
                        pbSyncProgress.Refresh();

                        if (pbSyncProgress.Maximum > 0)
                        {
                            double progress = ((double)pbSyncProgress.Value / pbSyncProgress.Maximum) * 100.0;
                            lblPercentDone.Text = Convert.ToString((int)progress) + "%";
                        }
                        else
                        {
                            //Progress bar will show 100% - making as string in resource file
                            lblPercentDone.Text = LanguageTranslator.GetValue("ProgressBarComplete");
                        }
                    //    cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion;
                        cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion + "\n" + LanguageTranslator.GetValue("TrayHoverSyncProgressText"); 
                    }
                    catch (Exception ex)
                    {
                        LogWrapper.LogMessage("frmSyncManager - CallBackSyncProgress", "Caught exception: " + ex.Message);
                        LogWrapper.LogMessage("frmSyncManager - CallBackSyncProgress", "Caught exception Maximum and actual value is: " + pbSyncProgress.Maximum + " , " + pbSyncProgress.Value);
                    }
                    //LogWrapper.LogMessage("frmSyncManager - CallbackSyncProgress", "leave");
                });
            }
            else
            {
                //LogWrapper.LogMessage("frmSyncManager - CallbackSyncProgress", "enter");
                try
                {
                    if (IsSyncPaused() || CallbackContinueRun() == false)
                        return;

                    filesize = filesize / pbScale;

                    if (pbSyncProgress.Value + filesize > pbSyncProgress.Maximum)
                        pbSyncProgress.Value = pbSyncProgress.Maximum;
                    else
                        pbSyncProgress.Value += (int)filesize;
                    if (0 == pbSyncProgress.Maximum)
                    {
                        // For 0 byte transfers and operations such as delete, copy, move, etc....
                        pbSyncProgress.Maximum = 1;
                        pbSyncProgress.Value = 1;
                    }
                    pbSyncProgress.Refresh();

                    if (pbSyncProgress.Maximum > 0)
                    {
                        double progress = ((double)pbSyncProgress.Value / pbSyncProgress.Maximum) * 100.0;
                        lblPercentDone.Text = Convert.ToString((int)progress) + "%";
                    }
                    else
                    {
                        //Progress bar will show 100% - making as string in resource file
                        lblPercentDone.Text = LanguageTranslator.GetValue("ProgressBarComplete");
                    }
                //    cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion;
                    cnotificationManager.HoverText = global::Mezeo.Properties.Resources.BrSyncManagerTitle + " " + AboutBox.AssemblyVersion + "\n" + LanguageTranslator.GetValue("TrayHoverSyncProgressText"); 
                }
                catch (Exception ex)
                {
                    LogWrapper.LogMessage("frmSyncManager - CallBackSyncProgress", "Caught exception: " + ex.Message);
                    LogWrapper.LogMessage("frmSyncManager - CallBackSyncProgress", "Caught exception Maximum and actual value is: " + pbSyncProgress.Maximum + " , " + pbSyncProgress.Value);
                }

                //LogWrapper.LogMessage("frmSyncManager - CallbackSyncProgress", "leave");
            }
        }

        #endregion
       
        # region DB methods 

        private void AddInDBForAdded(LocalEvents lEvent)
        {
            FileFolderInfo fInfo = new FileFolderInfo();

            FileInfo fileInfo = new FileInfo(lEvent.FullPath);

            fInfo.Key = lEvent.FileName;
            fInfo.ContentUrl = "";
            fInfo.ParentUrl = GetParentURI(lEvent.FileName);
            fInfo.CreatedDate = fileInfo.CreationTime;
            fInfo.ModifiedDate = fileInfo.LastWriteTime;
            fInfo.MimeType = "";
            fInfo.IsPublic = false;
            fInfo.IsShared = false;
            fInfo.Status = DB_STATUS_IN_PROGRESS;

            fInfo.ETag = "";

            if (lEvent.FileName.LastIndexOf("\\") == -1)
            {
                fInfo.FileName = lEvent.FileName;
                fInfo.ParentDir = "";
            }
            else
            {
                fInfo.FileName = lEvent.FileName.Substring(lEvent.FileName.LastIndexOf("\\") + 1);
                fInfo.ParentDir = lEvent.FileName.Substring(0, lEvent.FileName.LastIndexOf("\\"));
                fInfo.ParentDir = fInfo.ParentDir.Substring(fInfo.ParentDir.LastIndexOf("\\") +1);
            }

            if ((fileInfo.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
            {
                fInfo.Type = "DIRECTORY";
                fInfo.FileSize = 0;
            }
            else
            {
                fInfo.Type = "FILE";
                fInfo.FileSize = fileInfo.Length;
            }

            dbHandler.Write(fInfo);
        }

        private void AddInDBForRename(LocalEvents lEvent)
        {
            dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.KEY , lEvent.FileName , DbHandler.KEY , lEvent.OldFileName);

            // Now that the key has been updated, let's update the name as well.
            string fileName = lEvent.FileName.Substring(lEvent.FileName.LastIndexOf("\\") + 1);
            dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.FILE_NAME, fileName, DbHandler.KEY, lEvent.FileName);
            UpdateDBForStatus(lEvent, DB_STATUS_IN_PROGRESS);
        }

        private void UpdateDBForStatus(LocalEvents lEvent, string strStatus)
        {
            dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.STATUS , strStatus , DbHandler.KEY , lEvent.FileName );
        }

        private void UpdateDBForAddedSuccess(string strContentUri, LocalEvents lEvent)
        {
            int nStatusCode = 0;
            dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.CONTENT_URL , strContentUri , DbHandler.KEY , lEvent.FileName);

            string strEtag = cMezeoFileCloud.GetETag(strContentUri, ref nStatusCode);
            dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.E_TAG , strEtag , DbHandler.KEY , lEvent.FileName );

            UpdateDBForStatus(lEvent, DB_STATUS_SUCCESS);
        }

        private void UpdateDBForRemoveSuccess(LocalEvents lEvent)
        {
            string strType = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.TYPE, new string[] { DbHandler.KEY }, new string[] { lEvent.FileName }, new DbType[] { DbType.String });
            dbHandler.Delete(DbHandler.TABLE_NAME, DbHandler.KEY , lEvent.FileName);

            if (strType == "DIRECTORY")
            {                
                UpdateDBForRemoveDir(lEvent.FileName);
            }
        }

        private void UpdateDBForRemoveDir(string strDir)
        {
            string strParentDir = strDir.Substring(strDir.LastIndexOf("\\") + 1);
            List<string> result = dbHandler.GetStringList(DbHandler.TABLE_NAME, DbHandler.KEY, DbHandler.PARENT_DIR , strParentDir);
            foreach (string path in result)
            {
                if ((path.Length > strDir.Length) && (path.Substring(0, strDir.Length) == strDir))
                {
                    string strType = dbHandler.GetString(DbHandler.TABLE_NAME, DbHandler.TYPE, new string[] { DbHandler.KEY }, new string[] { path }, new DbType[] { DbType.String });
                    if (strType == "DIRECTORY")
                    {
                        dbHandler.Delete(DbHandler.TABLE_NAME, DbHandler.KEY , path);
                        //string strpath = path.Substring(path.LastIndexOf("\\") + 1);
                        UpdateDBForRemoveDir(path);
                    }
                    else
                        dbHandler.Delete(DbHandler.TABLE_NAME, DbHandler.KEY , path);
                }
            }
        }

        private void UpdateDBForRenameSuccess(LocalEvents lEvent)
        {
            UpdateDBForStatus(lEvent, DB_STATUS_SUCCESS);

            if (Directory.Exists(lEvent.FullPath))
            {
                DirectoryInfo rootDir = new DirectoryInfo(lEvent.FullPath);
                WalkDirectoryTree(rootDir, lEvent.OldFullPath);
            }
        }

        private void UpdateDBForModifiedSuccess(LocalEvents lEvent, string strContentURi)
        {
            int nStatusCode = 0;
            string strEtag = cMezeoFileCloud.GetETag(strContentURi, ref nStatusCode);
            dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.E_TAG, strEtag, DbHandler.KEY, lEvent.FileName);

            FileInfo fileInfo = new FileInfo(lEvent.FullPath);
            if (fileInfo.Exists)
                dbHandler.UpdateModifiedDate(fileInfo.LastWriteTime, lEvent.FileName);

            UpdateDBForStatus(lEvent, DB_STATUS_SUCCESS);
        }

        private void UpdateKeyInDb(string oldKey, string newKey)
        {
            dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.KEY, newKey, DbHandler.KEY, oldKey);
        }

        #endregion

        # region Walk directory

        private void WalkDirectoryTreeForDelete(System.IO.DirectoryInfo root)
        {
            System.IO.FileInfo[] files = null;
            System.IO.DirectoryInfo[] subDirs = null;

            // First, process all the files directly under this folder
            try
            {
                files = root.GetFiles("*.*");
            }
            // This is thrown if even one of the files requires permissions greater
            // than the application provides.
            catch (UnauthorizedAccessException e)
            {
                LogWrapper.LogMessage("frmSyncManager - WalkDirectoryTreeForDelete", "Caught exception (UnauthorizedAccessException): " + e.Message);
            }

            catch (System.IO.DirectoryNotFoundException e)
            {
                LogWrapper.LogMessage("frmSyncManager - WalkDirectoryTreeForDelete", "Caught exception (DirectoryNotFoundException): " + e.Message);
            }

            if (files != null)
            {
                foreach (System.IO.FileInfo fi in files)
                {
                    string Key = fi.FullName.Substring(BasicInfo.SyncDirPath.Length + 1);
                    SendToRecycleBin(fi.FullName, true);
                   // File.Delete(fi.FullName);
                    dbHandler.Delete(DbHandler.TABLE_NAME, DbHandler.KEY , Key);
                }

                // Now find all the subdirectories under this directory.
                subDirs = root.GetDirectories();

                foreach (System.IO.DirectoryInfo dirInfo in subDirs)
                {
                    // Resursive call for each subdirectory.
                    WalkDirectoryTreeForDelete(dirInfo);

                    string Key = dirInfo.FullName.Substring(BasicInfo.SyncDirPath.Length + 1);
                    SendToRecycleBin(dirInfo.FullName, false);
                    //Directory.Delete(dirInfo.FullName);
                    dbHandler.Delete(DbHandler.TABLE_NAME, DbHandler.KEY , Key);
                }
            }
        }

        private void WalkDirectoryTreeforAddFolder(System.IO.DirectoryInfo root, string lEventOldPath, ref List<LocalEvents> addEvents, ref List<LocalEvents> events)
        {
            System.IO.FileInfo[] files = null;
            System.IO.DirectoryInfo[] subDirs = null;
            bool bIsAlreadyAdded = false;
            // First, process all the files directly under this folder
            try
            {
                files = root.GetFiles("*.*");
            }
            // This is thrown if even one of the files requires permissions greater
            // than the application provides.
            catch (UnauthorizedAccessException e)
            {
                LogWrapper.LogMessage("frmSyncManager - WalkDirectoryTreeforAddFolder", "Caught exception (UnauthorizedAccessException): " + e.Message);
            }

            catch (System.IO.DirectoryNotFoundException e)
            {
                LogWrapper.LogMessage("frmSyncManager - WalkDirectoryTreeforAddFolder", "Caught exception (DirectoryNotFoundException): " + e.Message);
            }

            if (files != null)
            {
                foreach (System.IO.FileInfo fi in files)
                {
                    bIsAlreadyAdded = false;
                    foreach (LocalEvents id in events)
                    {
                        if (id.FileName == lEventOldPath + "\\" + fi.Name && id.EventType == LocalEvents.EventsType.FILE_ACTION_ADDED)
                            bIsAlreadyAdded = true;
                    }

                    try
                    {
                        FileAttributes attr = File.GetAttributes(fi.FullName);
                        if ((attr & FileAttributes.Hidden) == FileAttributes.Hidden || (attr & FileAttributes.Temporary) == FileAttributes.Temporary)
                            bIsAlreadyAdded = true;
                    }
                    catch (Exception ex)
                    {
                        LogWrapper.LogMessage("frmSyncManager - WalkDirectoryTreeforAddFolder", "Caught exception: " + ex.Message);
                        bIsAlreadyAdded = true;
                    }

                    if (!bIsAlreadyAdded)
                    {
                        LocalEvents lEvent = new LocalEvents();
                        lEvent.FileName = lEventOldPath + "\\" + fi.Name;
                        lEvent.FullPath = fi.FullName;
                        lEvent.EventType = LocalEvents.EventsType.FILE_ACTION_ADDED;

                        addEvents.Add(lEvent);
                    }
                }

                // Now find all the subdirectories under this directory.
                subDirs = root.GetDirectories();

                foreach (System.IO.DirectoryInfo dirInfo in subDirs)
                {
                    bIsAlreadyAdded = false;
                    // Resursive call for each subdirectory.
                    foreach (LocalEvents id in events)
                    {
                        if (id.FileName == lEventOldPath + "\\" + dirInfo.Name && id.EventType == LocalEvents.EventsType.FILE_ACTION_ADDED)
                            bIsAlreadyAdded = true;
                    }

                    if (!bIsAlreadyAdded)
                    {
                        LocalEvents lEvent = new LocalEvents();
                        lEvent.FileName = lEventOldPath + "\\" + dirInfo.Name;
                        lEvent.FullPath = dirInfo.FullName;
                        lEvent.EventType = LocalEvents.EventsType.FILE_ACTION_ADDED;

                        addEvents.Add(lEvent);
                    }

                    WalkDirectoryTreeforAddFolder(dirInfo, lEventOldPath + "\\" + dirInfo.Name, ref addEvents, ref events);
                }
            }
        }

        private void WalkDirectoryTreeForMove(System.IO.DirectoryInfo root,string lEventOldPath)
        {
            System.IO.FileInfo[] files = null;
            System.IO.DirectoryInfo[] subDirs = null;

            // First, process all the files directly under this folder
            try
            {
                files = root.GetFiles("*.*");
            }
            // This is thrown if even one of the files requires permissions greater
            // than the application provides.
            catch (UnauthorizedAccessException e)
            {
                LogWrapper.LogMessage("frmSyncManager - WalkDirectoryTreeForMove", "Caught exception (UnauthorizedAccessException): " + e.Message);
            }

            catch (System.IO.DirectoryNotFoundException e)
            {
                LogWrapper.LogMessage("frmSyncManager - WalkDirectoryTreeForMove", "Caught exception (DirectoryNotFoundException): " + e.Message);
            }

            if (files != null)
            {
                foreach (System.IO.FileInfo fi in files)
                {
                   string newKey = fi.FullName.Substring(BasicInfo.SyncDirPath.Length + 1);
                   string oldKey = lEventOldPath.Substring(BasicInfo.SyncDirPath.Length + 1);
                   oldKey += "\\" + fi.Name;

                   string strParentDir = lEventOldPath.Substring(lEventOldPath.LastIndexOf("\\") + 1);
                    
                   dbHandler.Update(DbHandler.TABLE_NAME,DbHandler.KEY , newKey ,DbHandler.KEY , oldKey );

                }

                // Now find all the subdirectories under this directory.
                subDirs = root.GetDirectories();

                foreach (System.IO.DirectoryInfo dirInfo in subDirs)
                {
                    // Resursive call for each subdirectory.
                    WalkDirectoryTreeForMove(dirInfo, lEventOldPath + "\\" + dirInfo.Name);

                    string newKey = dirInfo.FullName.Substring(BasicInfo.SyncDirPath.Length + 1);
                    string oldKey = lEventOldPath.Substring(BasicInfo.SyncDirPath.Length + 1);
                    oldKey += "\\" + dirInfo.Name;

                    string strParentDir = lEventOldPath.Substring(lEventOldPath.LastIndexOf("\\") + 1);

                    dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.KEY , newKey , DbHandler.KEY , oldKey );
                }
            }
        }

        private void WalkDirectoryTree(System.IO.DirectoryInfo root, string lEventOldPath)
        {
            System.IO.FileInfo[] files = null;
            System.IO.DirectoryInfo[] subDirs = null;

            // First, process all the files directly under this folder
            try
            {
                files = root.GetFiles("*.*");
            }
            // This is thrown if even one of the files requires permissions greater
            // than the application provides.
            catch (UnauthorizedAccessException e)
            {
                LogWrapper.LogMessage("frmSyncManager - WalkDirectoryTree", "Caught exception (UnauthorizedAccessException): " + e.Message);
            }

            catch (System.IO.DirectoryNotFoundException e)
            {
                LogWrapper.LogMessage("frmSyncManager - WalkDirectoryTree", "Caught exception (DirectoryNotFoundException): " + e.Message);
            }

            if (files != null)
            {
                foreach (System.IO.FileInfo fi in files)
                {
                    string newKey = fi.FullName.Substring(BasicInfo.SyncDirPath.Length + 1);
                    string oldKey = lEventOldPath.Substring(BasicInfo.SyncDirPath.Length + 1);
                    oldKey += "\\" + fi.Name;

                    string strParentDir = lEventOldPath.Substring(lEventOldPath.LastIndexOf("\\") + 1);

                    dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.KEY , newKey , DbHandler.KEY , oldKey );
                    dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.PARENT_DIR , strParentDir , DbHandler.KEY , newKey );

                }

                // Now find all the subdirectories under this directory.
                subDirs = root.GetDirectories();

                foreach (System.IO.DirectoryInfo dirInfo in subDirs)
                {
                    // Resursive call for each subdirectory.
                    WalkDirectoryTree(dirInfo, lEventOldPath + "\\" + dirInfo.Name);

                    string newKey = dirInfo.FullName.Substring(BasicInfo.SyncDirPath.Length + 1);
                    string oldKey = lEventOldPath.Substring(BasicInfo.SyncDirPath.Length + 1);
                    oldKey += "\\" + dirInfo.Name;

                    string strParentDir = lEventOldPath.Substring(lEventOldPath.LastIndexOf("\\") + 1);

                    dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.KEY , newKey , DbHandler.KEY ,oldKey );
                    dbHandler.Update(DbHandler.TABLE_NAME, DbHandler.PARENT_DIR , strParentDir , DbHandler.KEY ,newKey );
                }
            }
        }

        # endregion

  
        # region bwSyncThread Functions

        private void bwSyncThread_DoWork(object sender, DoWorkEventArgs e)
        {
            //LogWrapper.LogMessage("frmSyncManager - bwOffilneEvent_DoWork", "enter");
            int statusCode = RunSyncLoop(sender, e);
            e.Result = statusCode;
            //LogWrapper.LogMessage("frmSyncManager - bwOffilneEvent_DoWork", "leave");
        }

        private void bwSyncThread_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
                //LogWrapper.LogMessage("frmSyncManager - bwLocalEvents_ProgressChanged", "enter");
                if (this.InvokeRequired)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        label1.Visible = false;
                        pbSyncProgress.Refresh();

                        if (!lblPercentDone.Visible)
                        {
                            lblPercentDone.Text = "";
                            Application.DoEvents();
                        }

                        if (e.ProgressPercentage == SYNC_STARTED)
                        {
                            ShowInitialSyncMessage();
                        }
                        else if (e.ProgressPercentage == PROCESS_LOCAL_EVENTS_STARTED)
                        {
                            InitializeLocalEventsProcess((int)e.UserState);
                        }
                        else if (e.ProgressPercentage == PROGRESS_CHANGED_WITH_FILE_NAME)
                        {
                            showProgress();
                            //lblStatusL3.Text = e.UserState.ToString();
                        }
                        else if (e.ProgressPercentage == LOCAL_EVENTS_COMPLETED)
                        {
                            if (CanNotTalkToServer())
                            {
                                lastSync = DateTime.Now;
                                BasicInfo.LastSyncAt = lastSync;
                            }
                            else
                            {
                                ShowLocalEventsCompletedMessage();
                            }
                        }
                        else if (e.ProgressPercentage == LOCAL_EVENTS_STOPPED)
                        {
                            ShowSyncMessage(EventQueue.QueueNotEmpty());
                        }
                    });
                }
                else
                {
                    label1.Visible = false;
                    pbSyncProgress.Refresh();

                    if (!lblPercentDone.Visible)
                    {
                        lblPercentDone.Text = "";
                        Application.DoEvents();
                    }

                    if (e.ProgressPercentage == SYNC_STARTED)
                    {
                        ShowInitialSyncMessage();
                    }
                    else if (e.ProgressPercentage == PROCESS_LOCAL_EVENTS_STARTED)
                    {
                        InitializeLocalEventsProcess((int)e.UserState);
                    }
                    else if (e.ProgressPercentage == PROGRESS_CHANGED_WITH_FILE_NAME)
                    {
                        showProgress();
                        //lblStatusL3.Text = e.UserState.ToString();
                    }
                    else if (e.ProgressPercentage == LOCAL_EVENTS_COMPLETED)
                    {
                        if (CanNotTalkToServer())
                        {
                            lastSync = DateTime.Now;
                            BasicInfo.LastSyncAt = lastSync;
                        }
                        else
                        {
                            ShowLocalEventsCompletedMessage();
                        }
                    }
                    else if (e.ProgressPercentage == LOCAL_EVENTS_STOPPED)
                    {
                        ShowSyncMessage(EventQueue.QueueNotEmpty());
                    }
                }

                //Application.DoEvents();
                //LogWrapper.LogMessage("frmSyncManager - bwLocalEvents_ProgressChanged", "leave");
        }

        private void bwSyncThread_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            //LogWrapper.LogMessage("frmSyncManager - bwNQUpdate_RunWorkerCompleted", "enter");
            //ShowSyncMessage(IsEventCanceled());
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    //tmrNextSync.Start();
                    tmrNextSync.Interval = getSynNextCycleTimer();
                });
            }
            else
            {
                //tmrNextSync.Start();
                tmrNextSync.Interval = getSynNextCycleTimer();
            }

            // Set the job count to whatever is left in the queue.
            // May not be 0 if the sync was paused/stopped.
            dbHandler.ResetJobCount();

            //SetIsEventCanceled(false);
            try
            {
                if (e.Result != null && (CancelReason)e.Result == CancelReason.LOGIN_FAILED)
                {
                    this.Hide();
                    frmParent.ShowLoginAgainFromSyncMgr();
                }
                else if (e.Result != null && (CancelReason)e.Result == CancelReason.SERVER_INACCESSIBLE)
                {
                    SetCanNotTalkToServer(true);
                    // DisableSyncManager();
                    // ShowSyncManagerOffline();
                    // CheckServerStatus(); TODO:check for offline (Modified for server status thread)
                }
                else
                {
                    queue_WatchCompletedEvent();
                }

                resetAllControls();
            }
            catch (Exception ex)
            {
                LogWrapper.LogMessage("frmSyncManager - bwNQUpdate_RunWorkerCompleted", "Caught exception: " + ex.Message);
            }
            //LogWrapper.LogMessage("frmSyncManager - bwNQUpdate_RunWorkerCompleted", "leave");
        }

        #endregion


        # region bwUpdateUsage Thread Functions

        private void bwUpdateUsage_DoWork(object sender, DoWorkEventArgs e)
        {
            //LogWrapper.LogMessage("frmSyncManager - bwUpdateUsage_DoWork", "enter");
            e.Result = GetUsageString();
            //LogWrapper.LogMessage("frmSyncManager - bwUpdateUsage_DoWork", "leave");
        }

        private void bwUpdateUsage_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            //LogWrapper.LogMessage("frmSyncManager - bwUpdateUsage_RunWorkerCompleted", "enter");
            lblUsageDetails.Text = e.Result.ToString();
            //LogWrapper.LogMessage("frmSyncManager - bwUpdateUsage_RunWorkerCompleted", "leave");
        }

        #endregion

    }
}
