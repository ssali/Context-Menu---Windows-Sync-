using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Data;  // Needed to use the DbHandler stuff.

namespace Mezeo
{
    public static class EventQueue
    {
        private static DbHandler dbHandler;
        private static String m_strMacAdd;
        private static String m_strOriginal;
        private static String m_strPartial;

        // Time without receiving events for a resource before
        // it is moved from evenListCandidates to eventList.
        //private static int TIME_WITHOUT_EVENTS = 20000;  // 20 seconds as milliseconds.
        private static int TIME_WITHOUT_EVENTS = Convert.ToInt32(global::Mezeo.Properties.Resources.BrSettleTimer);
        // 20 seconds as 20000 milliseconds.
        
        // A list of events that have not had any activity in the last TIME_WITHOUT_EVENTS.
        private static List<LocalEvents> eventList = new List<LocalEvents>();

        // A list of events for resources that are still receiving/generating events.
        private static List<LocalEvents> eventListCandidates = new List<LocalEvents>();

        private static Object thisLock = new Object();
        private static System.Timers.Timer timer;
        public delegate void WatchCompleted();
        public static event WatchCompleted WatchCompletedEvent;

        public static void InitEventQueue(String strMacAdd)
        {
            timer = new System.Timers.Timer();
            timer.Elapsed += new System.Timers.ElapsedEventHandler(EventQueue.timer_Elapsed);
            timer.Interval = TIME_WITHOUT_EVENTS;
            timer.AutoReset = true;
            timer.Enabled = true;

            m_strMacAdd = strMacAdd;
            m_strOriginal = ".original." + m_strMacAdd;
            m_strPartial = ".partial." + m_strMacAdd;
        }

        private static DbHandler GetDbHandler()
        {
            if (dbHandler == null)
            {
                dbHandler = new DbHandler();
                dbHandler.OpenConnection();
            }
            return dbHandler;
        }

        // Collate the events before finally releasing them to be acted on.
        public static void CollateEvents()
        {
            // Go through the list of events and see what can be pruned out.
            // For now, just look at items that are REMOVED.  If they don't
            // exist in the database, then they were never created in the
            // cloud and all related events can be removed as a NOOP.

            if (0 < eventList.Count)
            {
                // Loop backwards theough the list.
                // If I find a REMOVED event, then keep track of it.
                List<LocalEvents> eventsToRemove = new List<LocalEvents>();
                for (int index = eventList.Count - 1; index >= 0; index--)
                {
                    if (eventList[index].EventType == LocalEvents.EventsType.FILE_ACTION_REMOVED)
                    {
                        string strCheck = GetDbHandler().GetString(DbHandler.TABLE_NAME, DbHandler.CONTENT_URL, new string[] { DbHandler.KEY }, new string[] { eventList[index].FileName }, new DbType[] { DbType.String });
                        if (0 == strCheck.Length)
                        {
                            LogWrapper.LogMessage("EventQueue - CollateEvents", "Event for item no in the database: (" + eventList[index].EventType + ") " + eventList[index].FullPath + " name: " + eventList[index].FileName);
                            // Since this item isn't in the database (and so not on the cloud),
                            // then we can throw out any events for this item since in the
                            // end it was deleted anyway. 
                            LocalEvents lEvent = new LocalEvents();
                            lEvent = eventList[index];
                            eventsToRemove.Add(lEvent);

                            // Now look for related events (before this event) and throw them out as well.
                            for (int indexRemove = 0; indexRemove < index; indexRemove++)
                            {
                                if ((eventList[indexRemove].FileName == lEvent.FileName) && (eventList[indexRemove].FullPath == lEvent.FullPath))
                                {
                                    LogWrapper.LogMessage("EventQueue - CollateEvents", "Removing event: (" + eventList[indexRemove].EventType + ") " + eventList[indexRemove].FullPath);
                                    LocalEvents relatedEvent = new LocalEvents();
                                    relatedEvent = eventList[indexRemove];
                                    eventsToRemove.Add(relatedEvent);
                                }
                            }
                        }
                    }
                }

                if (0 != eventsToRemove.Count)
                {
                    // Each event that was moved to eventList must
                    // be removed from eventListCandidates.
                    foreach (LocalEvents id in eventsToRemove)
                    {
                        eventList.Remove(id);
                    }
                    eventsToRemove.Clear();
                }
            }
        }

