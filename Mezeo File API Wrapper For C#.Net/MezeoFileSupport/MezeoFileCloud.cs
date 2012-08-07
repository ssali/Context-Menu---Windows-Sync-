using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Runtime.Serialization;
using System.IO;
using System.Xml;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;
using System.Diagnostics;
using System.Windows.Forms;
using System.Threading;
using System.Runtime.InteropServices;
using System.Web;


namespace MezeoFileSupport
{
    public delegate void CallbackIncrementProgress(double fileSize);

    public delegate bool CallbackContinueRunning();

    public class LoginDetails
    {
        public String szUserName;
        public int nAccountType;

        public double dblStorage_Allocated;
        public double dblStorage_Used;

        public String szS3_Authid;
        public String szs3_Authkey;

        public double dblBandWidth_Allocated;
        public double dblBandWidth_Total;
        public double dblBandWidth_Public;
        public double dblBandwidth_Private;

        public String szContainerContentsUri;
        public String szManagementUri;
        public String szNamespaceUri;
        public String szNQParentUri;

        public LoginDetails()
        {
            szUserName = "";
            nAccountType = -2;

            dblStorage_Allocated = 0.0;
            dblStorage_Used = 0.0;

            szS3_Authid = "";
            szs3_Authkey = "";

            dblBandWidth_Allocated = 0.0;
            dblBandWidth_Total = 0.0;
            dblBandWidth_Public = 0.0;
            dblBandwidth_Private = 0.0;

            szContainerContentsUri = "";
            szManagementUri = "";
            szNamespaceUri = "";
            szNQParentUri = "";
        }
    };

    public class ItemDetails
    {
        public String szItemType;
        public String szContentUrl;
        public String szParentUrl;

        public double dblSizeInBytes;
        public String strName;
        public String strETag;

        public DateTime dtAccessed;
        public DateTime dtCreated;
        public DateTime dtModified;

        public bool bShared;
        public bool bPublic;

        public String szMimeType;

        public int nCurrentPosition;
        public int nTotalItem;

        public ItemDetails()
        {
            szItemType = "";
            szContentUrl = "";
            szParentUrl = "";

            dblSizeInBytes = 0.0;
            strName = "";
            strETag = "";

            bShared = false;
            bPublic = false;

            szMimeType = "";

            nCurrentPosition = -1;
            nTotalItem = -1;
        }
    };

    public class FilterDetails
    {
        public String szFieldValue;
        public String szFieldName;
        public String szFilterOperation;

        public int nStartPosition;
        public int nCount;

        public FilterDetails()
        {
            szFieldValue = "";
            szFieldName = "name";
            szFilterOperation = "ILIKE";

            nStartPosition = -1;
            nCount = 100;
        }
    };

    public class ItemResults
    {
        public String szContentsUrl;
        public String szName;

        public ItemResults()
        {
            szContentsUrl = "";
            szName = "";
        }
    };

    public class NQDetails
    {
        public int nTotalNQ;
        public String StrEventResult;
        public String StrEventTime;
        public String StrObjectID;
        public String StrEventUser;
        public String StrEvent;
        public String StrParentID;
        public String StrObjectType;
        public String StrParentUri;
        public String StrDomainUri;
        public String StrObjectName;
        public String StrMezeoExportedPath;
        public String StrHash;
        public long lSize;
        public Int64 EventDbId;

        public NQDetails()
        {
            nTotalNQ = -1;
            StrEventResult = "";
            StrEventTime = "";
            StrObjectID = "";
            StrEventUser = "";
            StrEvent = "";
            StrParentID = "";
            StrObjectType = "";
            StrParentUri = "";
            StrDomainUri = "";
            StrObjectName = "";
            StrMezeoExportedPath = "";
            StrHash = "";
            lSize = 0;
            EventDbId = -1;
        }
    };

    public class NSResult
    {
        public DateTime dtAccessed;
        public DateTime dtCreated;
        public DateTime dtModified;
        public double dblSizeInBytes;
        public String StrName;
        public String StrVersion;
        public String StrParentUri;
        public String StrContentsUri;
        public String StrType;
        public String StrMimeType;
        public bool bShared;
        public bool bPublic;

        public NSResult()
        {
            dblSizeInBytes = 0;
            StrName = "";
            bShared = false;
            bPublic = false;
            StrVersion = "";
            StrParentUri = ""; 
            StrContentsUri = "";
            StrType = "";
            StrMimeType = "";
        }
    };

    public class NQLengthResult
    {
        public int nStart;
        public int nEnd;
        public NQLengthResult()
        {
            nStart  = 0;
            nEnd = 0;
        }
    };

    public class MezeoFileCloud
    {
        private String m_strLoginName;
		private String m_strPassword;
        private String m_strMacAdd;
        private XmlDocument m_xmlDocument = new XmlDocument();
        private bool m_bStop = false;
        private String StrAPIKey = "c5f5c39e22b4c743ff7c83470499748c6ac46b249c29e3934f5744166af130c6";
        private Int32 nTimeout = 120000;
        private Dictionary<string, string> types;

        public delegate void FileDownloadStoppedEvent(string fileName);
        public event FileDownloadStoppedEvent downloadStoppedEvent;

        public delegate void FileUploadStoppedEvent(string szSourceFileName, string szContantURI);
        public event FileUploadStoppedEvent uploadStoppedEvent;

        //create request format for get details
        private void OnGetRequest(ref HttpWebRequest webRequest, String strRequestURL, String strAccept, String strXCloudDepth, String strMethod, FilterDetails filterDetails)
        {
            //curl -u user:password -H "X-Cloud-Depth: 1" "https://rj.mezeo.net/v2/containers/Yzk0OTFiMmQzMTMyMWY4Y2ExZTExOWYwYTg4YTYzNDI5/contents?filterField=name&filterValue=Pi&filterOperation=ILIKE"
            //curl -u user:password -H "X-Cloud-Depth: 1" "https://rj.mezeo.net/v2/containers/Yzk0OTFiMmQzMTMyMWY4Y2ExZTExOWYwYTg4YTYzNDI5/contents?count=1&start=2"
            // Construct the URL to use.  Add a useless random element at the end to get around caching issues.
            String strRequestURLAndFilter;
            strRequestURLAndFilter = strRequestURL + "?n=" + DateTime.Now.Ticks.ToString();

            // If there is a filter, add the information to the URL.
            if (null != filterDetails)
            {
                if ((null != filterDetails.szFieldValue) && (0 != filterDetails.szFieldValue.Length))
                {
                    strRequestURLAndFilter = strRequestURLAndFilter + "&filterField=" + HttpUtility.UrlEncode(filterDetails.szFieldName) + "&filterOperation=" + HttpUtility.UrlEncode(filterDetails.szFilterOperation) + "&filterValue=" + HttpUtility.UrlEncode(filterDetails.szFieldValue);
                }

                if (-1 != filterDetails.nStartPosition)
                {
                    strRequestURLAndFilter = strRequestURLAndFilter + "&start=" + filterDetails.nStartPosition + "&count=" + filterDetails.nCount;
                }
            }

            webRequest = (HttpWebRequest)WebRequest.Create(strRequestURLAndFilter);
            //string credentials = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(m_strLoginName + ":" + m_strPassword));
            //webRequest.Headers.Add("Authorization", "Basic " + credentials);
	        webRequest.Credentials = new NetworkCredential(m_strLoginName, m_strPassword);
	        webRequest.PreAuthenticate = true;
	        webRequest.Method = strMethod;
	        webRequest.KeepAlive = false;
	        webRequest.Headers.Add("X-Client-Specification", "2");
	        if(strXCloudDepth != "")
		        webRequest.Headers.Add("X-Cloud-Depth", strXCloudDepth);
	        webRequest.Accept = strAccept;
            webRequest.Timeout = nTimeout;
        }

        //responce string for request basic
        private String OnGetResponseString(Stream responseStream)
        {
	        StringBuilder responseString = new StringBuilder();
	        byte[] buffer = new byte[1024*64];
	        int bytes_read = 0;
	        while ((bytes_read = responseStream.Read(buffer, 0, buffer.Length)) > 0)
	        {
		        responseString.Append(Encoding.UTF8.GetString(buffer, 0, bytes_read));
	        }
	        responseStream.Close();
	        return responseString.ToString();
        }

        //notification queue info
        private void OnNotificationQueue(ref HttpWebRequest webRequest, String StrUri, String StrMethod)
        {
            webRequest = (HttpWebRequest)WebRequest.Create(StrUri);
            webRequest.Credentials = new NetworkCredential(m_strLoginName, m_strPassword);
            webRequest.PreAuthenticate = true;
            webRequest.Method = StrMethod;
            webRequest.KeepAlive = false;
            webRequest.Headers.Add("X-CDMI-Specification-Version", "1.0.1");
            webRequest.Accept = "application/cdmi-queue";
            webRequest.ContentType = "application/cdmi-queue";
            webRequest.Timeout = nTimeout;
        }

        private bool OnPostAndPutRequest(ref HttpWebRequest webRequest, String strRequestURL, String strSource, String strContentType, String strLoadHeader, String strFinalBoundary, String StrMethod, String strDest, CallbackIncrementProgress IncProgress)
        {
            bool bStatus = true;
            string strNewUri = strRequestURL + "?n=" + DateTime.Now.Ticks.ToString();
            //bStatus = MultipartFormPost(ref webRequest, strNewUri, strSource, IncProgress);
            //return bStatus;

            // Overall content length
            Int64 contentLength = 0;

            //Convert payloadheader to UTF8
            byte[] bytes = Encoding.UTF8.GetBytes(strLoadHeader);

            // If there is a file, then get the information about it.
            FileInfo fileinfo = null;
            if ((strSource != null) && (strSource.Length != 0))
                fileinfo = new FileInfo(strSource);

            if (strFinalBoundary != null && strLoadHeader != null)
            {
                // compute length of multi-part form
                contentLength = bytes.Length + strFinalBoundary.Length;
            }

            if (fileinfo != null)
                contentLength += fileinfo.Length;

            webRequest = (HttpWebRequest)WebRequest.Create(strRequestURL);
            webRequest.Credentials = new NetworkCredential(m_strLoginName, m_strPassword);
            webRequest.PreAuthenticate = true;
            webRequest.Method = StrMethod;
            webRequest.KeepAlive = false;
            webRequest.Headers.Add("X-Client-Specification", "2");
            if (strDest != "")
                webRequest.Headers.Add("Content-Location", strDest);
            webRequest.ContentType = strContentType;
            webRequest.Timeout = System.Threading.Timeout.Infinite;
            //   webRequest.SendChunked = true;
            webRequest.AllowWriteStreamBuffering = false;
            webRequest.ContentLength = contentLength;

            bool finalize = true;

            using (Stream writeStream = webRequest.GetRequestStream())
            {
                writeStream.Write(bytes, 0, bytes.Length);
                if (strSource != "")
                {
                    using (FileStream fileStream = new FileStream(strSource, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        byte[] buffer = new byte[1024 * 64];
                        int bytesRead = 0;

                        while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            if (IncProgress != null)
                                IncProgress(bytesRead);

                            writeStream.Write(buffer, 0, bytesRead);

                            if (m_bStop)
                            {
                                m_bStop = false;
                                bStatus = false;
                                finalize = false;
                                break;
                            }
                        }
                        fileStream.Close();
                    }

                    if (finalize)
                    {
                        bytes = Encoding.UTF8.GetBytes(strFinalBoundary);
                        if (IncProgress != null)
                            IncProgress(bytes.Length);
                        writeStream.Write(bytes, 0, bytes.Length);
                    }
                }
                try
                {
                    writeStream.Close();
                }
                catch (Exception ex)
                {
                    // If the request was cancelled, then set the webRequest reference to null.
                    if (finalize == false)
                    {
                        webRequest.Abort();
                        webRequest = null;
                    }
                }
            }

            // If the request was cancelled, then set the webRequest reference to null.
            if ((finalize == false) && (null != webRequest))
            {
                webRequest.Abort();
                webRequest = null;
            }

            return bStatus;
        }


