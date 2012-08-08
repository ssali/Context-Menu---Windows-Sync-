using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Drawing;

namespace Mezeo
{
    public class NotificationManager
    {
        // The OS sets a range (typically from 10-30 seconds).  Anything outside of
        // the range will be forced into the range.
        // http://msdn.microsoft.com/en-us/library/ms160064.aspx
        private const int BALLON_TIME_OUT = 10000; // time in milli seconds

        private NotifyIcon cNotifyIcon;
        private string hoverText = "";
        private string ballonText = "";
        private string ballonTitleText = "";

        //private Icon trayIcon = Properties.Resources.MezeoVault;
        //fogbugzid: 2795 Sync icon should not be in green state before login
        private Icon trayIcon = Properties.Resources.app_offline;

        public NotifyIcon NotificationHandler
        {
            get
            {
                return cNotifyIcon;
            }

            set
            {
                cNotifyIcon = value;
                cNotifyIcon.Text = hoverText;
                cNotifyIcon.BalloonTipText = ballonText;
                cNotifyIcon.BalloonTipTitle = ballonTitleText;
                cNotifyIcon.BalloonTipIcon = ToolTipIcon.None;
                cNotifyIcon.Icon = trayIcon;
            }
        }

        public string HoverText
        {
            get
            {
                return hoverText;
            }
            set
            {
                hoverText = value;
                // The text is limited to 64 characters.
                if (hoverText.Length > 64)
                    hoverText = hoverText.Substring(0, 64);
                cNotifyIcon.Text = hoverText;
            }
        }

        public string BallonText
        {
            get
            {
                return ballonText;
            }
            set
            {
                ballonText = value;
                cNotifyIcon.BalloonTipText = ballonText;
            }
        }

        public string BallonTipTitle
        {
            get
            {
                return ballonTitleText;
            }
            set
            {
                ballonTitleText = value;
                cNotifyIcon.BalloonTipTitle = ballonTitleText;
            }
        }

        public Icon NotifyIcon
        {
            get
            {
                return trayIcon;
            }
            set
            {
                trayIcon = value;
                cNotifyIcon.Icon = trayIcon;
            }
        }

        public void ShowBallon()
        {
            cNotifyIcon.ShowBalloonTip(BALLON_TIME_OUT);
        }
    }
}