        private static bool IsFileLocked(String filePath)
        {
            FileStream stream = null;
            FileInfo file = new FileInfo(filePath);

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

        public static void timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            bool bNewEventExists = false;
            DateTime currTime = DateTime.Now;
            List<LocalEvents> eventsToRemove = new List<LocalEvents>();

            // Check the event candidate list and see which events should be moved.
            lock (thisLock)
            {
                bool copyEvent = true;
                // If the resource has not had an event in the last X timespan,
                // move it from the eventListCandidates to eventList.
                foreach (LocalEvents id in eventListCandidates)
                {
                    copyEvent = false;
                    TimeSpan diff = currTime - id.EventTimeStamp;
                    if (TIME_WITHOUT_EVENTS <= diff.TotalMilliseconds)
                    {
                        // If this is a file that's going to be uploaded, then make sure it isn't locked.
                        if (id.IsFile && ((id.EventType == LocalEvents.EventsType.FILE_ACTION_ADDED) || (id.EventType == LocalEvents.EventsType.FILE_ACTION_MODIFIED)))
                        {
                            if (!IsFileLocked(id.FullPath))
                                copyEvent = true;
                        }
                        else
                            copyEvent = true;

                        if (copyEvent)
                        {
                            LocalEvents lEvent = new LocalEvents();
                            lEvent = id;
                            lEvent.EventTimeStamp = id.EventTimeStamp;
                            eventList.Add(lEvent);
                            eventsToRemove.Add(id);
                            //dbHandler.AddEvent(lEvent);
                        }
                    }
                }

                // Each event that was moved to eventList must
                // be removed from eventListCandidates.
                foreach (LocalEvents id in eventsToRemove)
                {
                    eventListCandidates.Remove(id);
                }

                eventsToRemove.Clear();

                // Collate the events before finally releasing them to be acted on.
                CollateEvents();

                // If there is anything left after collating the events, then
                // move them from the list to the database.
                if (0 < eventList.Count())
                {
                    // Move the events to the database.
                    foreach (LocalEvents item in eventList)
                    {
                        bNewEventExists = true;
                        GetDbHandler().AddEvent(item);
                    }

                    // Empty the list.
                    eventList.Clear();
                }
            }

            // If something was added to the list, trigger the event.
            if (bNewEventExists)
            {
                if (WatchCompletedEvent != null)
                {
                    if (EventQueue.QueueNotEmpty())
                        WatchCompletedEvent();
                }
            }
        }

        public static bool QueueNotEmpty()
        {
            Int64 jobCount = GetDbHandler().GetJobCount();
            return (jobCount > 0);
        }

        public static LocalEvents GetCurrentQueueItem()
        {
            LocalEvents localEvent = null;
            lock (thisLock)
            {
                localEvent = GetDbHandler().GetLocalEvent();
            }
            return localEvent;
        }

        public static void FillInFileInfo(ref LocalEvents theEvent)
        {
            try
            {
                FileInfo fileInfo = new FileInfo(theEvent.FullPath);

                theEvent.Attributes = fileInfo.Attributes;
                LogWrapper.LogMessage("EventQueue - FillInFileInfo", "File " + theEvent.FullPath + " attributes are: " + fileInfo.Attributes.ToString());
                if (fileInfo.Exists)
                {
                    if (0 == (fileInfo.Attributes & FileAttributes.Directory))
                    {
                        theEvent.IsDirectory = false;
                        theEvent.IsFile = true;
                    }
                    else
                    {
                        theEvent.IsDirectory = true;
                        theEvent.IsFile = false;
                    }
                }
                else
                {
                    theEvent.IsDirectory = true;
                    theEvent.IsFile = false;
                }
            }
            catch (Exception ex)
            {
                LogWrapper.LogMessage("EventQueue - FillInFileInfo", "Caught exception: " + ex.Message);
            }
        }