        public bool MultipartFormPost(ref HttpWebRequest webRequest, String posturi, String filenameWithPath, CallbackIncrementProgress IncProgress)
        {
            // Strip off the path from our filename
            String filename = filenameWithPath;
            bool bStatus = true;
            FileInfo fileinfo = new FileInfo(filenameWithPath);
            int slashloc = filenameWithPath.LastIndexOf('\\');

            if (slashloc > 0)
                filename = filenameWithPath.Substring(slashloc);

            Console.WriteLine(filename);
            // Overall content length
            Int64 contentLength = 0;

            // Mime-Type of the file to be uploaded
            String contenttype = GetMineTypeContainsKey(fileinfo.Extension.ToLower()); 

            // Create a new random form boundary using the System time
            String boundary = DateTime.Now.Ticks.ToString("x") + DateTime.Now.Ticks.ToString("x");

            // File payload section
            String payloadheader;
            payloadheader = "--" + boundary + "\r\n";
            payloadheader += "Content-Disposition: form-data; name=\"Filedata\"; filename=\"" + filename + "\"\r\n";
            payloadheader += "Content-Type: " + contenttype + "\r\n\r\n";

            // multipart/form-data final boundary
            String finalboundary = "\r\n--" + boundary + "--\r\n";

            //Convert payloadheader to UTF8
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(payloadheader);

            // compute length of multi-part form
            contentLength = bytes.Length + finalboundary.Length;

            // Add our file's size to our overall contentlength
            contentLength += fileinfo.Length;

            // Set up our HttpWebRequest object 
            webRequest = (HttpWebRequest)WebRequest.Create(posturi);
            webRequest.ContentType = "multipart/form-data; boundary=" + boundary;
            webRequest.Method = "POST";
            webRequest.Accept = "*/*";
            webRequest.KeepAlive = false;
            webRequest.ProtocolVersion = HttpVersion.Version10;
            webRequest.ContentLength = contentLength;
            // Add our additional headers
            webRequest.Headers.Add("X-Client-Specification", "2");
            //request.Headers.Add("X-Client-Key", Properties.Credentials.Default.Apikey);

            webRequest.Timeout = Timeout.Infinite;

            // Add our username/password to the request
            webRequest.Credentials = new NetworkCredential(m_strLoginName, m_strPassword);

            // Set PreAuthenticate to true to preemptively send the required Auth header
            webRequest.PreAuthenticate = true;

            bool finalize = true;
 
            try
            {
                // Submit the form data to the server
                using (Stream requestStream = webRequest.GetRequestStream())
                {
                    // Send the payload header after converting to UTF8 bytes
                    requestStream.Write(bytes, 0, bytes.Length);

                    // Stream the file data to the server
                    using (FileStream fileStream = new FileStream(filenameWithPath, FileMode.Open, FileAccess.Read))
                    {
                        // Upload the file streaming it from disk
                        byte[] buffer = new byte[1024 * 4];
                        int bytesRead = 0;

                        while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            if (IncProgress != null)
                                IncProgress(bytesRead);
                          
                            requestStream.Write(buffer, 0, bytesRead);

                            if (m_bStop)
                            {
                                m_bStop = false;
                                bStatus = false;
                                finalize = false;
                                break;
                            }
                        }

                        fileStream.Close();
                    }

                    if (finalize)
                    {
                        // Send the form's final boundary after converting to UTF8 bytes
                        bytes = System.Text.Encoding.UTF8.GetBytes(finalboundary);
                    }
                    
                    if (IncProgress != null)
                        IncProgress(bytes.Length);

                    requestStream.Write(bytes, 0, bytes.Length);
                }
            }
            catch (Exception ex)
            {
                int x = 0;
                if (x == 0)
                { }
            }

            return bStatus;
        }


        //save on the local drive
        private bool OnSaveResponseFile(Stream responseStream, String strSaveInFile, long lFrom, CallbackIncrementProgress IncProgress, CallbackContinueRunning ContinueRun)
        {
            byte[] buffer = new byte[1024 * 64];
	        int bytes_read = 0;
	        FileStream fstPersons = null;
            bool bStatus = true;
            String strTempFileNew = strSaveInFile + ".partial." + m_strMacAdd;
            String strTempFileOld = strSaveInFile + ".original." + m_strMacAdd;

            m_bStop = false;

            try
            {
                if (lFrom > 0)
                {
                    fstPersons = new FileStream(strTempFileNew, FileMode.Append);
                    fstPersons.Seek(lFrom, SeekOrigin.Begin);
                }
                else
                {
                    try
                    {
                        fstPersons = new FileStream(strTempFileNew, FileMode.Create);
                    }
                    catch (System.IO.DirectoryNotFoundException ex)
                    {
                        // bugzid:1845 - (win) pull file event from cloud before 'folder create' event
                        // The directory doesn't exist so create it.
                        String strPath = strSaveInFile.Substring(0, strSaveInFile.LastIndexOf('\\'));
                        if (false == Directory.Exists(strPath))
                            Directory.CreateDirectory(strPath);
                        fstPersons = new FileStream(strTempFileNew, FileMode.Create);
                    }
                    catch (Exception ex)
                    {
                        //LogWrapper.LogMessage("MezeoFileCloud - OnSaveResponseFile", "Caught exception: " + ex.Message);
                        // The file probably already exists so truncate it...we're downloading it again.
                        fstPersons = new FileStream(strTempFileNew, FileMode.Truncate);
                    }
                }

                bool keepRunning = true;
                if (ContinueRun != null)
                    keepRunning = ContinueRun();

                while ((bytes_read = responseStream.Read(buffer, 0, buffer.Length)) > 0 && keepRunning)
                {
                    if (IncProgress != null)
                        IncProgress(bytes_read);

                    if (ContinueRun != null)
                        keepRunning = ContinueRun();

                    fstPersons.Write(buffer, 0, bytes_read);
                    if (m_bStop)
                    {
                        m_bStop = false;
                        bStatus = false;
                        break;
                    }
                }

                responseStream.Close();
                fstPersons.Close();

                if (!bStatus)
                {
                    if (downloadStoppedEvent != null)
                    {
                        downloadStoppedEvent(strTempFileNew);
                        //downloadStoppedEvent(strSaveInFile);
                    }
                }
            }
            finally
            {
                if (responseStream != null)
                    responseStream.Close();
                if (fstPersons != null)
                    fstPersons.Close();
            }

            if (bStatus)
            {
                try
                {
                    if (System.IO.File.Exists(strSaveInFile))
                    {
                        // Rename the existing file to 'original'.
                        File.Move(strSaveInFile, strTempFileOld);
                        System.IO.File.Delete(strTempFileOld);
                    }

                    // Rename the partial to the existing file.
                    File.Move(strTempFileNew, strSaveInFile);
                }
                catch (Exception ex)
                {
                    // This variable is just for debugging so that the debugger will
                    // actually step into this function so I can see what the exception is.
                    // TODO: Remove this for the release build.
                    //int x = 0;
                    //if (x > 0)
                    //{
                    //}
                }
            }
            return bStatus;
        }

        public void StopSyncProcess()
        {
	        m_bStop = true;
        }

        private String OnGetMimeType(String StrFilePath)
        {
	        String StrMimeType = "";
            FileInfo fileInfo = new FileInfo(StrFilePath);

            RegistryKey regKey = Registry.ClassesRoot.OpenSubKey(fileInfo.Extension.ToLower());
            if (regKey != null)
            {
		        Object ObjContentType = regKey.GetValue("Content Type");
                if (ObjContentType != null)
			        StrMimeType = ObjContentType.ToString();
                else
                    StrMimeType = GetMineTypeContainsKey(fileInfo.Extension.ToLower());
            }

	        return StrMimeType;
        }

        //get exception for item get
        private int OnException(WebException webEx)
        {
	        if (webEx.Status == WebExceptionStatus.NameResolutionFailure)
		        return -2;
	        else if(webEx.Status == WebExceptionStatus.ProtocolError)
	        {
                if (((HttpWebResponse)webEx.Response).StatusCode == HttpStatusCode.NotModified)
                    return 304;
                else if (((HttpWebResponse)webEx.Response).StatusCode == HttpStatusCode.Unauthorized)
                    return 401;
                else if (((HttpWebResponse)webEx.Response).StatusCode == HttpStatusCode.Forbidden)
                    return 403;
                else if (((HttpWebResponse)webEx.Response).StatusCode == HttpStatusCode.NotFound)
                    return 404;
                else if (((HttpWebResponse)webEx.Response).StatusCode == HttpStatusCode.NotAcceptable)
                    return 406;
                else if (((HttpWebResponse)webEx.Response).StatusCode == HttpStatusCode.Conflict)
                    return 409;
                else if (((HttpWebResponse)webEx.Response).StatusCode == HttpStatusCode.UnsupportedMediaType)
                    return 415;
                else if (((HttpWebResponse)webEx.Response).StatusCode == HttpStatusCode.InternalServerError)
                    return 500;
            }
	        return -1;
        }

        private void OnPutRequest(ref HttpWebRequest webRequest, String strRequestURL, String strSource, CallbackIncrementProgress IncProgress)
        {
	        webRequest = (HttpWebRequest)WebRequest.Create( strRequestURL );
	        webRequest.Credentials = new NetworkCredential(m_strLoginName, m_strPassword);
	        webRequest.PreAuthenticate = true;
	        webRequest.Method = "PUT";
	        webRequest.KeepAlive = false;
	        webRequest.Headers.Add("X-Client-Specification", "2");
            webRequest.Headers.Add("X-Client-Specification", "2");
            webRequest.Timeout = nTimeout;
            FileStream fileStream = null;
            Stream writeStream = null;

            try
            {

                writeStream = webRequest.GetRequestStream();

                if (writeStream != null)
                {
                    fileStream = new FileStream(strSource, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (fileStream != null)
                    {
                        byte[] buffer = new byte[1024 * 64];
                        int bytesRead = 0;

                        while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            if (IncProgress != null)
                                IncProgress(bytesRead);

                            writeStream.Write(buffer, 0, bytesRead);
                        }
                        fileStream.Close();
                    }
                    writeStream.Close();
                }
            }
            finally
            {
                if (fileStream != null)
                    fileStream.Close();
                if (writeStream != null)
                    writeStream.Close();
            }
        }

        //Cloud Login
        public LoginDetails Login(String strLoginName, String strPassword, String strUrl, String strMacAdd, ref int nStatusCode)
        {
            String m_strXmlResource;
            nStatusCode = 0;
	        m_strLoginName = strLoginName;
	        m_strPassword = strPassword;
            m_strMacAdd = strMacAdd;
            LoginDetails pLoginDetails = null;

            try
	        {
		        HttpWebRequest webRequest = null;
                XmlNode nodeXml;
                HttpWebResponse response = null;

                //------------Root Container and Management URI--------------------------
                OnGetRequest(ref webRequest, strUrl, "application/vnd.csp.cloud2+xml", "", "GET", null);
                //--- RootContainer and Management URI

                webRequest.Headers.Add("X-Cloud-Key", StrAPIKey);

		        response = (HttpWebResponse)webRequest.GetResponse();

		        nStatusCode = 200;
                m_strXmlResource = OnGetResponseString(response.GetResponseStream());
		        webRequest.Abort();
		        response.Close();

		        if(m_strXmlResource.Substring(0, 6) != "<cloud")
                    return null;

		        m_xmlDocument.LoadXml(m_strXmlResource);
                pLoginDetails = new LoginDetails();

                nodeXml = m_xmlDocument.SelectSingleNode("/cloud/locations/location/rootContainer");
                if (nodeXml != null)
                {
                    pLoginDetails.szContainerContentsUri = nodeXml.Attributes["xlink:href"].Value;
                    pLoginDetails.szContainerContentsUri += "/contents";
                    nodeXml.RemoveAll();
                }
                else
                {
                    pLoginDetails.szContainerContentsUri = "";
                    nStatusCode = -4;
                }

                nodeXml = m_xmlDocument.SelectSingleNode("/cloud/locations/location/management");
                if (nodeXml != null)
                {
                    pLoginDetails.szManagementUri = nodeXml.Attributes["xlink:href"].Value;
                    nodeXml.RemoveAll();
                }
                else
                {
                    pLoginDetails.szManagementUri = "";
                    nStatusCode = -4;
                }

                m_xmlDocument.RemoveAll();
                response.Close();

                //--------Account information----------------------//
                OnGetRequest(ref webRequest, strUrl + "/account", "application/vnd.csp.account-info2+xml", "", "GET", null);   
                //--- Account Information ---------//

                response = (HttpWebResponse)webRequest.GetResponse();

                nStatusCode = 200;
                m_strXmlResource = OnGetResponseString(response.GetResponseStream());
                webRequest.Abort();
                response.Close();

                m_xmlDocument.LoadXml(m_strXmlResource);

                nodeXml = m_xmlDocument.SelectSingleNode("/account-info/username");
                pLoginDetails.szUserName = nodeXml.InnerText;

                nodeXml.RemoveAll();
                nodeXml = m_xmlDocument.SelectSingleNode("/account-info/account_type");
                pLoginDetails.nAccountType = Convert.ToInt32(nodeXml.InnerText);

                nodeXml.RemoveAll();
                nodeXml = m_xmlDocument.SelectSingleNode("/account-info/storage/allocated");
                pLoginDetails.dblStorage_Allocated = Convert.ToDouble(nodeXml.InnerText);

                nodeXml.RemoveAll();
                nodeXml = m_xmlDocument.SelectSingleNode("/account-info/storage/used");
                if (nodeXml != null)
                {
                    pLoginDetails.dblStorage_Used = Convert.ToInt64(nodeXml.InnerText);
                    nodeXml.RemoveAll();
                }
                else
                    pLoginDetails.dblStorage_Used = 0;

                nodeXml = m_xmlDocument.SelectSingleNode("/account-info/s3/authid");
                if (nodeXml != null)
                {
                    pLoginDetails.szS3_Authid = nodeXml.InnerText;
                    nodeXml.RemoveAll();
                }
                else
                    pLoginDetails.szS3_Authid = "";

                nodeXml = m_xmlDocument.SelectSingleNode("/account-info/s3/authkey");
                if (nodeXml != null)
                {
                    pLoginDetails.szs3_Authkey = nodeXml.InnerText;
                    nodeXml.RemoveAll();
                }
                else
                    pLoginDetails.szs3_Authkey = "";

                nodeXml = m_xmlDocument.SelectSingleNode("/account-info/bandwidth/allocated");
                pLoginDetails.dblBandWidth_Allocated = Convert.ToInt64(nodeXml.InnerText);

                nodeXml.RemoveAll();
                nodeXml = m_xmlDocument.SelectSingleNode("/account-info/bandwidth/total");
                pLoginDetails.dblBandWidth_Total = Convert.ToDouble(nodeXml.InnerText);

                nodeXml.RemoveAll();
                nodeXml = m_xmlDocument.SelectSingleNode("/account-info/bandwidth/public");
                pLoginDetails.dblBandWidth_Public = Convert.ToDouble(nodeXml.InnerText);

                nodeXml.RemoveAll();
                nodeXml = m_xmlDocument.SelectSingleNode("/account-info/bandwidth/private");
                pLoginDetails.dblBandwidth_Private = Convert.ToDouble(nodeXml.InnerText);

                m_xmlDocument.RemoveAll();
                nodeXml.RemoveAll();
                response.Close();

                //--------------namespace URI---------------------------------//
                OnGetRequest(ref webRequest, strUrl + "/namespaces", "", "", "GET", null);
                //--- NameSpaces URI

                response = (HttpWebResponse)webRequest.GetResponse();

                nStatusCode = 200;
                m_strXmlResource = OnGetResponseString(response.GetResponseStream());
                webRequest.Abort();
                response.Close();

                m_xmlDocument.LoadXml(m_strXmlResource);

                nodeXml = m_xmlDocument.SelectSingleNode("/namespaces/namespace/container");
                if (nodeXml != null)
                {
                    pLoginDetails.szNamespaceUri = nodeXml.Attributes["xlink:href"].Value;
                    nodeXml.RemoveAll();
                }

                m_xmlDocument.RemoveAll();
		        response.Close();
	        }
	        catch(WebException wEx)
	        {
                nStatusCode = OnException(wEx);
		        return null;
	        }
	        catch
	        {
		        nStatusCode = -3;
		        //delete pLoginDetails;
		        return null;
	        }

	        return pLoginDetails;
        }

