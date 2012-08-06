using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;

namespace Mezeo
{
    /// <summary>
    /// The Watcher class is responsible for gathering up the local file events
    /// that occur in the sync directory, and all child directories, and adding
    /// them to the EventQueue for further processing.
    /// </summary>
    class Watcher
    {
        FileSystemWatcher fileWatcher;  ///< The FileSystemWatcher instance that will gather events for us to queue up.
        string folderToWatch;           ///< The full path of the local sync folder where local file events are of interest.

        /// <summary>
        /// The constructor for the class.  The internal buffer size for fileWatcher
        /// is bumped up to 64k in order to allow the OS to queue up as many local
        /// file events as possible without losing any.  Event loss is still possible
        /// though and this should not be treated as a fix-all for the issue.
        /// We also register all of the file event types that we are interested in
        /// receiving.  Specifically the changed, created, deleted, and renamed events.
        /// </summary>
        /// @todo Use the modified event for folders as a hint to walk the entire folder and make sure no events were dropped/missed.
        /// <param name="folder">is the complete path to the folder to gather events for.</param>
        public Watcher(string folder)
        {
            this.folderToWatch = folder;
            fileWatcher = new FileSystemWatcher(folderToWatch);

            fileWatcher.InternalBufferSize = 64 * 1024;
            fileWatcher.EnableRaisingEvents = false;
            fileWatcher.Filter = "*.*";
            fileWatcher.IncludeSubdirectories = true;
            fileWatcher.NotifyFilter = NotifyFilters.Attributes | NotifyFilters.LastWrite | NotifyFilters.DirectoryName | NotifyFilters.FileName;

            fileWatcher.Changed += new FileSystemEventHandler(fileWatcher_Changed);
            fileWatcher.Created += new FileSystemEventHandler(fileWatcher_Created);
            fileWatcher.Deleted += new FileSystemEventHandler(fileWatcher_Deleted);
            fileWatcher.Renamed += new RenamedEventHandler(fileWatcher_Renamed);
        }

        /// <summary>
        /// A method to start monitoring the sync directory.
        /// </summary>
        public void StartMonitor()
        {
            fileWatcher.EnableRaisingEvents = true;
        }

        /// <summary>
        /// Stop the fileWatcher from gathering any more local file events.
        /// </summary>
        public void StopMonitor()
        {
            fileWatcher.EnableRaisingEvents = false;
            fileWatcher.Dispose();
        }

        /// <summary>
        /// The fileWatcher is notifying us that it has received a fileWatcher_Renamed event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void fileWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            FillEventsQueue(e, true, LocalEvents.EventsType.FILE_ACTION_RENAMED);
        }

        /// <summary>
        /// The fileWatcher is notifying us that it has received a fileWatcher_Deleted event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void fileWatcher_Deleted(object sender, FileSystemEventArgs e)
        {
            FillEventsQueue(e, false, LocalEvents.EventsType.FILE_ACTION_REMOVED);           
        }

        /// <summary>
        /// The fileWatcher is notifying us that it has received a fileWatcher_Created event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void fileWatcher_Created(object sender, FileSystemEventArgs e)
        {
            FillEventsQueue(e, false, LocalEvents.EventsType.FILE_ACTION_ADDED);            
        }

        /// <summary>
        /// The fileWatcher is notifying us that it has received a fileWatcher_Changed event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void fileWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            FillEventsQueue(e, false, LocalEvents.EventsType.FILE_ACTION_MODIFIED);            
        }

        /// <summary>
        /// Populate a LocalEvents object and add it to the event job queue for
        /// further processing.
        /// </summary>
        /// <param name="e">is the actual event supplied by fileWatcher.</param>
        /// <param name="isRename">is set to True if this event is a RenamedEventArgs.</param>
        /// <param name="eventType">is a LocalEvents.EventsType value so that others know what action generated the event.</param>
        public void FillEventsQueue(EventArgs e,bool isRename, LocalEvents.EventsType eventType)
        {
            LocalEvents lEvent = new LocalEvents();
            DateTime eventTime = DateTime.Now;

            if(isRename)
            {
                RenamedEventArgs rArgs=(RenamedEventArgs)e;
                lEvent.FileName = rArgs.Name;
                lEvent.FullPath = rArgs.FullPath;
                lEvent.OldFileName = rArgs.OldName;
                lEvent.OldFullPath = rArgs.OldFullPath;
                lEvent.EventType = eventType;
                lEvent.EventTimeStamp = eventTime;
            }
            else
            {
                FileSystemEventArgs rArgs = (FileSystemEventArgs)e;
                lEvent.FileName = rArgs.Name;
                lEvent.FullPath = rArgs.FullPath;
                lEvent.OldFileName = "";
                lEvent.OldFullPath = "";
                lEvent.EventType = eventType;
                lEvent.EventTimeStamp = eventTime;
            }

            EventQueue.Add(lEvent);
        }
    }
}
