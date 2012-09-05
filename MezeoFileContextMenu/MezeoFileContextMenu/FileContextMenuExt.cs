/********************************** Module Header **********************************\
Module Name:  FileContextMenuExt.cs

The FileContextMenuExt.cs file defines a context menu handler by implementing the 
IShellExtInit and IContextMenu interfaces.

\***********************************************************************************/

#region Using directives
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using MezeoFileShellExt.Properties;
using System.Drawing;
using System.IO;
using System.Diagnostics;
#endregion


namespace MezeoFileShellExt
{
    [ClassInterface(ClassInterfaceType.None)]
    [Guid("B1F1405D-94A1-4692-B72F-FC8CAF8B8700"), ComVisible(true)]

    public class FileContextMenuExt : IShellExtInit, IContextMenu
    { 
        // The name of the selected file.
        private string selectedFile;
   
        private string menuText = "&" + Resources.BrContextMenuTitle;
        private IntPtr menuBmp = IntPtr.Zero;
        private string verb = "csdisplay";
        private string verbCanonicalName = "CSDisplayFileName";
        private string verbHelpText = Resources.BrContextMenuTitle;
        private uint IDM_DISPLAY = 0;
        uint cmdFirst;

        // All Verbs for Context Menu
        string[] syncVerbCanonicalName = null;     

        public FileContextMenuExt()
        {
            // Load the bitmap for the menu item.
            Bitmap bmp = Resources.ContextMenuIcon;
            bmp.MakeTransparent(bmp.GetPixel(0, 0));
            this.menuBmp = bmp.GetHbitmap();
            // Write the string to a file.
            Debug.WriteLine("Constructor Start");
        }

        ~FileContextMenuExt()
        {
            if (this.menuBmp != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(this.menuBmp);
                this.menuBmp = IntPtr.Zero;
            }
        }


        void OnVerbDisplayFileName(IntPtr hWnd)
        {
            Debug.WriteLine("Context menu click");
            UserInterface form2 = new UserInterface();
            form2.ShowDialog();
        }

        // MessageBo
    [DllImport("user32")]
    static extern int MessageBox(int hWnd, string text, string caption, int type);

        #region Shell Extension Registration

        [ComRegisterFunction()]
        public static void Register(Type t)
        {
            try
            {
                //ShellExtReg.RegisterShellExtContextMenuHandler(t.GUID,"*","MezeoFileShellExt");
                ShellExtReg.RegisterShellExtContextMenuHandler(t.GUID, "*", Resources.BrContextMenuProduct);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message); // Log the error
                throw;  // Re-throw the exception
            }
        }