        //item detials
        public ItemDetails[] DownloadItemDetails(String strContainer, ref int nStatusCode, FilterDetails filterDetails)
        {
            nStatusCode=0;
            String m_strTemp;
	        ItemDetails[] pItemDetails = null;

            DateTime tmStaticSet = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            try
	        {
		        HttpWebRequest webRequest = null;
                OnGetRequest(ref webRequest, strContainer, "", "1", "Get", filterDetails);

		        HttpWebResponse response = (HttpWebResponse)(webRequest.GetResponse());
		        nStatusCode = 200;
		        m_strTemp = OnGetResponseString(response.GetResponseStream());

		        XmlDocument m_xmlFileList = new XmlDocument();
		        m_xmlFileList.LoadXml(m_strTemp);

		        XmlNode nodeXml = m_xmlFileList.SelectSingleNode("/file-list");
		        if(nodeXml.ChildNodes.Count == 0)
			        return null;

		        pItemDetails = new ItemDetails[nodeXml.ChildNodes.Count];
                pItemDetails[0] = new ItemDetails();
                m_strTemp = response.Headers.Get("eTag");

                pItemDetails[0].nTotalItem = -3;

		        webRequest.Abort();
		        response.Close();

		        if(nodeXml.HasChildNodes)
		        {
			        XmlNode nodeChildXml;
			        String strItemType;
			        for(int nNodePos=0; nNodePos<nodeXml.ChildNodes.Count; nNodePos++)
			        {
				        m_xmlFileList.RemoveAll();
				        m_xmlFileList.LoadXml(nodeXml.ChildNodes[nNodePos].InnerXml);
				        strItemType = "";

                        pItemDetails[nNodePos] = new ItemDetails();

                        pItemDetails[0].strETag = m_strTemp;
                        pItemDetails[nNodePos].nTotalItem = nodeXml.ChildNodes.Count;
				        pItemDetails[nNodePos].nCurrentPosition = nNodePos;

                        nodeChildXml = m_xmlFileList.SelectSingleNode("/container");
				        if(nodeChildXml != null)
				        {
					        strItemType = "/container";
					        pItemDetails[nNodePos].szItemType = "DIRECTORY";

					        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/contents");
					        pItemDetails[nNodePos].szContentUrl = nodeChildXml.Attributes["xlink:href"].Value;
					        nodeChildXml.RemoveAll();
				        }

                        nodeChildXml = m_xmlFileList.SelectSingleNode("/file");
				        if(nodeChildXml != null)
				        {
					        strItemType = "/file";
					        pItemDetails[nNodePos].szItemType = "FILE";

					        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/mime_type");
					        pItemDetails[nNodePos].szMimeType = nodeChildXml.InnerText;
					        nodeChildXml.RemoveAll();

					        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/public");
                            if(nodeChildXml.InnerText == "True")
					            pItemDetails[nNodePos].bPublic = true;
                            else
                                pItemDetails[nNodePos].bPublic = false;

					        nodeChildXml.RemoveAll();

					        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/content");
					        pItemDetails[nNodePos].szContentUrl = nodeChildXml.Attributes["xlink:href"].Value;
					        nodeChildXml.RemoveAll();
				        }

                        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/accessed");
                        //tmObj = tmStaticSet.AddMilliseconds(Convert.ToInt64(nodeChildXml.InnerText)*1000).ToLocalTime();
                        //pItemDetails[nNodePos].dtAccessed = tmObj.ToString("M/d/yyyy h:mm tt");
                        pItemDetails[nNodePos].dtAccessed = tmStaticSet.AddMilliseconds(Convert.ToInt64(nodeChildXml.InnerText) * 1000).ToLocalTime();
				        nodeChildXml.RemoveAll();

				        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/bytes");
				        pItemDetails[nNodePos].dblSizeInBytes = Convert.ToDouble(nodeChildXml.InnerText);
				        nodeChildXml.RemoveAll();

				        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/created");
                        pItemDetails[nNodePos].dtCreated = tmStaticSet.AddMilliseconds(Convert.ToInt64(nodeChildXml.InnerText) * 1000).ToLocalTime();
				        nodeChildXml.RemoveAll();

				        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/modified");
                        pItemDetails[nNodePos].dtModified = tmStaticSet.AddMilliseconds(Convert.ToInt64(nodeChildXml.InnerText) * 1000).ToLocalTime();
				        nodeChildXml.RemoveAll();

				        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/name");
				        pItemDetails[nNodePos].strName = nodeChildXml.InnerText;
				        nodeChildXml.RemoveAll();

				        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/shared");
                        if (nodeChildXml.InnerText == "True")
                            pItemDetails[nNodePos].bShared = true;
                        else
                            pItemDetails[nNodePos].bShared = false;
				        nodeChildXml.RemoveAll();

				        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/parent");
				        pItemDetails[nNodePos].szParentUrl = nodeChildXml.Attributes["xlink:href"].Value;
				        nodeChildXml.RemoveAll();
			        }
		        }
	        }
	        catch(WebException wEx)
	        {
                nStatusCode = OnException(wEx);
		        return null;
	        }
	        catch
	        {
		        if(pItemDetails != null)
			        pItemDetails[0].nTotalItem -= 1;
		        else
		        {
			        nStatusCode = -3;
			        return null;
		        }
	        }

	        return pItemDetails;
        }

        public bool DownloadFile(String strSource, String strDestination, double dblFileSizeInBytes, ref int nStatusCode, CallbackIncrementProgress IncProgress, CallbackContinueRunning ContinueRun) 
        {
            bool bStatus = true;
            nStatusCode = 0;

	        try
	        {
                foreach (DriveInfo driveInfo in DriveInfo.GetDrives())
                {
                    if (driveInfo.Name == strDestination.Substring(0, 3))
                    {
                        if (dblFileSizeInBytes > driveInfo.TotalFreeSpace)
                        {
                            nStatusCode = -5;
                            return false;
                        }
                        break;
                    }
                }

		        HttpWebRequest webRequest = null;
                OnGetRequest(ref webRequest, strSource, "", "1", "Get", null);

		        HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
		        nStatusCode = 200;
                if (!OnSaveResponseFile(response.GetResponseStream(), strDestination, 0, IncProgress, ContinueRun))
                {
                    nStatusCode = -4;
                    bStatus = false;
                }

			    webRequest.Abort();
			    response.Close();
	        }
	        catch(WebException wEx)
	        {
                nStatusCode = OnException(wEx);
		        bStatus = false;
	        }
	        catch
	        {
		        nStatusCode = -3;
		        bStatus = false;
	        }

            return bStatus;
        }

        public String GetETag(String strContainUrl, ref int nStatusCode)
        {
            nStatusCode = 0;
	        String StrRet = "";
	        try
	        {
                int nTypeLen = strContainUrl.LastIndexOf('/') + 1;
                String strBuff = strContainUrl.Substring(nTypeLen, strContainUrl.Length - nTypeLen);
                if (strBuff == "content" || strBuff == "contents")
                    strContainUrl = strContainUrl.Substring(0, nTypeLen-1);

		        //Get Parent Url
		        HttpWebRequest webRequest = null;
                OnGetRequest(ref webRequest, strContainUrl, "", "", "Get", null);

		        HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
		        nStatusCode = 200;

                XmlDocument m_xmlSingleFile = new XmlDocument();
		        m_xmlSingleFile.LoadXml(OnGetResponseString(response.GetResponseStream()));
		        webRequest.Abort();
		        response.Close();

                XmlNode nodeXml = m_xmlSingleFile.SelectSingleNode("/file");
		        if(nodeXml != null)
		        {
			        nodeXml = m_xmlSingleFile.SelectSingleNode("/file/parent");
			        StrRet = nodeXml.Attributes["xlink:href"].Value;
			        nodeXml.RemoveAll();
		        }

                nodeXml = m_xmlSingleFile.SelectSingleNode("/container");
		        if(nodeXml != null)
		        {
			        nodeXml = m_xmlSingleFile.SelectSingleNode("/container/parent");
			        StrRet = nodeXml.Attributes["xlink:href"].Value;
			        nodeXml.RemoveAll();
		        }
		        m_xmlSingleFile.RemoveAll();

		        //check url infomation
                OnGetRequest(ref webRequest, StrRet, "", "", "Get", null);
		        response = (HttpWebResponse)webRequest.GetResponse();		
		        m_xmlSingleFile.LoadXml(OnGetResponseString(response.GetResponseStream()));
		        webRequest.Abort();
		        response.Close();
		        m_xmlSingleFile.RemoveAll();

		        //get eTag
		        strContainUrl += "/" + strBuff;
                OnGetRequest(ref webRequest, strContainUrl, "", "", "Get", null);
		        response = (HttpWebResponse)webRequest.GetResponse();
		        StrRet = response.Headers.Get("eTag");
		        webRequest.Abort();
		        response.Close();
	        }
	        catch(WebException wEx)
	        {
                nStatusCode = OnException(wEx);
		        return "";
	        }
	        catch
	        {
		        nStatusCode = -3;
		        return "";
	        }

	        return StrRet;
        }

        public ItemDetails GetContinerResult(String strContainUrl, ref int nStatusCode)
        {
            nStatusCode = 0;
	        ItemDetails pItemDetails = null;
	        DateTime tmStaticSet = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            //DateTime tmObj;
	        try
	        {
		        HttpWebRequest webRequest = null;
                OnGetRequest(ref webRequest, strContainUrl, "", "", "Get", null);

		        HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
		        nStatusCode = 200;

                //String str1 = OnGetResponseString(response.GetResponseStream());

		        XmlDocument m_xmlFileList = new XmlDocument();
                m_xmlFileList.LoadXml(OnGetResponseString(response.GetResponseStream()));

		        webRequest.Abort();
		        response.Close();

		        pItemDetails = new ItemDetails();
		        String strItemType = "";
		        XmlNode nodeChildXml;
                //pItemDetails.strETag = response.Headers.Get("eTag");

                nodeChildXml = m_xmlFileList.SelectSingleNode("/container");
		        if( nodeChildXml != null )
		        {
			        strItemType = "/container";
			        pItemDetails.szItemType = "DIRECTORY";

			        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/contents");
			        pItemDetails.szContentUrl = nodeChildXml.Attributes["xlink:href"].Value;
			        nodeChildXml.RemoveAll();
		        }
				
                nodeChildXml = m_xmlFileList.SelectSingleNode("/file");
		        if( nodeChildXml != null )
		        {
			        strItemType = "/file";
			        pItemDetails.szItemType = "FILE";

			        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/mime_type");
			        pItemDetails.szMimeType = nodeChildXml.InnerText;
			        nodeChildXml.RemoveAll();

			        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/public");
                    if( nodeChildXml.InnerText == "True")
			            pItemDetails.bPublic = true;
                    else
                        pItemDetails.bPublic = false;
			        nodeChildXml.RemoveAll();

			        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/content");
			        pItemDetails.szContentUrl = nodeChildXml.Attributes["xlink:href"].Value;
			        nodeChildXml.RemoveAll();
		        }

                nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/accessed");
                pItemDetails.dtAccessed = tmStaticSet.AddMilliseconds(Convert.ToInt64(nodeChildXml.InnerText) * 1000).ToLocalTime();
		        nodeChildXml.RemoveAll();

		        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/bytes");
		        pItemDetails.dblSizeInBytes = Convert.ToDouble( nodeChildXml.InnerText );
		        nodeChildXml.RemoveAll();

		        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/created");
                pItemDetails.dtCreated = tmStaticSet.AddMilliseconds(Convert.ToInt64(nodeChildXml.InnerText) * 1000).ToLocalTime();
		        nodeChildXml.RemoveAll();

		        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/modified");
                pItemDetails.dtModified = tmStaticSet.AddMilliseconds(Convert.ToInt64(nodeChildXml.InnerText) * 1000).ToLocalTime();
		        nodeChildXml.RemoveAll();

		        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/name");
		        pItemDetails.strName = nodeChildXml.InnerText;
		        nodeChildXml.RemoveAll();

		        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/shared");
                if (nodeChildXml.InnerText == "True")
                    pItemDetails.bShared = true;
                else
                    pItemDetails.bShared = false;
		        nodeChildXml.RemoveAll();

		        nodeChildXml = m_xmlFileList.SelectSingleNode(strItemType + "/parent");
		        pItemDetails.szParentUrl = nodeChildXml.Attributes["xlink:href"].Value;
		        nodeChildXml.RemoveAll();
	        }
	        catch(WebException wEx)
	        {
                nStatusCode = OnException(wEx);
		        return null;
	        }
	        catch
	        {
		        if(pItemDetails != null)
			        pItemDetails.nTotalItem -= 1;
		        else
		        {
			        nStatusCode = -3;
			        return null;
		        }
	        }

	        return pItemDetails;
        }