        public static void Add(LocalEvents newEvent)
        {
            // Ignore events for anything that has .partial.m_strMacAdd or .original.m_strMacAdd in the name.
            if (newEvent.FileName.Contains(m_strOriginal) || newEvent.FileName.Contains(m_strPartial) ||
                newEvent.OldFileName.Contains(m_strOriginal) || newEvent.OldFileName.Contains(m_strPartial))
            {
                LogWrapper.LogMessage("EventQueue - Add", "Ignoring event: (" + newEvent.EventType + ") " + newEvent.FullPath);
                return;
            }

            int indexToRemove = -1;

            LogWrapper.LogMessage("EventQueue - Add", "Adding event: (" + newEvent.EventType + ") " + newEvent.FullPath);
            if (newEvent.EventType == LocalEvents.EventsType.FILE_ACTION_RENAMED)
            {
                LogWrapper.LogMessage("EventQueue - Add", "              (" + newEvent.EventType + ") old path:" + newEvent.OldFullPath);
                LogWrapper.LogMessage("EventQueue - Add", "              (" + newEvent.EventType + ") new path:" + newEvent.FullPath);
            }

            // Only fill in this information if the event isn't a DELETE event.
            // Otherwise, the file/folder isn't there to get the info for.
            if (newEvent.EventType != LocalEvents.EventsType.FILE_ACTION_REMOVED)
            {
                FillInFileInfo(ref newEvent);
            }

            lock (thisLock)
            {
                bool bAdd = true;

                // Only FILE_ACTION_MODIFIED events are possibly ignored, so they are the only ones that
                // we should spend the effort/time to loop through the list for.
                if (newEvent.EventType == LocalEvents.EventsType.FILE_ACTION_MODIFIED)
                {
                    foreach (LocalEvents id in eventListCandidates)
                    {
                        if (id.FileName == newEvent.FileName)
                        {
                            if (newEvent.IsFile)
                            {
                                // If a event type is added, removed, renamed, or moved, then go ahead and accept the event.
                                // If the new event is MODIFIED and the existing is ADDED, then update the timestamp of the
                                // existing event, but don't add it to the list.
                                if ((id.EventType == LocalEvents.EventsType.FILE_ACTION_ADDED) ||
                                    (id.EventType == LocalEvents.EventsType.FILE_ACTION_MODIFIED) ||
                                    (id.EventType == LocalEvents.EventsType.FILE_ACTION_RENAMED)
                                    )
                                {
                                    LogWrapper.LogMessage("EventQueue - Add", "Local event already exists for: " + newEvent.FullPath);
                                    id.EventTimeStamp = newEvent.EventTimeStamp;
                                    bAdd = false;
                                    break;
                                }
                            }
                            else
                            {
                                // For directories, ignore any FILE_ACTION_MODIFIED events.  They should not reset the time either.
                                LogWrapper.LogMessage("EventQueue - Add", "Local event already exists for: " + newEvent.FullPath);
                                bAdd = false;
                                break;
                            }
                        }
                    }
                }
                else if (newEvent.EventType == LocalEvents.EventsType.FILE_ACTION_RENAMED)
                {
                    if (newEvent.IsFile)
                    {
                        // If something locally has been renamed, then look through the existing events
                        // and see if there is a 'created' event for the old path.  If so, then the
                        // existing event MUST be changed so the path reflects the new path.  Otherwise
                        // no file will be uploaded (the local file has been renamed) and the rename
                        // won't occur on the server since the file wasn't uploaded.
                        bool foundAddEvent = false;
                        foreach (LocalEvents id in eventListCandidates)
                        {
                            if ((id.FileName == newEvent.OldFileName) && (id.EventType == LocalEvents.EventsType.FILE_ACTION_ADDED))
                            {
                                // Set the path to the new path so the correct folder is created or file is uploaded.
                                // Set the event to FILE_ACTION_MODIFIED so that a new version of the file is uploaded.
                                LogWrapper.LogMessage("EventQueue - Add", "Local ADDED event already exists for: " + newEvent.FullPath);
                                LogWrapper.LogMessage("EventQueue - Add", "Changing existing path from " + id.FullPath + " to " + newEvent.FullPath);
                                LogWrapper.LogMessage("EventQueue - Add", "Changing existing event from " + id.EventType + " to FILE_ACTION_MODIFIED");
                                id.FullPath = newEvent.FullPath;
                                id.FileName = newEvent.FileName;
                                id.EventType = LocalEvents.EventsType.FILE_ACTION_MODIFIED;
                                bAdd = false;
                                foundAddEvent = true;
                            }
                            if (foundAddEvent)
                            {
                                // If a FILE_ACTION_ADDED event was modified, then we keep looking for a FILE_ACTION_REMOVED
                                // action as well and remove/change it to something else.
                                if ((id.FileName == newEvent.FileName) && (id.EventType == LocalEvents.EventsType.FILE_ACTION_REMOVED))
                                {
                                    // The delete action for the file should be deleted since it would delete the file
                                    // that will now be uploaded.
                                    indexToRemove = eventListCandidates.IndexOf(id);
                                    LogWrapper.LogMessage("EventQueue - Add", "Removing delete action for: " + newEvent.FullPath);
                                    break;
                                }
                            }
                        }
                    }
                }
                else if (newEvent.EventType == LocalEvents.EventsType.FILE_ACTION_ADDED)
                {
                    // The 'Save As' from editors like Notepad execute a strange series of events.  Specifically,
                    // FILE_ACTION_ADDED, FILE_ACTION_REMOVED, FILE_ACTION_ADDED, and FILE_ACTION_MODIFIED.
                    // If the ADDED action finds both the ADDED and REMOVED actions earlier in the list, then
                    // modify the first ADDED event, remove the REMOVED, and throw this event away.
                    bool foundAdded = false;
                    bool foundRemoved = false;
                    int indexAdded = -1;
                    int indexRemoved = -1;
                    foreach (LocalEvents id in eventListCandidates)
                    {
                        if ((id.EventType == LocalEvents.EventsType.FILE_ACTION_ADDED) && (id.FullPath == newEvent.FullPath))
                        {
                            LogWrapper.LogMessage("EventQueue - Add", "Found existing FILE_ACTION_ADDED (save seq) for: " + newEvent.FullPath);
                            foundAdded = true;
                            indexAdded = eventListCandidates.IndexOf(id);
                        }
                        if (foundAdded)
                        {
                            if ((id.EventType == LocalEvents.EventsType.FILE_ACTION_REMOVED) && (id.FullPath == newEvent.FullPath))
                            {
                                LogWrapper.LogMessage("EventQueue - Add", "Found existing FILE_ACTION_REMOVED (save seq) for: " + newEvent.FullPath);
                                foundRemoved = true;
                                indexRemoved = eventListCandidates.IndexOf(id);
                            }
                        }

                        // We found what we were looking for so we can exit the loop.
                        if (foundAdded && foundRemoved)
                            break;
                    }

                    if (foundAdded && foundRemoved)
                    {
                        // If I found the two events (in the correct order), then I need to modify
                        // the Added entry, remove the Removed entry, and throw this entry away.
                        bAdd = false;
                        indexToRemove = indexRemoved;
                        eventListCandidates[indexAdded].EventTimeStamp = newEvent.EventTimeStamp;
                        eventListCandidates[indexAdded].IsDirectory = newEvent.IsDirectory;
                        eventListCandidates[indexAdded].IsFile = newEvent.IsFile;
                        eventListCandidates[indexAdded].Attributes = newEvent.Attributes;
                        LogWrapper.LogMessage("EventQueue - Add", "Updating existing FILE_ACTION_ADD for: " + newEvent.FullPath);
                        LogWrapper.LogMessage("EventQueue - Add", "Removing existing FILE_ACTION_REMOVED for: " + newEvent.FullPath);
                        LogWrapper.LogMessage("EventQueue - Add", "Ignoring new FILE_ACTION_ADDED for: " + newEvent.FullPath);
                    }
                    else // Look for a move event.
                    {
                        string fileName = newEvent.FileName.Substring(newEvent.FileName.LastIndexOf("\\") + 1);

                        foundRemoved = false;
                        indexRemoved = -1;
                        LogWrapper.LogMessage("EventQueue - Add", "Looking for a MOVE sequence.");
                        for (int loopIndex = eventListCandidates.Count - 1; loopIndex >= 0; loopIndex--)
                        {
                            string eventFileName = eventListCandidates[loopIndex].FileName.Substring(eventListCandidates[loopIndex].FileName.LastIndexOf("\\") + 1);
                            LogWrapper.LogMessage("EventQueue - Add", "Comparing (" + eventListCandidates[loopIndex].EventType.ToString() + ") " + eventFileName + " to " + fileName);
                            if ((eventListCandidates[loopIndex].EventType == LocalEvents.EventsType.FILE_ACTION_REMOVED) && (0 == eventFileName.CompareTo(fileName)))
                            {
                                LogWrapper.LogMessage("EventQueue - Add", "Found existing FILE_ACTION_REMOVED (move seq) for: " + newEvent.FullPath);
                                indexRemoved = loopIndex;
                                foundRemoved = true;

                                // We found what we were looking for so we can exit the loop.
                                break;
                            }
                        }

                        if (foundRemoved)
                        {
                            // If an item has an ADDED and REMOVED action within a VERY short time
                            // period then the item was probably MOVED/RENAMED.  Adjust the existing
                            // REMOVED action accordingly and ignore the ADDED action.
                            TimeSpan diff = newEvent.EventTimeStamp - eventListCandidates[indexRemoved].EventTimeStamp;

                            LogWrapper.LogMessage("EventQueue - Add", "Time diff (move seq) is : " + diff.TotalMilliseconds + "ms");
                            if (100 >= diff.TotalMilliseconds)
                            {
                                LogWrapper.LogMessage("EventQueue - Add", "Time diff is small so Assuming " + eventListCandidates[indexRemoved].FullPath + " was MOVED to " + newEvent.FullPath);
                                // Change the REMOVED event to a MOVE.
                                eventListCandidates[indexRemoved].EventType = LocalEvents.EventsType.FILE_ACTION_MOVE;

                                // Update the old path, full path, and time.
                                eventListCandidates[indexRemoved].OldFileName = eventListCandidates[indexRemoved].FileName;
                                eventListCandidates[indexRemoved].FileName = newEvent.FileName;
                                eventListCandidates[indexRemoved].OldFullPath = eventListCandidates[indexRemoved].FullPath;
                                eventListCandidates[indexRemoved].FullPath = newEvent.FullPath;
                                eventListCandidates[indexRemoved].IsDirectory = newEvent.IsDirectory;
                                eventListCandidates[indexRemoved].IsFile = newEvent.IsFile;
                                eventListCandidates[indexRemoved].Attributes = newEvent.Attributes;

                                // Ignore this event since it's really a move.
                                bAdd = false;

                                LogWrapper.LogMessage("EventQueue - Add", "Updating existing FILE_ACTION_REMOVED for: " + eventListCandidates[indexRemoved].FullPath + " to be FILE_ACTION_MOVE.");
                                LogWrapper.LogMessage("EventQueue - Add", "Ignoring new FILE_ACTION_ADDED for: " + newEvent.FullPath);
                            }
                        }
                    }
                }

                if (-1 != indexToRemove)
                {
                    eventListCandidates.RemoveAt(indexToRemove);
                }

                if (bAdd)
                {
                    if (1 == newEvent.EventTimeStamp.Year)
                        newEvent.EventTimeStamp = DateTime.Now;
                    eventListCandidates.Add(newEvent);
                }
            }
        }
    }
}