        [ComUnregisterFunction()]
        public static void Unregister(Type t)
        {
            try
            {
                //ShellExtReg.UnregisterShellExtContextMenuHandler(t.GUID, "*", "MezeoFileShellExt");
                ShellExtReg.UnregisterShellExtContextMenuHandler(t.GUID, "*", Resources.BrContextMenuProduct);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message); // Log the error
                throw;  // Re-throw the exception
            }
        }

        #endregion


        #region IShellExtInit Members

        /// <summary>
        /// Initialize the context menu handler.
        /// </summary>
        /// <param name="pidlFolder">
        /// A pointer to an ITEMIDLIST structure that uniquely identifies a folder.
        /// </param>
        /// <param name="pDataObj">
        /// A pointer to an IDataObject interface object that can be used to retrieve 
        /// the objects being acted upon.
        /// </param>
        /// <param name="hKeyProgID">
        /// The registry key for the file object or folder type.
        /// </param>
        public void Initialize(IntPtr pidlFolder, IntPtr pDataObj, IntPtr hKeyProgID)
        {
            if (pDataObj == IntPtr.Zero)
            {
                throw new ArgumentException();
            }

            FORMATETC fe = new FORMATETC();
            fe.cfFormat = (short)CLIPFORMAT.CF_HDROP;
            fe.ptd = IntPtr.Zero;
            fe.dwAspect = DVASPECT.DVASPECT_CONTENT;
            fe.lindex = -1;
            fe.tymed = TYMED.TYMED_HGLOBAL;
            STGMEDIUM stm = new STGMEDIUM();

            // The pDataObj pointer contains the objects being acted upon. In this 
            // example, we get an HDROP handle for enumerating the selected files 
            // and folders.
            IDataObject dataObject = (IDataObject)Marshal.GetObjectForIUnknown(pDataObj);
            dataObject.GetData(ref fe, out stm);

            try
            {
                // Get an HDROP handle.
                IntPtr hDrop = stm.unionmember;
                if (hDrop == IntPtr.Zero)
                {
                    throw new ArgumentException();
                }

                // Determine how many files are involved in this operation.
                uint nFiles = NativeMethods.DragQueryFile(hDrop, UInt32.MaxValue, null, 0);

                // This code sample displays the custom context menu item when only 
                // one file is selected. 
                if (nFiles == 1)
                {
                    // Get the path of the file.
                    StringBuilder fileName = new StringBuilder(260);
                    if (0 == NativeMethods.DragQueryFile(hDrop, 0, fileName,
                        fileName.Capacity))
                    {
                        Marshal.ThrowExceptionForHR(WinError.E_FAIL);
                    }
                    this.selectedFile = fileName.ToString();
                }
                else
                {
                    Marshal.ThrowExceptionForHR(WinError.E_FAIL);
                }

                // [-or-]

                // Enumerate the selected files and folders.
                //if (nFiles > 0)
                //{
                //    StringCollection selectedFiles = new StringCollection();
                //    StringBuilder fileName = new StringBuilder(260);
                //    for (uint i = 0; i < nFiles; i++)
                //    {
                //        // Get the next file name.
                //        if (0 != NativeMethods.DragQueryFile(hDrop, i, fileName,
                //            fileName.Capacity))
                //        {
                //            // Add the file name to the list.
                //            selectedFiles.Add(fileName.ToString());
                //        }
                //    }
                //
                //    // If we did not find any files we can work with, throw 
                //    // exception.
                //    if (selectedFiles.Count == 0)
                //    {
                //        Marshal.ThrowExceptionForHR(WinError.E_FAIL);
                //    }
                //}
                //else
                //{
                //    Marshal.ThrowExceptionForHR(WinError.E_FAIL);
                //}
            }
            finally
            {
                NativeMethods.ReleaseStgMedium(ref stm);
            }
        }

        #endregion


        #region IContextMenu Members

        /// <summary>
        /// Add commands to a shortcut menu.
        /// </summary>
        /// <param name="hMenu">A handle to the shortcut menu.</param>
        /// <param name="iMenu">
        /// The zero-based position at which to insert the first new menu item.
        /// </param>
        /// <param name="idCmdFirst">
        /// The minimum value that the handler can specify for a menu item ID.
        /// </param>
        /// <param name="idCmdLast">
        /// The maximum value that the handler can specify for a menu item ID.
        /// </param>
        /// <param name="uFlags">
        /// Optional flags that specify how the shortcut menu can be changed.
        /// </param>
        /// <returns>
        /// If successful, returns an HRESULT value that has its severity value set 
        /// to SEVERITY_SUCCESS and its code value set to the offset of the largest 
        /// command identifier that was assigned, plus one.
        /// </returns>
        public int QueryContextMenu(
            IntPtr hMenu,
            //HMenu hMenu,
            uint iMenu,
            uint idCmdFirst,
            uint idCmdLast,
            uint uFlags)
        {
            MessageBox(0, "Query context menu function", "Info", 0);
            if (syncVerbCanonicalName != null && syncVerbCanonicalName.Length > 0)
            {
                Array.Clear(syncVerbCanonicalName, 0, syncVerbCanonicalName.Length);
            }
            
            syncVerbCanonicalName = new string[10];
           
            string dirName = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\" + Resources.BrContextMenuTitle +"\\";
            MessageBox(0,"Query context menu function 2222","Info",0);
            // If uFlags include CMF_DEFAULTONLY then we should not do anything.
            if (((uint)CMF.CMF_DEFAULTONLY & uFlags) != 0)
            {
                return WinError.MAKE_HRESULT(WinError.SEVERITY_SUCCESS, 0, 0);
            }

            if (this.selectedFile.Substring(0, dirName.Length).CompareTo(dirName) != 0)
            {
                return WinError.MAKE_HRESULT(WinError.SEVERITY_SUCCESS, 0, 0);
            }

                   int id = 1;
            //       if ((uFlags & (uint)(CMF.CMF_VERBSONLY | CMF.CMF_DEFAULTONLY | CMF.CMF_NOVERBS)) == 0 ||
            //(uFlags & (uint)CMF.CMF_EXPLORE) != 0)
               //    {
                   
                   //file.WriteLine("Add First Seperator");
                   // Add a separator.
                   cmdFirst = idCmdFirst;
                    MENUITEMINFO sep = new MENUITEMINFO();
                   sep.cbSize = (uint)Marshal.SizeOf(sep);
                   sep.fMask = MIIM.MIIM_TYPE;
                   sep.fType = MFT.MFT_SEPARATOR;
                   if (!NativeMethods.InsertMenuItem(hMenu, iMenu, true, ref sep))
                   {
                       return Marshal.GetHRForLastWin32Error();
                   }
                   Debug.WriteLine("Context menu Submenu popup");
                       HMenu submenu = Helpers.CreatePopupMenu();

                       string filePath = this.selectedFile;
                       FileAttributes attr = File.GetAttributes(filePath);
             
                       //detect whether its a directory or file
                       if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                       {
                          // file.WriteLine("Menu for folder");
                           Debug.WriteLine("Menu for folder");
                           syncVerbCanonicalName[0] = "syncFolderContextMenu";
                           Helpers.AppendMenu(submenu, MFMENU.MF_STRING | MFMENU.MF_ENABLED,
                                              new IntPtr(idCmdFirst + id++), "Secure Share");
                           syncVerbCanonicalName[1] = "syncSecureShare";
           
                           Helpers.AppendMenu(submenu, MFMENU.MF_STRING | MFMENU.MF_ENABLED,
                                              new IntPtr(idCmdFirst + id++), "Comments");
                           syncVerbCanonicalName[2] = "syncComments";
                           Helpers.AppendMenu(submenu, MFMENU.MF_SEPARATOR | MFMENU.MF_ENABLED,
                                                         new IntPtr(idCmdFirst + id++), "");
                           syncVerbCanonicalName[3] = "syncSeparator";
                           Helpers.AppendMenu(submenu, MFMENU.MF_STRING | MFMENU.MF_ENABLED,
                                              new IntPtr(idCmdFirst + id++), "View Web Home");
                           syncVerbCanonicalName[4] = "syncWebHome";
                       }
                       else 
                       {
                          // file.WriteLine("Menu for file");
                           Debug.WriteLine("Menu for folder");
                           syncVerbCanonicalName[0] = "syncFileContextMenu";
                           Helpers.AppendMenu(submenu, MFMENU.MF_STRING | MFMENU.MF_ENABLED,
                                              new IntPtr(idCmdFirst + id++), "Public Share");
                           syncVerbCanonicalName[1] = "syncFilePublicShare";
                           Helpers.AppendMenu(submenu, MFMENU.MF_STRING | MFMENU.MF_ENABLED,
                                              new IntPtr(idCmdFirst + id++), "Secure Share");
                           syncVerbCanonicalName[2] = "syncFileSecureShare";
                           Helpers.AppendMenu(submenu, MFMENU.MF_STRING | MFMENU.MF_ENABLED,
                                              new IntPtr(idCmdFirst + id++), "File Versions");
                           syncVerbCanonicalName[3] = "syncFileVersion";
                           Helpers.AppendMenu(submenu, MFMENU.MF_STRING | MFMENU.MF_ENABLED,
                                              new IntPtr(idCmdFirst + id++), "Comments");
                           syncVerbCanonicalName[4] = "syncFileComments";
                           Helpers.AppendMenu(submenu, MFMENU.MF_SEPARATOR | MFMENU.MF_ENABLED,
                                                         new IntPtr(idCmdFirst + id++), "");
                           syncVerbCanonicalName[5] = "syncFileSeparator";
                           Helpers.AppendMenu(submenu, MFMENU.MF_STRING | MFMENU.MF_ENABLED,
                                              new IntPtr(idCmdFirst + id++), "View File on Web");
                           syncVerbCanonicalName[6] = "syncSecureShare";
                           Helpers.AppendMenu(submenu, MFMENU.MF_STRING | MFMENU.MF_ENABLED,
                                              new IntPtr(idCmdFirst + id++), "View Web Home");
                           syncVerbCanonicalName[7] = "syncFileWebHome";
                       }

                     //  file.WriteLine("Add main menu");
                       Debug.WriteLine("Add main menu");
                 //// Use either InsertMenu or InsertMenuItem to add menu items.
                 MENUITEMINFO mii = new MENUITEMINFO();
                 mii.cbSize = (uint)Marshal.SizeOf(mii);
                 mii.fMask = MIIM.MIIM_BITMAP | MIIM.MIIM_STRING | MIIM.MIIM_FTYPE |
                     MIIM.MIIM_ID | MIIM.MIIM_STATE | MIIM.MIIM_SUBMENU;
                 mii.wID = idCmdFirst + IDM_DISPLAY;
                 mii.fType = MFT.MFT_STRING;
                 mii.dwTypeData = this.menuText;
                 mii.fState = MFS.MFS_ENABLED;
                 mii.hbmpItem = this.menuBmp;
                 mii.hSubMenu = submenu.handle;
                 
                 if (!NativeMethods.InsertMenuItem(hMenu, iMenu + 1, true, ref mii))
                 {
                     return Marshal.GetHRForLastWin32Error();
                 }

                 //// Add a separator.
                 MENUITEMINFO sep1 = new MENUITEMINFO();
                 sep1.cbSize = (uint)Marshal.SizeOf(sep1);
                 sep1.fMask = MIIM.MIIM_TYPE;
                 sep1.fType = MFT.MFT_SEPARATOR;
                 if (!NativeMethods.InsertMenuItem(hMenu, iMenu + 2, true, ref sep1))
                 {
                     return Marshal.GetHRForLastWin32Error();
                 }
              //   file.WriteLine("Add last Seperator");
                 Debug.WriteLine("Add last Seperator");
            // Return an HRESULT value with the severity set to SEVERITY_SUCCESS. 
            // Set the code value to the offset of the largest command identifier 
            // that was assigned, plus one (1).
            return WinError.MAKE_HRESULT(WinError.SEVERITY_SUCCESS, 0,
                IDM_DISPLAY + 1);
        }

        /// <summary>
        /// Carry out the command associated with a shortcut menu item.
        /// </summary>
        /// <param name="pici">
        /// A pointer to a CMINVOKECOMMANDINFO or CMINVOKECOMMANDINFOEX structure 
        /// containing information about the command. 
        /// </param>
        public void InvokeCommand(IntPtr pici)
        {

            bool isUnicode = false;

            // Determine which structure is being passed in, CMINVOKECOMMANDINFO or 
            // CMINVOKECOMMANDINFOEX based on the cbSize member of lpcmi. Although 
            // the lpcmi parameter is declared in Shlobj.h as a CMINVOKECOMMANDINFO 
            // structure, in practice it often points to a CMINVOKECOMMANDINFOEX 
            // structure. This struct is an extended version of CMINVOKECOMMANDINFO 
            // and has additional members that allow Unicode strings to be passed.
            //file.WriteLine("Invoke command");
            string test = "InvokeCommand";
            MessageBox(0, test, "Information", 0);
            
            CMINVOKECOMMANDINFO ici = (CMINVOKECOMMANDINFO)Marshal.PtrToStructure(
                pici, typeof(CMINVOKECOMMANDINFO));
            CMINVOKECOMMANDINFOEX iciex = new CMINVOKECOMMANDINFOEX();
            if (ici.cbSize == Marshal.SizeOf(typeof(CMINVOKECOMMANDINFOEX)))
            {
                if ((ici.fMask & CMIC.CMIC_MASK_UNICODE) != 0)
                {
                    isUnicode = true;
                    iciex = (CMINVOKECOMMANDINFOEX)Marshal.PtrToStructure(pici,
                        typeof(CMINVOKECOMMANDINFOEX));
                }
            }
            // Determines whether the command is identified by its offset or verb.
            // There are two ways to identify commands:
            // 
            //   1) The command's verb string 
            //   2) The command's identifier offset
            // 
            // If the high-order word of lpcmi->lpVerb (for the ANSI case) or 
            // lpcmi->lpVerbW (for the Unicode case) is nonzero, lpVerb or lpVerbW 
            // holds a verb string. If the high-order word is zero, the command 
            // offset is in the low-order word of lpcmi->lpVerb.

            // For the ANSI case, if the high-order word is not zero, the command's 
            // verb string is in lpcmi->lpVerb. 
           // file.WriteLine("first condition for high word");
             Debug.WriteLine("first condition for high word");
            if (!isUnicode && NativeMethods.HighWord(ici.verb.ToInt32()) != 0)
            {
                MessageBox(0,"first condition for high word inside","",0);
            
                // Is the verb supported by this context menu extension?
                if (Marshal.PtrToStringAnsi(ici.verb) == this.verb)
                {
                    OnVerbDisplayFileName(ici.hwnd);
                }
                else
                {
                   // file.WriteLine("first condition for high word exception");
            
                    // If the verb is not recognized by the context menu handler, it 
                    // must return E_FAIL to allow it to be passed on to the other 
                    // context menu handlers that might implement that verb.
                    Marshal.ThrowExceptionForHR(WinError.E_FAIL);
                }
            }

            // For the Unicode case, if the high-order word is not zero, the 
            // command's verb string is in lpcmi->lpVerbW. 
            else if (isUnicode && NativeMethods.HighWord(iciex.verbW.ToInt32()) != 0)
            {
                MessageBox(0, "Condition for high word first else if", "", 0);
                // Is the verb supported by this context menu extension?
                if (Marshal.PtrToStringUni(iciex.verbW) == this.verb)
                {
                   Debug.WriteLine("second condition for high word");
            
                    OnVerbDisplayFileName(ici.hwnd);
                }
                else
                {
                    // If the verb is not recognized by the context menu handler, it 
                    // must return E_FAIL to allow it to be passed on to the other 
                    // context menu handlers that might implement that verb.
                  //  file.WriteLine("second condition for high word exception");
                    Marshal.ThrowExceptionForHR(WinError.E_FAIL);

                }
            }

            // If the command cannot be identified through the verb string, then 
            // check the identifier offset.
            else
            {
                Debug.WriteLine("Command can not identified");
                string filePath = this.selectedFile;
                FileAttributes attr = File.GetAttributes(filePath);
                MessageBox(0, "Condition for high word first else if", "", 0);
                //detect whether its a directory or file
                if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                {
                   string test1 = (NativeMethods.LowWord(ici.verb.ToInt32()) - cmdFirst).ToString();
                    Debug.WriteLine("detect directory or file");
                    MessageBox(0, test1, "Information", 0);
                    switch (NativeMethods.LowWord(ici.verb.ToInt32()) - cmdFirst)
                    {
                        case 1:
                            MessageBox(0, "IContextMenu.InvokeCommand", "Information", 0);
                            OnVerbDisplayFileName(ici.hwnd);
                        break;
                        
                        case 2:
                            MessageBox(0, "IContextMenu.InvokeCommand1", "Information", 0);
                            OnVerbDisplayFileName(ici.hwnd);
                        break;
        
                        case 4:
                        MessageBox(0, "IContextMenu.InvokeCommand2", "Information", 0);
                        OnVerbDisplayFileName(ici.hwnd);
                        break;
                    
                        default:
                            // If the verb is not recognized by the context menu handler, it 
                            // must return E_FAIL to allow it to be passed on to the other 
                            // context menu handlers that might implement that verb.
                            Marshal.ThrowExceptionForHR(WinError.E_FAIL);
                            break;
                    }
                }
                else
                {
                    MessageBox(0, "detect directory or file second switch", "", 0);
                    switch (NativeMethods.LowWord(ici.verb.ToInt32()))
                    {
                        case 0:
                            MessageBox(0, "IContextMenu.InvokeCommandFile 1", "Information", 0);
                            OnVerbDisplayFileName(ici.hwnd);
                            break;
                        case 1:
                            MessageBox(0, "IContextMenu.InvokeCommandFile 2", "Information", 0);
                       
                            OnVerbDisplayFileName(ici.hwnd);
                            break;
                        case 2:
                            MessageBox(0, "IContextMenu.InvokeCommandFile 3", "Information", 0);
                       
                            OnVerbDisplayFileName(ici.hwnd);
                            break;
                        case 3:
                            MessageBox(0, "IContextMenu.InvokeCommandFile 4", "Information", 0);
                       
                            OnVerbDisplayFileName(ici.hwnd);
                            break;
                        case 5:
                            MessageBox(0, "IContextMenu.InvokeCommandFile 6", "Information", 0);
                       
                            OnVerbDisplayFileName(ici.hwnd);
                            break;
                        case 6:
                            MessageBox(0, "IContextMenu.InvokeCommandFile 7", "Information", 0);
                       
                            OnVerbDisplayFileName(ici.hwnd);
                            break;
                        default:
                            // If the verb is not recognized by the context menu handler, it 
                            // must return E_FAIL to allow it to be passed on to the other 
                            // context menu handlers that might implement that verb.
                            Marshal.ThrowExceptionForHR(WinError.E_FAIL);
                            break;
                    }
                }
                
                
                //// Is the command identifier offset supported by this context menu 
                //// extension?
                //if (NativeMethods.LowWord(ici.verb.ToInt32()) == IDM_DISPLAY)
                //{
                //    OnVerbDisplayFileName(ici.hwnd);
                //}
                //else
                //{
                //    // If the verb is not recognized by the context menu handler, it 
                //    // must return E_FAIL to allow it to be passed on to the other 
                //    // context menu handlers that might implement that verb.
                //    Marshal.ThrowExceptionForHR(WinError.E_FAIL);
                //}
            } 

        }

        /// <summary>
        /// Get information about a shortcut menu command, including the help string 
        /// and the language-independent, or canonical, name for the command.
        /// </summary>
        /// <param name="idCmd">Menu command identifier offset.</param>
        /// <param name="uFlags">
        /// Flags specifying the information to return. This parameter can have one 
        /// of the following values: GCS_HELPTEXTA, GCS_HELPTEXTW, GCS_VALIDATEA, 
        /// GCS_VALIDATEW, GCS_VERBA, GCS_VERBW.
        /// </param>
        /// <param name="pReserved">Reserved. Must be IntPtr.Zero</param>
        /// <param name="pszName">
        /// The address of the buffer to receive the null-terminated string being 
        /// retrieved.
        /// </param>
        /// <param name="cchMax">
        /// Size of the buffer, in characters, to receive the null-terminated string.
        /// </param>
        public void GetCommandString(
            UIntPtr idCmd,
            uint uFlags,
            IntPtr pReserved,
            StringBuilder pszName,
            uint cchMax)
        {
            return;
           Debug.WriteLine("Get Command String");
         
          // MessageBox(0, "GetCommandString", "Information", 0);
          // MessageBox(0, idCmd.ToString(), "Information", 0);
            //if (idCmd.ToUInt32() == IDM_DISPLAY)
            if (idCmd.ToUInt32() < 10)
            {
                Debug.WriteLine("Switch Get Command String");
            
                switch ((GCS)uFlags)
                {
                    case GCS.GCS_VERBW:
                        if (this.verbCanonicalName.Length > cchMax - 1)
                        {
                            Marshal.ThrowExceptionForHR(WinError.STRSAFE_E_INSUFFICIENT_BUFFER);
                        }
                        else
                        {
                            pszName.Clear();
                            pszName.Append(this.syncVerbCanonicalName[idCmd.ToUInt32()]);
                        }
                        break;

                    case GCS.GCS_HELPTEXTW:
                        if (this.verbHelpText.Length > cchMax - 1)
                        {
                            Marshal.ThrowExceptionForHR(WinError.STRSAFE_E_INSUFFICIENT_BUFFER);
                        }
                        else
                        {
                            pszName.Clear();
                            pszName.Append(this.verbHelpText);
                        }
                        break;
                }
            }
         //   MessageBox(0, pszName.ToString(), "Info", 0);
        }
       

        #endregion
        
    }
   
}