        private class OnJSONHelper
        {
            public static T Deserialise<T>(string szJson)
            {
                MemoryStream MemStream = new MemoryStream(Encoding.UTF8.GetBytes(szJson));
                DataContractJsonSerializer JSONSerialiser = new DataContractJsonSerializer(typeof(T));
                return (T)JSONSerialiser.ReadObject(MemStream);
            }
        }

        [DataContract]
        private class OnGetParentUri
        {
            [DataMember]
            public string objectName { get; set; }

            [DataMember]
            public string parentURI { get; set; }
        }

        [DataContract]
        private class OnGetNQValue
        {
            [DataMember(Name = "value")]
            public List<String> value { get; set; }
        }

        [DataContract]
        private class OnGetNQQueueValue
        {
            [DataMember]
            public string queueValues { get; set; }
        }

        [DataContract]
        private class GetValue
        {
            [DataMember]
            public string cdmi_event_result { get; set; }
            [DataMember]
            public string cdmi_event_time { get; set; }
            [DataMember]
            public string objectID { get; set; }
            [DataMember]
            public string cdmi_event_user { get; set; }
            [DataMember]
            public string cdmi_event { get; set; }
            [DataMember]
            public string parentID { get; set; }
            [DataMember]
            public string objectType { get; set; }
            [DataMember]
            public string parentURI { get; set; }
            [DataMember]
            public string domainURI { get; set; }
            [DataMember]
            public string objectName { get; set; }
            [DataMember]
            public OnMetadata metadata { get; set; }
        }

        [DataContract]
        public class OnMetadata
        {
            [DataMember]
            public string mezeo_exported_path { get; set; }
            [DataMember]
            public string cdmi_hash { get; set; }
            [DataMember]
            public string cdmi_size { get; set; }
        }

        public NQLengthResult NQGetLength(String StrUri, String StrQueueName, ref int nStatusCode)
        {
            nStatusCode = 0;
            StrUri += "/" + StrQueueName + "?queueValues";
            
            String StrRev;
            Int32 nRet = 0;
            NQLengthResult pNQLengthResult = null;
            try
            {
                HttpWebRequest webRequest = null;
                OnNotificationQueue(ref webRequest, StrUri, "GET");

                HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
                nStatusCode = 200;
                StrRev = OnGetResponseString(response.GetResponseStream());

                webRequest.Abort();
                response.Close();

                pNQLengthResult = new NQLengthResult();

                OnGetNQQueueValue pGetNQQueueValue = OnJSONHelper.Deserialise<OnGetNQQueueValue>(StrRev);
                nRet = pGetNQQueueValue.queueValues.IndexOf('-');

                if (nRet > -1)
                {
                    StrRev = pGetNQQueueValue.queueValues.Substring(0, nRet);
                    nRet++;
                    nRet = Convert.ToInt32(pGetNQQueueValue.queueValues.Substring(nRet, pGetNQQueueValue.queueValues.Length - nRet));

                    pNQLengthResult.nStart = Convert.ToInt32(StrRev);
                    pNQLengthResult.nEnd = nRet;
                }
                else
                {
                    pNQLengthResult.nStart = -1;
                    pNQLengthResult.nEnd = -1;
                }
            }
            catch (WebException wEx)
            {
                nStatusCode = OnException(wEx);
                return null;
            }
            catch
            {
                nStatusCode = -3;
                return null;
            }
            return pNQLengthResult;
        }

        public NQDetails[] NQGetData(String StrUri, String StrQueueName, Int32 nCountValue, ref int nStatusCode)
        {
            nStatusCode = 0;
            StrUri += "/" + StrQueueName +"?values:" + Convert.ToString(nCountValue);
            String StrRev;
            NQDetails[] pNQDetails = null;
            try
            {
                HttpWebRequest webRequest = null;
                OnNotificationQueue(ref webRequest, StrUri, "GET");

                HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
                nStatusCode = 200;
                StrRev = OnGetResponseString(response.GetResponseStream());

                webRequest.Abort();
                response.Close();

                OnGetNQValue pGetNQValue = OnJSONHelper.Deserialise<OnGetNQValue>(StrRev);
                GetValue pGetValue;
                int nCount = pGetNQValue.value.Count();
                if (nCount > 0)
                {
                    pNQDetails = new NQDetails[nCount];
                    for (int i = 0; i < nCount; i++)
                    {
                        pGetValue = OnJSONHelper.Deserialise<GetValue>(pGetNQValue.value[i].ToString());
                        if (pGetValue != null)
                        {
                            pNQDetails[i] = new NQDetails();
                            pNQDetails[i].StrEventResult = pGetValue.cdmi_event_result;
                            pNQDetails[i].StrEventTime = pGetValue.cdmi_event_time;
                            pNQDetails[i].StrObjectID = pGetValue.objectID;
                            pNQDetails[i].StrEventUser = pGetValue.cdmi_event_user;
                            pNQDetails[i].StrEvent = pGetValue.cdmi_event;
                            pNQDetails[i].StrParentID = pGetValue.parentID;
                            if (pGetValue.objectType == "application/cdmi-container")
                                pNQDetails[i].StrObjectType = "DIRECTORY";
                            else if (pGetValue.objectType == "application/cdmi-object")
                                pNQDetails[i].StrObjectType = "FILE";
                            else
                                pNQDetails[i].StrObjectType = "UNSUPPORTED_TYPE";
                            pNQDetails[i].StrParentUri = pGetValue.parentURI;
                            pNQDetails[i].StrDomainUri = pGetValue.domainURI;
                            pNQDetails[i].StrObjectName = pGetValue.objectName;
                            pNQDetails[i].StrMezeoExportedPath = pGetValue.metadata.mezeo_exported_path;
                            if (pGetValue.metadata.cdmi_hash == null)
                                pNQDetails[i].StrHash = "";
                            else
                                pNQDetails[i].StrHash = pGetValue.metadata.cdmi_hash;
                            pNQDetails[i].lSize = Convert.ToInt32(pGetValue.metadata.cdmi_size);
                            pNQDetails[i].nTotalNQ = nCount;
                        }
                        pGetValue = null;
                    }
                }
            }
            catch (WebException wEx)
            {
                nStatusCode = OnException(wEx);
                return null;
            }
            catch
            {
                nStatusCode = -3;
                return null;
            }
            return pNQDetails;
        }

        public bool NQCreate(String StrUri, String StrQueueName, String StrStarts, ref int nStatusCode)
        {             
            String StrJSON = "{\"metadata\":{\"cdmi_queue_type\":\"cdmi_notification_queue\",";
            StrJSON += "\"cdmi_notification_events\":[\"cdmi_create_complete\",\"cdmi_modify_complete\",\"cdmi_delete\",";
            StrJSON += "\"cdmi_rename\",\"cdmi_copy\"],\"cdmi_scope_specification\":[{\"parentURI\":\"starts ";
            StrJSON += StrStarts;
            StrJSON += "/\"}],\"cdmi_results_specification\":{\"cdmi_event\":\"\",\"cdmi_event_result\":\"\",";
            StrJSON += "\"cdmi_event_time\":\"\",\"cdmi_event_user\":\"\",\"objectName\":\"\",\"objectID\":\"\",\"objectType\":\"\",\"parentURI\":\"\",";
            StrJSON += "\"parentID\":\"\",\"domainURI\":\"\",\"metadata\":{\"mezeo_exported_path\":\"\",\"cdmi_size\":\"\",\"cdmi_hash\":\"\"}}}}";
            

            StrUri += "/" + StrQueueName;

            try
            {
                HttpWebRequest webRequest = null;
                webRequest = (HttpWebRequest)WebRequest.Create(StrUri);
                webRequest.Credentials = new NetworkCredential(m_strLoginName, m_strPassword);
                webRequest.PreAuthenticate = true;
                webRequest.Method = "PUT";
                webRequest.KeepAlive = false;
                webRequest.Headers.Add("X-CDMI-Specification-Version", "1.0.1");
                webRequest.ContentType = "application/cdmi-queue";
                webRequest.Accept = "application/cdmi-queue";
                webRequest.Timeout = nTimeout;

                Stream writeStream = webRequest.GetRequestStream();
                if (writeStream != null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(StrJSON);
                    writeStream.Write(bytes, 0, bytes.Length);
                    writeStream.Close();
                }
                nStatusCode = 202;
                HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();

                webRequest.Abort();
                response.Close();
            }
            catch (WebException wEx)
            {
                nStatusCode = OnException(wEx);
                return false;
            }
            catch
            {
                nStatusCode = -3;
                return false;
            }

            return true;
        }

        public bool NQDeleteValue(String StrUri, String StrQueueName, int nCountValue, ref int nStatusCode)
        {
            StrUri += "/" + StrQueueName + "?values:" + Convert.ToString( nCountValue );

            try
            {
                HttpWebRequest webRequest = null;
                OnNotificationQueue(ref webRequest, StrUri, "DELETE");

                HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
                nStatusCode = 200;

                webRequest.Abort();
                response.Close();

            }
            catch (WebException wEx)
            {
                nStatusCode = OnException(wEx);
                return false;
            }
            catch
            {
                nStatusCode = -3;
                return false;
            }
            return true;
        }

        public bool NQDelete(String StrUri, String StrQueueName, ref int nStatusCode)
        {
            StrUri += "/" + StrQueueName;

            try
            {
                HttpWebRequest webRequest = null;
                OnNotificationQueue(ref webRequest, StrUri, "DELETE");

                HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
                nStatusCode = 200;

                webRequest.Abort();
                response.Close();

            }
            catch (WebException wEx)
            {
                nStatusCode = OnException(wEx);
                return false;
            }
            catch
            {
                nStatusCode = -3;
                return false;
            }
            return true;
        }

        public String NQParentUri(String StrUri, ref int nStatusCode)
        {
            String StrRev;
            OnGetParentUri pGetParentUri;
            try
            {
                HttpWebRequest webRequest = null;
                OnNotificationQueue(ref webRequest, StrUri, "GET");

                HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
                nStatusCode = 200;
                StrRev = OnGetResponseString(response.GetResponseStream());

                webRequest.Abort();
                response.Close();

                pGetParentUri = new OnGetParentUri();
                pGetParentUri = OnJSONHelper.Deserialise<OnGetParentUri>(StrRev);
            }
            catch (WebException wEx)
            {
                nStatusCode = OnException(wEx);
                return "";
            }
            catch
            {
                nStatusCode = -3;
                return "";
            }
            return pGetParentUri.parentURI + pGetParentUri.objectName;
        }

        public String NewContainer(String strNewContainer, String strContentsResource, ref int nStatusCode)
        {
            String StrRef = "";
            nStatusCode = 0;
	        String strXML = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";
	        strXML += "<container xmlns:xlink=\"http://www.w3.org/1999/xlink\">";
	        strXML += "<modified>0</modified>";
	        strXML += "<name>";
	        strXML += HttpUtility.HtmlEncode(strNewContainer);
	        strXML += "</name>";
	        strXML += "<created>0</created>";
	        strXML += "</container>";

	        try
	        {
		        HttpWebRequest webRequest = null;
		        OnPostAndPutRequest(ref webRequest, strContentsResource, "", "application/vnd.csp.container-info+xml", strXML, "", "POST", "", null);

		        HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
		        nStatusCode = 201;
		        StrRef = OnGetResponseString(response.GetResponseStream());
                StrRef = StrRef.Substring(0, (StrRef.Length - Environment.NewLine.Length));
		        webRequest.Abort();
		        response.Close();

	        }
	        catch(WebException wEx)
	        {
                nStatusCode = OnException(wEx);
		        return "";
	        }
	        catch
	        {
		        nStatusCode = -3;
		        return "";
	        }

	        return StrRef;
        }

        public String UploadingFileOnResume(String strSource, String strDestination, ref int nStatusCode, CallbackIncrementProgress IncProcess)
        {
            nStatusCode = 0;
            String StrRet = "";
            String strFileName = strSource;

            int nNameLoc = strSource.LastIndexOf('\\');

            if (nNameLoc > 0)
                strFileName = strSource.Substring(nNameLoc);

            // Create a new random form boundary using the System time
            String strBoundary = DateTime.Now.Ticks.ToString("x") + DateTime.Now.Ticks.ToString("x");

            // File payload section
            String strLoadHeader = "--" + strBoundary + "\r\n";
            strLoadHeader += "Content-Disposition: form-data; name=\"Filedata\"; filename=\"" + strFileName + "\"\r\n";
            strLoadHeader += "Content-Type: " + OnGetMimeType(strSource) + "\r\n\r\n";
            HttpWebRequest webRequest = null;
            HttpWebResponse response = null;

            try
            {
                bool bStatus = OnPostAndPutRequest(ref webRequest, strDestination, strSource, "multipart/form-data; boundary=" + strBoundary, strLoadHeader, "\r\n--" + strBoundary + "--\r\n", "POST", "", IncProcess);

                if (null != webRequest)
                {
                    response = (HttpWebResponse)webRequest.GetResponse();
                    nStatusCode = 201;
                    //StrRet = OnGetResponseString(response.GetResponseStream());
                    //StrRet = StrRet.Substring(0, (StrRet.Length - Environment.NewLine.Length));
                    webRequest.Abort();
                    response.Close();
                }
                if (!bStatus)
                {
                    if (uploadStoppedEvent != null)
                    {
                        nStatusCode = -4;
                        uploadStoppedEvent(strSource, StrRet);
                    }
                }
            }
            catch (WebException wEx)
            {
                nStatusCode = OnException(wEx);
                return "";
            }
            catch
            {
                nStatusCode = -3;
                return "";
            }

            finally
            {
                if (webRequest != null)
                    webRequest.Abort();
                if (response != null)
                    response.Close();
            }
            return StrRet;
        }

        public String UploadingFile(String strSource, String strDestination, ref int nStatusCode, CallbackIncrementProgress IncProcess)
        {
            nStatusCode = 0;
            String StrRet = "";
	        String strFileName = strSource;

            int nNameLoc = strSource.LastIndexOf('\\');

            if (nNameLoc > 0)
		        strFileName = strSource.Substring(nNameLoc);

	        // Create a new random form boundary using the System time
	        String strBoundary = DateTime.Now.Ticks.ToString("x") + DateTime.Now.Ticks.ToString("x");

	        // File payload section
            String strLoadHeader = "--" + strBoundary + "\r\n";
            strLoadHeader += "Content-Disposition: form-data; name=\"Filedata\"; filename=\"" + strFileName + "\"\r\n";
            strLoadHeader += "Content-Type: " + OnGetMimeType(strSource) + "\r\n\r\n";
            HttpWebRequest webRequest = null;
            HttpWebResponse response = null;

            try
            {
                bool bStatus = OnPostAndPutRequest(ref webRequest, strDestination, strSource, "multipart/form-data; boundary=" + strBoundary, strLoadHeader, "\r\n--" + strBoundary + "--\r\n", "POST", "", IncProcess);

                if (null != webRequest)
                {
                    response = (HttpWebResponse)webRequest.GetResponse();
                    nStatusCode = 201;
                    StrRet = OnGetResponseString(response.GetResponseStream());
                    StrRet = StrRet.Substring(0, (StrRet.Length - Environment.NewLine.Length));
                    webRequest.Abort();
                    response.Close();
                }
                if (!bStatus)
                {
                    if (uploadStoppedEvent != null)
                    {
                        nStatusCode = -4;
                        uploadStoppedEvent(strSource, StrRet);
                    }
                }
            }
            catch (WebException wEx)
            {
                nStatusCode = OnException(wEx);
                return "";
            }
            catch
            {
                nStatusCode = -3;
                return "";
            }

            finally
            {
                if (webRequest != null)
                    webRequest.Abort();
                if (response != null)
                    response.Close();
            }
            return StrRet;
        }

        public bool OverWriteFile(String strSource, String strDestination, ref int nStatusCode, CallbackIncrementProgress IncProgress)
        {
            nStatusCode = 0;
            String StrRet = "";
            String strFileName = strSource;
            bool success = true;

            int nNameLoc = strSource.LastIndexOf('\\');

            if (nNameLoc > 0)
                strFileName = strSource.Substring(nNameLoc);

            // Create a new random form boundary using the System time
            String strBoundary = DateTime.Now.Ticks.ToString("x") + DateTime.Now.Ticks.ToString("x");

            // File payload section
            String strLoadHeader = "--" + strBoundary + "\r\n";
            strLoadHeader += "Content-Disposition: form-data; name=\"Filedata\"; filename=\"" + strFileName + "\"\r\n";
            strLoadHeader += "Content-Type: " + OnGetMimeType(strSource) + "\r\n\r\n";
            HttpWebRequest webRequest = null;
            HttpWebResponse response = null;

            try
            {
                success = OnPostAndPutRequest(ref webRequest, strDestination, strSource, "multipart/form-data; boundary=" + strBoundary, strLoadHeader, "\r\n--" + strBoundary + "--\r\n", "POST", "", IncProgress);

                if (null != webRequest)
                {
                    response = (HttpWebResponse)webRequest.GetResponse();
                    if (response.StatusCode == HttpStatusCode.Created)
                    {
                        nStatusCode = 201;
                        StrRet = OnGetResponseString(response.GetResponseStream());
                        StrRet = StrRet.Substring(0, (StrRet.Length - Environment.NewLine.Length));
                    }
                    else if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        nStatusCode = 204;
                    }

                    webRequest.Abort();
                    response.Close();
                }
                if (!success)
                {
                    if (uploadStoppedEvent != null)
                    {
                        nStatusCode = -4;
                        uploadStoppedEvent(strSource, StrRet);
                    }
                }
            }
            catch (WebException wEx)
            {
                nStatusCode = OnException(wEx);
                success = false;
            }
            catch
            {
                nStatusCode = -3;
                success = false;
            }

            finally
            {
                if (webRequest != null)
                    webRequest.Abort();
                if (response != null)
                    response.Close();
            }
            return success;
        }

        public ItemResults GetParentName(String strContents, ref int nStatusCode)
        {
            nStatusCode = 0;
	       
	        ItemResults pItemResults = new ItemResults();
	        try
	        {
		        HttpWebRequest webRequest = null;
                OnGetRequest(ref webRequest, strContents, "", "1", "Get", null);

		        HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();

		        nStatusCode = 200;
		        strContents = OnGetResponseString(response.GetResponseStream());
		        webRequest.Abort();
		        response.Close();

		        XmlDocument m_xmlSingleFile = new XmlDocument();
		        m_xmlSingleFile.LoadXml(strContents);

		        XmlNode nodeXml = m_xmlSingleFile.SelectSingleNode("/container/parent");
		        pItemResults.szContentsUrl = nodeXml.Attributes["xlink:href"].Value;
		        nodeXml.RemoveAll();

		        nodeXml = m_xmlSingleFile.SelectSingleNode("/container/name");
		        pItemResults.szName = nodeXml.InnerText;
		        nodeXml.RemoveAll();
	        }
	        catch(WebException wEx)
	        {
                nStatusCode = OnException(wEx);
		        return null;
	        }
	        catch
	        {
		        nStatusCode = -3;
		        return null;
	        }

	        return pItemResults;
        }

        public String GetDeletedInfo(String strContainUrl, ref int nStatusCode)
        {
            nStatusCode = 0;
	        String StrRet = "";
	        try
	        {
		        HttpWebRequest webRequest = null;
                OnGetRequest(ref webRequest, strContainUrl, "", "", "Get", null);

		        HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
		        nStatusCode = 200;

		        XmlDocument m_xmlSingleFile = new XmlDocument();
		        m_xmlSingleFile.LoadXml(OnGetResponseString(response.GetResponseStream()));
		        webRequest.Abort();
		        response.Close();

		        XmlNode nodeXml = m_xmlSingleFile.SelectSingleNode("/file");
		        if(nodeXml != null)
		        {
			        nodeXml = m_xmlSingleFile.SelectSingleNode("/file/parent");
			        StrRet = nodeXml.Attributes["xlink:href"].Value;
			        nodeXml.RemoveAll();
		        }

                nodeXml = m_xmlSingleFile.SelectSingleNode("/container");
		        if(nodeXml != null)
		        {
			        nodeXml = m_xmlSingleFile.SelectSingleNode("/container/parent");
			        StrRet = nodeXml.Attributes["xlink:href"].Value;
			        nodeXml.RemoveAll();
		        }

	        }
	        catch(WebException wEx)
	        {
                nStatusCode = OnException(wEx);
		        return "";
	        }
	        catch
	        {
		        nStatusCode = -3;
		        return "";
	        }

	        return StrRet;
        }

        public bool FileRename(String strPath, String strNewName, String strMineType, bool bPublic, ref int nStatusCode)
        {
            nStatusCode = 0;
	        String strXML = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";
	        strXML += "<file xmlns:xlink=\"http://www.w3.org/1999/xlink\">";
	        strXML += "<modified>0</modified>";
	        strXML += "<name>";
	        strXML += HttpUtility.HtmlEncode(strNewName);
	        strXML += "</name>";
	        strXML += "<mime_type>";
	        strXML += strMineType;
	        strXML += "</mime_type>";
	        strXML += "<public>";
	        strXML += Convert.ToString(bPublic);
	        strXML += "</public>";
	        strXML += "<created>0</created>";
	        strXML += "</file>";

	        try
	        {
		        HttpWebRequest webRequest = null;
		        OnPostAndPutRequest(ref webRequest, strPath, "", "application/vnd.csp.file-info+xml", strXML, "", "PUT", "", null);

		        HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
		        nStatusCode = 204;
		        webRequest.Abort();
		        response.Close();             
	        }
	        catch(WebException wEx)
	        {
                nStatusCode = OnException(wEx);
		        return false;
	        }
	        catch
	        {
		        nStatusCode = -3;
		        return false;
	        }
	        return true;
        }

        public bool ContainerRename(String strPath, String strNewName, ref int nStatusCode)
        {
            nStatusCode = 0;
	        String strXML = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";
	        strXML += "<container xmlns:xlink=\"http://www.w3.org/1999/xlink\">";
	        strXML += "<modified>0</modified>";
	        strXML += "<name>";
	        strXML += HttpUtility.HtmlEncode(strNewName);
	        strXML += "</name>";
	        strXML += "<created>0</created>";
	        strXML += "</container>";

	        try
	        {
		        HttpWebRequest webRequest = null;
		        OnPostAndPutRequest(ref webRequest, strPath, "", "application/vnd.csp.container-info+xml", strXML, "", "PUT", "", null);

		        HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
		        nStatusCode = 204;
		        webRequest.Abort();
		        response.Close();
	        }
	        catch(WebException wEx)
	        {
                nStatusCode = OnException(wEx);
		        return false;
	        }
	        catch
	        {
		        nStatusCode = -3;
		        return false;
	        }

	        return true;
        }

        public bool Delete(String strPath, ref int nStatusCode)
        {
            nStatusCode = 0;
	        try
	        {
		        HttpWebRequest webRequest = null;
                OnGetRequest(ref webRequest, strPath, "", "", "DELETE", null);

		        HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
		        nStatusCode = 204;
		        webRequest.Abort();
		        response.Close();
	        }
	        catch(WebException wEx)
	        {
                nStatusCode = OnException(wEx);
		        return false;
	        }
	        catch
	        {
		        nStatusCode = -3;
		        return false;
	        }

	        return true;
        }

        public bool FileMove(String strPath, String strNewName, String strMineType, bool bPublic, String StrParent, ref int nStatusCode)
        {
            nStatusCode = 0;
            String strXML = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";
            strXML += "<file xmlns:xlink=\"http://www.w3.org/1999/xlink\">";
            strXML += "<name>";
            strXML += HttpUtility.HtmlEncode(strNewName);
            strXML += "</name>";
            strXML += "<mime_type>";
            strXML += strMineType;
            strXML += "</mime_type>";
            strXML += "<public>";
            strXML += Convert.ToString(bPublic);
            strXML += "</public>";
            strXML += "<parent xlink:href=\"";
            strXML += StrParent;
            strXML += "\" xlink:type=\"simple\"> </parent>";
            strXML += "</file>";

            try
            {
                HttpWebRequest webRequest = null;
                OnPostAndPutRequest(ref webRequest, strPath, "", "application/vnd.csp.file-info+xml", strXML, "", "PUT", "", null);

                HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
                nStatusCode = 204;
                webRequest.Abort();
                response.Close();
            }
            catch (WebException wEx)
            {
                nStatusCode = OnException(wEx);
                return false;
            }
            catch
            {
                nStatusCode = -3;
                return false;
            }
            return true;
        }

        public bool ContainerMove(String strPath, String strNewName, String strMineType, bool bPublic, String StrParent, ref int nStatusCode)
        {
            nStatusCode = 0;
            String strXML = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";
            strXML += "<container xmlns:xlink=\"http://www.w3.org/1999/xlink\">";
            strXML += "<name>";
            strXML += HttpUtility.HtmlEncode(strNewName);
            strXML += "</name>";
            strXML += "<mime_type>";
            strXML += strMineType;
            strXML += "</mime_type>";
            strXML += "<public>";
            strXML += Convert.ToString(bPublic);
            strXML += "</public>";
            strXML += "<parent xlink:href=\"";
            strXML += StrParent;
            strXML += "\" xlink:type=\"simple\"> </parent>";
            strXML += "</container>";

            try
            {
                HttpWebRequest webRequest = null;
                OnPostAndPutRequest(ref webRequest, strPath, "", "application/vnd.csp.container-info+xml", strXML, "", "PUT", "", null);

                HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
                nStatusCode = 204;
                webRequest.Abort();
                response.Close();
            }
            catch (WebException wEx)
            {
                nStatusCode = OnException(wEx);
                return false;
            }
            catch
            {
                nStatusCode = -3;
                return false;
            }
            return true;
        }

        public String Copy(String strSource, String StrDestination, String StrType, ref int nStatusCode)
        {
            nStatusCode = 0;
            String strXML = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";
            if (StrType == "FILE")
            {
                strXML += "<file xmlns:xlink=\"http://www.w3.org/1999/xlink\">";
                strXML += "</file>";
            }
            else if (StrType == "DIRECTORY")
            {
                strXML += "<container xmlns:xlink=\"http://www.w3.org/1999/xlink\">";
                strXML += "</container>";
            }
            else
                return "";

            String StrRet = "";

            try
            {
                HttpWebRequest webRequest = null;
                OnPostAndPutRequest(ref webRequest, strSource, "", "application/vnd.csp.file-info+xml", strXML, "", "POST", StrDestination, null);

                HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();

                StrRet = OnGetResponseString(response.GetResponseStream());
                StrRet = StrRet.Substring(0, (StrRet.Length - Environment.NewLine.Length));
                nStatusCode = 201;
                webRequest.Abort();
                response.Close();
            }
            catch (WebException wEx)
            {
                nStatusCode = OnException(wEx);
                return "";
            }
            catch
            {
                nStatusCode = -3;
                return "";
            }
            return StrRet;
        }

        public NSResult GetNamespaceResult(String StrUri, String StrObjectType, ref int nStatusCode)
        {
            NSResult pNSResult;
            String strItemType = "";
            DateTime tmStaticSet = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            try
            {
                HttpWebRequest webRequest = null;
                if (StrObjectType == "FILE")
                {
                    OnGetRequest(ref webRequest, StrUri, "application/vnd.csp.file-info+xml", "", "Get", null);
                    strItemType = "/file";
                }
                else if (StrObjectType == "DIRECTORY")
                {
                    OnGetRequest(ref webRequest, StrUri, "application/vnd.csp.container-info+xml", "", "Get", null);
                    strItemType = "/container";
                }

                HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
                nStatusCode = 200;

                XmlDocument m_xmlList = new XmlDocument();
                m_xmlList.LoadXml( OnGetResponseString(response.GetResponseStream()) );

                webRequest.Abort();
                response.Close();

                pNSResult = new NSResult();
                XmlNode nodeChildXml;
                pNSResult.StrType = StrObjectType;

                nodeChildXml = m_xmlList.SelectSingleNode(strItemType + "/contents");
                if (nodeChildXml != null)
                {
                    pNSResult.StrContentsUri = nodeChildXml.Attributes["xlink:href"].Value;
                    nodeChildXml.RemoveAll();
                }

                nodeChildXml = m_xmlList.SelectSingleNode(strItemType + "/mime_type");
                if (nodeChildXml != null)
                {
                    pNSResult.StrMimeType = nodeChildXml.InnerText;
                    nodeChildXml.RemoveAll();
                }

                nodeChildXml = m_xmlList.SelectSingleNode(strItemType + "/public");
                if (nodeChildXml != null)
                { 
                    if (nodeChildXml.InnerText == "True")
                        pNSResult.bPublic = true;
                    else
                        pNSResult.bPublic = false;
                    nodeChildXml.RemoveAll();
                 }

                nodeChildXml = m_xmlList.SelectSingleNode(strItemType + "/content");
                if (nodeChildXml != null)
                {
                    pNSResult.StrContentsUri = nodeChildXml.Attributes["xlink:href"].Value;
                    nodeChildXml.RemoveAll();
                }

                nodeChildXml = m_xmlList.SelectSingleNode(strItemType + "/accessed");
                pNSResult.dtAccessed = tmStaticSet.AddMilliseconds(Convert.ToInt64(nodeChildXml.InnerText) * 1000).ToLocalTime();
                nodeChildXml.RemoveAll();

                nodeChildXml = m_xmlList.SelectSingleNode(strItemType + "/bytes");
                pNSResult.dblSizeInBytes = Convert.ToDouble(nodeChildXml.InnerText);
                nodeChildXml.RemoveAll();

                nodeChildXml = m_xmlList.SelectSingleNode(strItemType + "/created");
                pNSResult.dtCreated = tmStaticSet.AddMilliseconds(Convert.ToInt64(nodeChildXml.InnerText) * 1000).ToLocalTime();
                nodeChildXml.RemoveAll();

                nodeChildXml = m_xmlList.SelectSingleNode(strItemType + "/modified");
                pNSResult.dtModified = tmStaticSet.AddMilliseconds(Convert.ToInt64(nodeChildXml.InnerText) * 1000).ToLocalTime();
                nodeChildXml.RemoveAll();

                nodeChildXml = m_xmlList.SelectSingleNode(strItemType + "/name");
                pNSResult.StrName = nodeChildXml.InnerText;
                nodeChildXml.RemoveAll();

                nodeChildXml = m_xmlList.SelectSingleNode(strItemType + "/version");
                pNSResult.StrVersion = nodeChildXml.InnerText;
                nodeChildXml.RemoveAll();

                nodeChildXml = m_xmlList.SelectSingleNode(strItemType + "/shared");
                if (nodeChildXml.InnerText == "True")
                    pNSResult.bShared = true;
                else
                    pNSResult.bShared = false;
                nodeChildXml.RemoveAll();

                nodeChildXml = m_xmlList.SelectSingleNode(strItemType + "/parent");
                pNSResult.StrParentUri = nodeChildXml.Attributes["xlink:href"].Value;
                nodeChildXml.RemoveAll();
            }
            catch (WebException wEx)
            {
                nStatusCode = OnException(wEx);
                return null;
            }
            catch
            {
                nStatusCode = -3;
                return null;
            }
            return pNSResult;
        }

        public bool GetOverlayRegisteration()
        {
            return true;
        }

        public String GetETagMatching(String StrUri, String StrETag, ref int nStatusCode)
        {
            String StrRev;
            try
            {
                HttpWebRequest webRequest = null;
                OnGetRequest(ref webRequest, StrUri, "", "", "GET", null);
                webRequest.Headers.Add("If-None-Match", StrETag);

                HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();

                StrRev = response.Headers.Get("eTag");

                nStatusCode = 200;
                webRequest.Abort();
                response.Close();
            }
            catch (WebException wEx)
            {
                nStatusCode = OnException(wEx);
                return "";
            }
            catch
            {
                nStatusCode = -3;
                return "";
            }

            return StrRev;
        }

        public double GetStorageUsed(String strUrl, ref int nStatusCode)
        {
            nStatusCode = 0;
	        double dblStorageSize = 0;
	        try
	        {
		        HttpWebRequest webRequest = null;
                OnGetRequest(ref webRequest, strUrl, "", "1", "Get", null);

		        HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
		        nStatusCode = 200;
		        String StrUsedStorage = OnGetResponseString(response.GetResponseStream());
		        webRequest.Abort();
		        response.Close();

		        if(StrUsedStorage.Substring(0, 6) != "<cloud")
			        return 0;

                XmlDocument m_xmlDocument = new XmlDocument();
		        m_xmlDocument.LoadXml(StrUsedStorage);

		        XmlNode nodeXml = m_xmlDocument.SelectSingleNode("/cloud/account/account-info/storage/used");
		        dblStorageSize = Convert.ToDouble( nodeXml.InnerText );
		        nodeXml.RemoveAll();

		        response.Close();
	        }
	        catch(WebException wEx)
	        {
                nStatusCode = OnException(wEx);
		        return 0;
	        }
	        catch
	        {
		        nStatusCode = -3;
		        return 0;
	        }

	        return dblStorageSize;
        }

        public bool StatusConnection(String strLoginName, String strPassword, String strUrl, ref int nStatusCode)
        {
            nStatusCode = 0;
	        try
	        {
		        HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create( strUrl );
		        webRequest.Credentials = new NetworkCredential(strLoginName, strPassword);
                webRequest.Timeout = nTimeout;

		        HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();

		        nStatusCode = 200;

                webRequest.Abort();
		        response.Close();
	        }
	        catch(WebException wEx)
	        {
                nStatusCode = OnException(wEx);
		        return false;
	        }
	        catch
	        {
		        nStatusCode = -3;
		        return false;
	        }

	        return true;
        }

        public bool Logout()
        {
	        try
	        {
		        m_strLoginName = "";
		        m_strPassword = "";
                m_xmlDocument.RemoveAll();
                m_bStop = false;
	        }
	        catch
	        {
		        return false;
	        }

	        return true;
        }

        public bool DownloadResumeFile(String strSource, String strDestination, long lFrom, long lTo, ref int nStatusCode, CallbackIncrementProgress IncProgress, CallbackContinueRunning ContinueRun)
        {
            nStatusCode = 0;
	        try
	        {
		        HttpWebRequest webRequest = null;
		        //OnGetResumeRequest(ref webRequest, strSource, lFrom, lTo);
                OnGetRequest(ref webRequest, strSource, "", "", "GET", null);
                webRequest.AddRange(lFrom, lTo);

		        HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
		        nStatusCode = 200;
		        if(!OnSaveResponseFile(response.GetResponseStream(), strDestination, lFrom, IncProgress, ContinueRun))
		        {
			        webRequest.Abort();
			        response.Close();
		        }
	        }
	        catch(WebException wEx)
	        {
                nStatusCode = OnException(wEx);
		        return false;
	        }
	        catch
	        {
		        nStatusCode = -3;
		        return false;
	        }

	        return true;
        }

        public bool ExceuteEventViewer(String strName)
        {
	        try
	        {
                if (File.Exists(Environment.SystemDirectory + "\\winevt\\Logs\\" + strName + ".evtx"))
                    Process.Start(Environment.SystemDirectory + "\\winevt\\Logs\\" + strName + ".evtx");
                else
                    Process.Start(Environment.SystemDirectory + "\\eventvwr.msc");
	        }
	        catch
	        {
		        return false;
	        }

	        return true;
        }
        
        public bool AppEventViewer(String StrLogName, String StrLogMsg, int nLevel)
        {
            EventLog eventLog = new EventLog();
	        bool bResult = true;
	        try
	        {
		        if(!EventLog.SourceExists(StrLogName))
		        {
			        EventLog.CreateEventSource(StrLogName, StrLogName);
			        eventLog.Source = StrLogName;
			        eventLog.Log = StrLogName;

			        eventLog.Source = StrLogName;
			        eventLog.WriteEntry("The " + StrLogName + " was successfully initialize component.", EventLogEntryType.Information);
		        }

		        eventLog.Source = StrLogName;
		        switch(nLevel)
		        {
			        case 1:
				        eventLog.WriteEntry(StrLogMsg, EventLogEntryType.Error);
				        break;
			        case 2:
				        eventLog.WriteEntry(StrLogMsg, EventLogEntryType.FailureAudit);
				        break;
			        case 3:
				        eventLog.WriteEntry(StrLogMsg, EventLogEntryType.Information);
				        break;
			        case 4:
				        eventLog.WriteEntry(StrLogMsg, EventLogEntryType.SuccessAudit);
				        break;
			        case 5:
				        eventLog.WriteEntry(StrLogMsg, EventLogEntryType.Warning);
				        break;
			        default:
				        bResult = false;
				        break;
		        }
	        }
	        catch
	        {
		        return false;
	        }
            eventLog.Dispose();
	        eventLog.Close();

            return bResult;	
        }

        public bool CleanEventViewer(String StrLogName)
        {
	        try
	        {
		        EventLog eventLog = new EventLog();
		        eventLog.Log = StrLogName;
		        eventLog.Clear();
		        eventLog.Close();
	        }
	        catch
	        {
		        return false;
	        }
	        return true;
        }

        static public byte[] Encrypt(string StrPadded)
        {
            byte[] bStr;
            try
            {
                string szBase = Convert.ToBase64String(ASCIIEncoding.Unicode.GetBytes(StrPadded));
                char[] cBuffer = szBase.ToCharArray();
                Int32 nConvt = 0;
                bStr = new byte[szBase.Length];
                for (int i = 0; i < szBase.Length; i++)
                {
                    nConvt = Convert.ToInt32(cBuffer[i]);
                    nConvt += i + 22;
                    bStr[i] = Convert.ToByte(nConvt);
                }
            }
            catch
            {
                return null;
            }

            return bStr;
        }

        static public String Decrypt(byte[] bPadded)
        {
            char szBuffer;
            string szBase = "";
            Int32 nConvt = 0;
            for (int i = 0; i < bPadded.Length; i++)
            {
                nConvt = Convert.ToInt32(bPadded[i]);
                nConvt -= i;
                nConvt -= 22;
                szBuffer = Convert.ToChar(nConvt);
                szBase += Convert.ToString(szBuffer);
            }

            byte[] encodedDataAsBytes = Convert.FromBase64String(szBase);
            string returnValue = ASCIIEncoding.Unicode.GetString(encodedDataAsBytes);
            return returnValue;	        
        }

        public String GetMineTypeContainsKey(String StrContainsKey)
        {
            if (types.ContainsKey(StrContainsKey))
                return types[StrContainsKey];

            return "";
        }

        public void AddMineTypeDictionary()
        {
            types = new Dictionary<string, string>();

            types.Add(".3dm", "x-world/x-3dmf");
            types.Add(".3dmf", "x-world/x-3dmf");
            types.Add(".3g2", "video/3g2");
            types.Add(".3gp", "video/3gp");
            types.Add(".aab", "application/x-authorware-bin");
            types.Add(".aam", "application/x-authorware-map");
            types.Add(".aas", "application/x-authorware-seg");
            types.Add(".abc", "text/vnd.abc");
            types.Add(".abs", "video/mpeg");
            types.Add(".acgi", "text/html");
            types.Add(".afl", "video/animaflex");
            types.Add(".ai", "application/postscript");
            types.Add(".aif", "audio/aiff");
            types.Add(".aifc", "audio/aiff");
            types.Add(".aiff", "audio/aiff");
            types.Add(".aim", "application/x-aim");
            types.Add(".aip", "text/x-audiosoft-intra");
            types.Add(".ani", "application/x-navi-animation");
            types.Add(".aos", "application/x-nokia-9000-communicator-add-on-software");
            types.Add(".aps", "application/mime");
            types.Add(".art", "image/x-jg");
            types.Add(".asc", "text/plain");
            types.Add(".asf", "video/x-ms-asf");
            types.Add(".asm", "text/x-asm");
            types.Add(".asp", "text/asp");
            types.Add(".asx", "application/x-mplayer2");
            //types.Add(".au", "audio/x-au");
            types.Add(".au", "audio/basic");
            types.Add(".avi", "video/avi");
            types.Add(".avs", "video/avs-video");
            types.Add(".bat", "text/plain");
            types.Add(".bcpio", "application/x-bcpio");
            types.Add(".bm", "image/bmp");
            types.Add(".bmp", "image/bmp");
            types.Add(".bin", "application/octet-stream");
            types.Add(".boo", "application/book");
            types.Add(".book", "application/book");
            types.Add(".boz", "application/x-bzip2");
            types.Add(".bsh", "application/x-bsh");
            types.Add(".bz", "application/x-bzip");
            types.Add(".bz2", "application/x-bzip2");
            types.Add(".c", "text/plain");
            types.Add(".c++", "text/plain");
            types.Add(".cat", "application/vnd.ms-pki.seccat");
            types.Add(".cc", "text/plain");
            types.Add(".ccad", "application/clariscad");
            types.Add(".cco", "application/x-cocoa");
            //types.Add(".cdf", "application/cdf");
            types.Add(".cdf", "application/x-netcdf");
            types.Add(".cer", "application/pkix-cert");
            types.Add(".cgm", "image/cgm");
            types.Add(".cha", "application/x-chat");
            types.Add(".chat", "application/x-chat");
            types.Add(".chh", "text/plain");
            //types.Add(".class", "application/java");
            types.Add(".class", "application/octet-stream");
            types.Add(".cmd", "text/plain");
            types.Add(".conf", "text/plain");
            types.Add(".cpio", "application/x-cpio");
            types.Add(".cpp", "text/plain");
            //types.Add(".cpt", "application/x-cpt");
            types.Add(".cpt", "application/mac-compactpro");
            types.Add(".crl", "application/pkix-crl");
            types.Add(".crt", "application/pkix-cert");
            types.Add(".csh", "application/x-csh");
            types.Add(".css", "text/css");
            types.Add(".csv", "text/plain");
            types.Add(".cxx", "text/plain");
            types.Add(".dcr", "application/x-director");
            types.Add(".deepv", "application/x-deepv");
            types.Add(".def", "text/plain");
            types.Add(".der", "application/x-x509-ca-cert");
            types.Add(".dif", "video/x-dv");
            types.Add(".dir", "application/x-director");
            types.Add(".divx", "video/x-divx");
            types.Add(".djv", "image/vnd.djvu");
            types.Add(".djvu", "image/vnd.djvu");
            types.Add(".dl", "video/dl");
            types.Add(".dll", "application/octet-stream");
            types.Add(".dmg", "application/octet-stream");
            types.Add(".dms", "application/octet-stream");
            types.Add(".doc", "application/msword");
            types.Add(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
            types.Add(".dot", "application/msword");
            types.Add(".dp", "application/commonground");
            types.Add(".drw", "application/drafting");
            types.Add(".dtd", "application/xml-dtd");
            types.Add(".dv", "video/x-dv");
            types.Add(".dvi", "application/x-dvi");
            types.Add(".dwf", "drawing/x-dwf (old)");
            types.Add(".dwg", "application/acad");
            types.Add(".dxf", "application/dxf");
            types.Add(".dxr", "application/x-director");
            types.Add(".el", "text/x-script.elisp");
            types.Add(".elc", "application/x-elc");
            types.Add(".eps", "application/postscript");
            types.Add(".es", "application/x-esrehber");
            types.Add(".etx", "text/x-setext");
            types.Add(".evy", "application/envoy");
            types.Add(".exe", "application/octet-stream");
            types.Add(".ez", "application/andrew-inset");
            types.Add(".f", "text/plain");
            types.Add(".f77", "text/plain");
            types.Add(".f90", "text/plain");
            types.Add(".fdf", "application/vnd.fdf");
            types.Add(".fif", "image/fif");
            types.Add(".fli", "video/fli");
            types.Add(".flo", "image/florian");
            types.Add(".flv", "video/x-flv");
            types.Add(".flx", "text/vnd.fmi.flexstor");
            types.Add(".fmf", "video/x-atomic3d-feature");
            types.Add(".for", "text/plain");
            types.Add(".fpx", "image/vnd.fpx");
            types.Add(".frl", "application/freeloader");
            types.Add(".funk", "audio/make");
            types.Add(".g", "text/plain");
            types.Add(".g3", "image/g3fax");
            types.Add(".gif", "image/gif");
            types.Add(".gl", "video/gl");
            types.Add(".grxml", "application/srgs+xml");
            types.Add(".gram", "application/srgs");
            types.Add(".gsd", "audio/x-gsm");
            types.Add(".gsm", "audio/x-gsm");
            types.Add(".gsp", "application/x-gsp");
            types.Add(".gss", "application/x-gss");
            types.Add(".gtar", "application/x-gtar");
            types.Add(".gz", "application/x-gzip");
            types.Add(".gzip", "application/x-gzip");
            types.Add(".h", "text/plain");
            types.Add(".hdf", "application/x-hdf");
            types.Add(".help", "application/x-helpfile");
            types.Add(".hgl", "application/vnd.hp-HPGL");
            types.Add(".hh", "text/plain");
            types.Add(".hlb", "text/x-script");
            types.Add(".hlp", "application/x-helpfile");
            types.Add(".hpg", "application/vnd.hp-HPGL");
            types.Add(".hpgl", "application/vnd.hp-HPGL");
            //types.Add(".hqx", "application/binhex");
            types.Add(".hqx", "application/mac-binhex40");
            types.Add(".hta", "application/hta");
            types.Add(".htc", "text/x-component");
            types.Add(".htm", "text/html");
            types.Add(".html", "text/html");
            types.Add(".htmls", "text/html");
            types.Add(".htt", "text/webviewhtml");
            types.Add(".htx", "text/html");
            types.Add(".ice", "x-conference/x-cooltalk");
            types.Add(".ico", "image/x-icon");
            types.Add(".ics", "text/calendar");
            types.Add(".idc", "text/plain");
            types.Add(".ief", "image/ief");
            types.Add(".iefs", "image/ief");
            types.Add(".ifb", "text/calendar");
            types.Add(".iges", "application/iges");
            types.Add(".igs", "application/iges");
            types.Add(".ima", "application/x-ima");
            types.Add(".imap", "application/x-httpd-imap");
            types.Add(".inf", "application/inf");
            types.Add(".ini", "text/plain");
            types.Add(".ins", "application/x-internett-signup");
            types.Add(".ip", "application/x-ip2");
            types.Add(".isu", "video/x-isvideo");
            types.Add(".it", "audio/it");
            types.Add(".iv", "application/x-inventor");
            types.Add(".ivr", "i-world/i-vrml");
            types.Add(".ivy", "application/x-livescreen");
            types.Add(".jam", "audio/x-jam");
            types.Add(".jar", "application/java-archive");
            types.Add(".jav", "text/plain");
            types.Add(".java", "text/plain");
            types.Add(".jcm", "application/x-java-commerce");
            types.Add(".jfif", "image/jpeg");
            types.Add(".jfif-tbnl", "image/jpeg");
            types.Add(".jnlp", "application/jnlp");
            types.Add(".jpe", "image/jpeg");
            types.Add(".jpeg", "image/jpeg");
            types.Add(".jpg", "image/jpeg");
            types.Add(".jps", "image/x-jps");
            types.Add(".js", "application/x-javascript");
            types.Add(".json", "application/json");
            types.Add(".jut", "image/jutvision");
            types.Add(".kar", "audio/midi");
            types.Add(".ksh", "text/x-script.ksh");
            types.Add(".la", "audio/nspaudio");
            types.Add(".lam", "audio/x-liveaudio");
            types.Add(".latex", "application/x-latex");
            types.Add(".lha", "application/octet-stream");
            types.Add(".list", "text/plain");
            types.Add(".lma", "audio/nspaudio");
            types.Add(".log", "text/plain");
            types.Add(".lsp", "application/x-lisp");
            types.Add(".lst", "text/plain");
            types.Add(".lsx", "text/x-la-asf");
            types.Add(".ltx", "application/x-latex");
            types.Add(".lzh", "application/octet-stream");
            types.Add(".m", "text/plain");
            types.Add(".m1v", "video/mpeg");
            types.Add(".m2a", "audio/mpeg");
            //types.Add(".m2v", "video/mpeg");
            types.Add(".m2v", "video/m2v");
            types.Add(".m3u", "audio/x-mpequrl");
            //types.Add(".m4a", "audio/mp4");
            types.Add(".m4a", "audio/mp4a-latm");
            types.Add(".m4u", "video/vnd.mpegurl");
            types.Add(".m4v", "video/mp4");
            types.Add(".man", "application/x-troff-man");
            types.Add(".map", "application/x-navimap");
            types.Add(".mar", "text/plain");
            types.Add(".mathml", "application/mathml+xml");
            types.Add(".mbd", "application/mbedlet");
            types.Add(".mc$", "application/x-magic-cap-package-1.0");
            types.Add(".mcd", "application/mcad");
            types.Add(".mcf", "image/vasa");
            types.Add(".mcp", "application/netmc");
            types.Add(".me", "application/x-troff-me");
            types.Add(".mesh", "model/mesh");
            types.Add(".mht", "message/rfc822");
            types.Add(".mhtml", "message/rfc822");
            types.Add(".mid", "audio/midi");
            types.Add(".midi", "audio/midi");
            //types.Add(".mif", "application/x-mif");
            types.Add(".mif", "application/vnd.mif");
            types.Add(".mime", "message/rfc822");
            types.Add(".mjf", "audio/x-vnd.AudioExplosion.MjuiceMediaFile");
            types.Add(".mjpg", "video/x-motion-jpeg");
            types.Add(".mm", "application/base64");
            types.Add(".mme", "application/base64");
            types.Add(".mod", "audio/mod");
            types.Add(".moov", "video/quicktime");
            types.Add(".mov", "video/quicktime");
            types.Add(".movie", "video/x-sgi-movie");
            types.Add(".mp1", "audio/mpeg");
            types.Add(".mp2", "video/mpeg");
            types.Add(".mp3", "audio/mpeg3");
            types.Add(".mp4", "video/mp4");
            types.Add(".mpa", "audio/mpeg");
            types.Add(".mpc", "application/x-project");
            types.Add(".mpe", "video/mpeg");
            types.Add(".mpeg", "video/mpeg");
            types.Add(".mpg", "video/mpeg");
            types.Add(".mpga", "audio/mpeg");
            types.Add(".mpp", "application/vnd.ms-project");
            types.Add(".mpt", "application/x-project");
            types.Add(".mpv", "application/x-project");
            types.Add(".mpx", "application/x-project");
            types.Add(".mrc", "application/marc");
            types.Add(".ms", "application/x-troff-ms");
            types.Add(".msh", "model/mesh");
            types.Add(".mv", "video/x-sgi-movie");
            types.Add(".mxu", "video/vnd.mpegurl");
            types.Add(".my", "audio/make");
            types.Add(".mzz", "application/x-vnd.AudioExplosion.mzz");
            types.Add(".nap", "image/naplps");
            types.Add(".naplps", "image/naplps");
            types.Add(".nc", "application/x-netcdf");
            types.Add(".ncm", "application/vnd.nokia.configuration-message");
            types.Add(".nif", "image/x-niff");
            types.Add(".niff", "image/x-niff");
            types.Add(".nix", "application/x-mix-transfer");
            types.Add(".nsc", "application/x-conference");
            types.Add(".nvd", "application/x-navidoc");
            types.Add(".oda", "application/oda");
            types.Add(".odg", "application/vnd.oasis.opendocument.graphics");
            types.Add(".ods", "application/vnd.oasis.opendocument.spreadsheet");
            types.Add(".odt", "application/vnd.oasis.opendocument.text");
            types.Add(".odp", "application/vnd.oasis.opendocument.presentation");
            types.Add(".ogg", "application/x-ogg");
            types.Add(".omc", "application/x-omc");
            types.Add(".omcd", "application/x-omcdatamaker");
            types.Add(".omcr", "application/x-omcregerator");
            types.Add(".p", "text/x-pascal");
            types.Add(".p10", "application/pkcs10");
            types.Add(".p12", "application/pkcs-12");
            types.Add(".p7a", "application/x-pkcs7-signature");
            types.Add(".p7c", "application/pkcs7-mime");
            types.Add(".p7m", "application/pkcs7-mime");
            types.Add(".p7r", "application/x-pkcs7-certreqresp");
            types.Add(".p7s", "application/pkcs7-signature");
            types.Add(".part", "application/pro_eng");
            types.Add(".pas", "text/pascal");
            types.Add(".pbm", "image/x-portable-bitmap");
            types.Add(".pcl", "application/x-pcl");
            types.Add(".pct", "image/x-pict");
            types.Add(".pcx", "image/x-pcx");
            types.Add(".pdb", "chemical/x-pdb");
            types.Add(".pdf", "application/pdf");
            types.Add(".pfunk", "audio/make");
            types.Add(".pgm", "image/x-portable-graymap");
            types.Add(".pgn", "application/x-chess-pgn");
            types.Add(".pic", "image/pict");
            types.Add(".pict", "image/pict");
            types.Add(".pkg", "application/x-newton-compatible-pkg");
            types.Add(".pko", "application/vnd.ms-pki.pko");
            types.Add(".pl", "text/plain");
            types.Add(".plx", "application/x-PiXCLscript");
            types.Add(".pm", "image/x-xpixmap");
            types.Add(".pm4", "application/x-pagemaker");
            types.Add(".pm5", "application/x-pagemaker");
            types.Add(".png", "image/png");
            types.Add(".pnm", "application/x-portable-anymap");
            types.Add(".pot", "application/mspowerpoint");
            types.Add(".pov", "model/x-pov");
            types.Add(".ppa", "application/vnd.ms-powerpoint");
            types.Add(".ppm", "image/x-portable-pixmap");
            //types.Add(".pps", "application/mspowerpoint");
            //types.Add(".ppt", "application/mspowerpoint");
            types.Add(".ppt", "application/vnd.ms-powerpoint");
            types.Add(".pps", "application/vnd.ms-powerpoint");
            types.Add(".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation");
            types.Add(".ppsx", "application/vnd.openxmlformats-officedocument.presentationml.slideshow");
            types.Add(".ppz", "application/mspowerpoint");
            types.Add(".pre", "application/x-freelance");
            types.Add(".prt", "application/pro_eng");
            types.Add(".ps", "application/postscript");
            types.Add(".psd", "image/photoshop");
            types.Add(".pvu", "paleovu/x-pv");
            types.Add(".pwz", "application/vnd.ms-powerpoint");
            types.Add(".py", "text/x-script.phyton");
            types.Add(".pyc", "applicaiton/x-bytecode.python");
            types.Add(".qcp", "audio/vnd.qcelp");
            types.Add(".qd3", "x-world/x-3dmf");
            types.Add(".qd3d", "x-world/x-3dmf");
            types.Add(".qif", "image/x-quicktime");
            types.Add(".qt", "video/quicktime");
            types.Add(".qtc", "video/x-qtc");
            types.Add(".qti", "image/x-quicktime");
            types.Add(".qtif", "image/x-quicktime");
            types.Add(".ra", "audio/x-pn-realaudio");
            types.Add(".ram", "audio/x-pn-realaudio");
            types.Add(".rar", "application/x-rar-compressed");
            types.Add(".ras", "application/x-cmu-raster");
            types.Add(".rast", "image/cmu-raster");
            types.Add(".rdf", "application/rdf+xml");
            types.Add(".rexx", "text/x-script.rexx");
            types.Add(".rf", "image/vnd.rn-realflash");
            types.Add(".rgb", "image/x-rgb");
            types.Add(".rm", "application/vnd.rn-realmedia");
            types.Add(".rmi", "audio/mid");
            types.Add(".rmm", "audio/x-pn-realaudio");
            types.Add(".rmp", "audio/x-pn-realaudio");
            types.Add(".rng", "application/ringing-tones");
            types.Add(".rnx", "application/vnd.rn-realplayer");
            types.Add(".roff", "application/x-troff");
            types.Add(".rp", "image/vnd.rn-realpix");
            types.Add(".rpm", "audio/x-pn-realaudio-plugin");
            types.Add(".rss", "text/xml");
            types.Add(".rt", "text/richtext");
            //types.Add(".rtf", "text/richtext");
            types.Add(".rtf", "text/rtf");
            types.Add(".rtx", "text/richtext");
            types.Add(".rv", "video/vnd.rn-realvideo");
            types.Add(".s", "text/x-asm");
            types.Add(".s3m", "audio/s3m");
            types.Add(".sbk", "application/x-tbook");
            types.Add(".scm", "application/x-lotusscreencam");
            types.Add(".sdml", "text/plain");
            types.Add(".sdp", "application/sdp");
            types.Add(".sdr", "application/sounder");
            types.Add(".sea", "application/sea");
            types.Add(".set", "application/set");
            types.Add(".sgm", "text/sgml");
            types.Add(".sgml", "text/sgml");
            //types.Add(".sh", "text/x-script.sh");
            types.Add(".sh", "application/x-sh");
            //types.Add(".shar", "application/x-bsh");
            types.Add(".shar", "application/x-shar");
            types.Add(".shtml", "text/html");
            types.Add(".sid", "audio/x-psid");
            types.Add(".silo", "model/mesh");
            //types.Add(".sit", "application/x-sit");
            types.Add(".sit", "application/x-stuffit");
            types.Add(".skd", "application/x-koan");
            types.Add(".skm", "application/x-koan");
            types.Add(".skp", "application/x-koan");
            types.Add(".skt", "application/x-koan");
            types.Add(".sl", "application/x-seelogo");
            types.Add(".smi", "application/smil");
            types.Add(".smil", "application/smil");
            types.Add(".snd", "audio/basic");
            types.Add(".so", "application/octet-stream");
            types.Add(".sol", "application/solids");
            types.Add(".spc", "application/x-pkcs7-certificates");
            //types.Add(".spl", "application/futuresplash");
            types.Add(".spl", "application/x-futuresplash");
            types.Add(".spr", "application/x-sprite");
            types.Add(".sprite", "application/x-sprite");
            types.Add(".src", "application/x-wais-source");
            types.Add(".ssi", "text/x-server-parsed-html");
            types.Add(".ssm", "application/streamingmedia");
            types.Add(".sst", "application/vnd.ms-pki.certstore");
            types.Add(".step", "application/step");
            types.Add(".stl", "application/sla");
            types.Add(".stp", "application/step");
            types.Add(".sv4cpio", "application/x-sv4cpio");
            types.Add(".sv4crc", "application/x-sv4crc");
            types.Add(".svf", "image/x-dwg");
            types.Add(".svg", "image/svg+xml");
            types.Add(".svr", "application/x-world");
            types.Add(".swf", "application/x-shockwave-flash");
            types.Add(".t", "application/x-troff");
            types.Add(".talk", "text/x-speech");
            types.Add(".tar", "application/x-tar");
            types.Add(".tbk", "application/toolbook");
            //types.Add(".tcl", "text/x-script.tcl");
            types.Add(".tcl", "application/x-tcl");
            types.Add(".tcsh", "text/x-script.tcsh");
            types.Add(".tex", "application/x-tex");
            types.Add(".texi", "application/x-texinfo");
            types.Add(".texinfo", "application/x-texinfo");
            types.Add(".text", "text/plain");
            //types.Add(".tgz", "application/x-compressed");
            types.Add(".tgz", "application/tgz");
            types.Add(".tif", "image/tiff");
            types.Add(".tiff", "image/tiff");
            types.Add(".tr", "application/x-troff");
            types.Add(".tsi", "audio/tsp-audio");
            types.Add(".tsp", "audio/tsplayer");
            types.Add(".tsv", "text/tab-separated-values");
            types.Add(".turbot", "image/florian");
            types.Add(".txt", "text/plain");
            types.Add(".uil", "text/x-uil");
            types.Add(".uni", "text/uri-list");
            types.Add(".unis", "text/uri-list");
            types.Add(".unv", "application/i-deas");
            types.Add(".uri", "text/uri-list");
            types.Add(".uris", "text/uri-list");
            //types.Add(".ustar", "multipart/x-ustar");
            types.Add(".ustar", "application/x-ustar");
            types.Add(".uu", "text/x-uuencode");
            types.Add(".uue", "text/x-uuencode");
            types.Add(".vcd", "application/x-cdlink");
            types.Add(".vcf", "application/x-vcard");
            types.Add(".vcs", "text/x-vCalendar");
            types.Add(".vda", "application/vda");
            types.Add(".vdo", "video/vdo");
            types.Add(".vew", "application/groupwise");
            types.Add(".viv", "video/vivo");
            types.Add(".vivo", "video/vivo");
            types.Add(".vmd", "application/vocaltec-media-desc");
            types.Add(".vmf", "application/vocaltec-media-file");
            types.Add(".voc", "audio/voc");
            types.Add(".vos", "video/vosaic");
            types.Add(".vox", "audio/voxware");
            types.Add(".vqe", "audio/x-twinvq-plugin");
            types.Add(".vqf", "audio/x-twinvq");
            types.Add(".vql", "audio/x-twinvq-plugin");
            types.Add(".vrml", "application/x-vrml");
            types.Add(".vrt", "x-world/x-vrt");
            types.Add(".vsd", "application/x-visio");
            types.Add(".vst", "application/x-visio");
            types.Add(".vsw", "application/x-visio");
            types.Add(".w60", "application/wordperfect6.0");
            types.Add(".w61", "application/wordperfect6.1");
            types.Add(".w6w", "application/msword");
            //types.Add(".wav", "audio/wav");
            types.Add(".wav", "audio/x-wav");
            types.Add(".wb1", "application/x-qpro");
            types.Add(".wbmp", "image/vnd.wap.wbmp");
            types.Add(".web", "application/vnd.xara");
            types.Add(".wiz", "application/msword");
            types.Add(".wk1", "application/x-123");
            types.Add(".wma", "audio/x-ms-wma");
            types.Add(".wmf", "windows/metafile");
            types.Add(".wml", "text/vnd.wap.wml");
            types.Add(".wmlc", "application/vnd.wap.wmlc");
            types.Add(".wmls", "text/vnd.wap.wmlscript");
            types.Add(".wmlsc", "application/vnd.wap.wmlscriptc");
            types.Add(".wmv", "video/x-ms-wmv");
            types.Add(".word", "application/msword");
            types.Add(".wp", "application/wordperfect");
            types.Add(".wp5", "application/wordperfect");
            types.Add(".wp6", "application/wordperfect");
            types.Add(".wpd", "application/wordperfect");
            types.Add(".wq1", "application/x-lotus");
            types.Add(".wri", "application/mswrite");
            types.Add(".wrl", "application/x-world");
            types.Add(".wrz", "model/vrml");
            types.Add(".wsc", "text/scriplet");
            types.Add(".wsrc", "application/x-wais-source");
            types.Add(".wtk", "application/x-wintalk");
            types.Add(".xbm", "image/x-xbitmap");
            types.Add(".xdr", "video/x-amt-demorun");
            types.Add(".xgz", "xgl/drawing");
            types.Add(".xht", "application/xhtml+xml");
            types.Add(".xhtml", "application/xhtml+xml");
            types.Add(".xif", "image/vnd.dxiff");
            types.Add(".xl", "application/excel");
            types.Add(".xla", "application/excel");
            types.Add(".xlb", "application/excel");
            types.Add(".xlc", "application/excel");
            types.Add(".xld", "application/excel");
            types.Add(".xlk", "application/excel");
            types.Add(".xll", "application/excel");
            types.Add(".xlm", "application/excel");
            //types.Add(".xls", "application/excel");
            types.Add(".xls", "application/vnd.ms-excel");
            types.Add(".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            types.Add(".xlt", "application/excel");
            types.Add(".xlv", "application/excel");
            types.Add(".xlw", "application/excel");
            types.Add(".xm", "audio/xm");
            types.Add(".xml", "application/xml");
            types.Add(".xmz", "xgl/movie");
            types.Add(".xpix", "application/x-vnd.ls-xpix");
            types.Add(".xpm", "image/xpm");
            types.Add(".x-png", "image/png");
            types.Add(".xsl", "application/xml");
            types.Add(".xslt", "application/xslt+xml");
            types.Add(".xsr", "video/x-amt-showrun");
            types.Add(".xul", "application/vnd.mozilla.xul+xml");
            types.Add(".xwd", "image/x-xwd");
            types.Add(".xyz", "chemical/x-pdb");
            types.Add(".z", "application/x-compressed");
            types.Add(".zip", "application/zip");
            types.Add(".zsh", "text/x-script.zsh");
        }
    }
}
