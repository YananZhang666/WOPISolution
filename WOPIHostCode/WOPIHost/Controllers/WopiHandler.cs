﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Web;
using WOPIHost.Utils;

namespace WOPIHost.Controllers
{
    /// <summary>
    /// This class implements a simple WOPI handler that allows users to read and write files
    /// stored on the local filesystem.
    /// </summary>
    /// <remarks>
    /// The implementation of these WOPI methods is for illustrative purposes only.
    /// A real WOPI system would verify user permissions, store locks in a persistent way, etc.
    /// </remarks>
    public class WopiHandler : IHttpHandler
    {
        private const string WopiPath = @"/wopi/";
        private const string FilesRequestPath = @"files/";
        private const string FoldersRequestPath = @"folders/";
        private const string ContentsRequestPath = @"/contents";
        private const string ChildrenRequestPath = @"/children";
        private const string uid = "TestUser";
        public static readonly string LocalStoragePath = ConfigurationManager.AppSettings["FileLocalPath"];

        private class LockInfo
        {
            public string Lock { get; set; }
            public DateTime DateCreated { get; set; }
            public bool Expired { get { return this.DateCreated.AddMinutes(30) < DateTime.UtcNow; } }
        }

        /// <summary>
        /// Simplified Lock info storage.
        /// A real lock implementation would use persised storage for locks.
        /// </summary>
        private static readonly Dictionary<string, LockInfo> Locks = new Dictionary<string, LockInfo>();
        private static readonly List<string> WithoutRestrictedLinks = new List<string>();

        #region IHttpHandler Members

        public bool IsReusable
        {
            // Return false in case your Managed Handler cannot be reused for another request.
            // This would be false if you have some state information preserved per request.
            get { return true; }
        }

        /// <summary>
        /// Begins processing the incoming WOPI request.
        /// </summary>
        public void ProcessRequest(HttpContext context)
        {
            // WOPI ProofKey validation is an optional way that a WOPI host can ensure that the request
            // is coming from the Office Online server that they expect to be talking to.
            if (!ValidateWopiProofKey(context.Request))
            {
                ReturnServerError(context.Response);
            }

            // Parse the incoming WOPI request
            WopiRequest requestData = ParseRequest(context.Request);

            // Call the appropriate handler for the WOPI request we received
            switch (requestData.Type)
            {
                case RequestType.AddActivities:
                    HandleAddActivitiesRequest(context, requestData);
                    break;

                case RequestType.CheckFileInfo:
                    HandleCheckFileInfoRequest(context, requestData);
                    break;

                case RequestType.Lock:
                    HandleLockRequest(context, requestData);
                    break;

                case RequestType.Unlock:
                    HandleUnlockRequest(context, requestData);
                    break;

                case RequestType.RefreshLock:
                    HandleRefreshLockRequest(context, requestData);
                    break;

                case RequestType.UnlockAndRelock:
                    HandleUnlockAndRelockRequest(context, requestData);
                    break;

                case RequestType.GetFile:
                    HandleGetFileRequest(context, requestData);
                    break;

                case RequestType.PutFile:
                    HandlePutFileRequest(context, requestData);
                    break;
                case RequestType.GetRestrictedLink:
                    HandleGetRestrictedLink(context, requestData);
                    break;
                case RequestType.ReadSecureStore:
                    HandleReadSecureStore(context, requestData);
                    break;
                case RequestType.CheckFolderInfo:
                    HandleCheckFolderInfo(context, requestData);
                    break;
                case RequestType.RevokeRestrictedLink:
                    HandleRevokeRestrictedLink(context, requestData);
                    break;
                case RequestType.EnumerateAncestors:
                    HandleEnumerateAncestorsForFile(context, requestData);
                    break;
                case RequestType.DeleteFile:
                    HandleDeleteFileRequest(context, requestData);
                    break;
                case RequestType.PutRelativeFile:
                    HandlePutRelativeFileRequest(context, requestData);
                    break;
                case RequestType.RenameFile:
                    HandleRenameFileRequest(context, requestData);
                    break;
                case RequestType.GetLock:
                    HandleGetLockRequest(context, requestData);
                    break;
                case RequestType.GetShareUrl:
                    HandleGetShareUrlRequest(context, requestData);
                    break;
                case RequestType.PutUserInfo:
                    HandlePutUserInfoRequest(context, requestData);
                    break;
                case RequestType.EnumerateChildren:
                    HandleEnumerateChildren(context, requestData);
                    break;

                // These request types are not implemented in this sample.
                // Of these, only PutRelativeFile would be implemented by a typical WOPI host.
                case RequestType.ExecuteCobaltRequest:
                    ReturnUnsupported(context.Response);
                    break;

                default:
                    ReturnServerError(context.Response);
                    break;
            }
        }

        #endregion

        /// <summary>
        /// Parse the request determine the request type, access token, and file id.
        /// For more details, see the [MS-WOPI] Web Application Open Platform Interface Protocol specification.
        /// </summary>
        /// <remarks>
        /// Can be extended to parse client version, machine name, etc.
        /// </remarks>
        private static WopiRequest ParseRequest(HttpRequest request)
        {
            // Initilize wopi request data object with default values
            WopiRequest requestData = new WopiRequest()
            {
                Type = RequestType.None,
                AccessToken = request.QueryString["access_token"],
                Id = "",
                Headers = request.Headers
            };

            // request.Url pattern:
            // http(s)://server/<...>/wopi/[files|folders]/<id>?access_token=<token>
            // or
            // https(s)://server/<...>/wopi/files/<id>/contents?access_token=<token>
            // or
            // https(s)://server/<...>/wopi/folders/<id>/children?access_token=<token>

            // Get request path, e.g. /<...>/wopi/files/<id>
            string requestPath = request.Url.AbsolutePath;
            // remove /<...>/wopi/
            string wopiPath = requestPath.Substring(WopiPath.Length);

            if (wopiPath.StartsWith(FilesRequestPath))
            {
                // A file-related request

                // remove /files/ from the beginning of wopiPath
                string rawId = wopiPath.Substring(FilesRequestPath.Length);
                rawId = HttpUtility.UrlDecode(rawId);

                if (rawId.EndsWith(ContentsRequestPath))
                {
                    // The rawId ends with /contents so this is a request to read/write the file contents

                    // Remove /contents from the end of rawId to get the actual file id
                    requestData.Id = rawId.Substring(0, rawId.Length - ContentsRequestPath.Length).ToLower();

                    if (request.HttpMethod == "GET")
                        requestData.Type = RequestType.GetFile;
                    if (request.HttpMethod == "POST")
                        requestData.Type = RequestType.PutFile;
                }
                else if (rawId.EndsWith("/ancestry"))
                {
                    requestData.Id = rawId.Substring(0, rawId.Length - "/ancestry".Length).ToLower();

                    if (request.HttpMethod == "GET")
                        requestData.Type = RequestType.EnumerateAncestors;
                }
                else
                {
                    requestData.Id = rawId.ToLower();

                    if (request.HttpMethod == "GET")
                    {
                        // a GET to the file is always a CheckFileInfo request
                        requestData.Type = RequestType.CheckFileInfo;
                    }
                    else if (request.HttpMethod == "POST")
                    {
                        // For a POST to the file we need to use the X-WOPI-Override header to determine the request type
                        string wopiOverride = request.Headers[WopiHeaders.RequestType];

                        switch (wopiOverride)
                        {
                            case "ADD_ACTIVITIES":
                                requestData.Type = RequestType.AddActivities;
                                break;

                            case "PUT_RELATIVE":
                                requestData.Type = RequestType.PutRelativeFile;
                                break;
                            case "LOCK":
                                // A lock could be either a lock or an unlock and relock, determined based on whether
                                // the request sends an OldLock header.
                                if (request.Headers[WopiHeaders.OldLock] != null)
                                    requestData.Type = RequestType.UnlockAndRelock;
                                else
                                    requestData.Type = RequestType.Lock;
                                break;
                            case "UNLOCK":
                                requestData.Type = RequestType.Unlock;
                                break;
                            case "REFRESH_LOCK":
                                requestData.Type = RequestType.RefreshLock;
                                break;
                            case "GET_LOCK":
                                requestData.Type = RequestType.GetLock;
                                break;
                            case "COBALT":
                                requestData.Type = RequestType.ExecuteCobaltRequest;
                                break;
                            case "DELETE":
                                requestData.Type = RequestType.DeleteFile;
                                break;
                            case "READ_SECURE_STORE":
                                requestData.Type = RequestType.ReadSecureStore;
                                break;
                            case "GET_RESTRICTED_LINK":
                                requestData.Type = RequestType.GetRestrictedLink;
                                break;
                            case "REVOKE_RESTRICTED_LINK":
                                requestData.Type = RequestType.RevokeRestrictedLink;
                                break;
                            case "RENAME_FILE":
                                requestData.Type = RequestType.RenameFile;
                                break;
                            case "GET_SHARE_URL":
                                requestData.Type = RequestType.GetShareUrl;
                                break;
                            case "PUT_USER_INFO":
                                requestData.Type = RequestType.PutUserInfo;
                                break;

                        }
                    }
                }
            }
            else if (wopiPath.StartsWith(FoldersRequestPath))
            {
                // A folder-related request.

                // remove /folders/ from the beginning of wopiPath
                string rawId = wopiPath.Substring(FoldersRequestPath.Length);

                if (rawId.EndsWith(ChildrenRequestPath))
                {
                    // rawId ends with /children, so it's an EnumerateChildren request.

                    // remove /children from the end of rawId
                    requestData.Id = rawId.Substring(0, rawId.Length - ChildrenRequestPath.Length);
                    requestData.Type = RequestType.EnumerateChildren;
                }
                else
                {
                    // rawId doesn't end with /children, so it's a CheckFolderInfo.

                    requestData.Id = rawId;
                    requestData.Type = RequestType.CheckFolderInfo;
                }
            }
            else
            {
                // An unknown request.
                requestData.Type = RequestType.None;
            }

            return requestData;
        }

        #region Processing for each of the WOPI operations

        /// <summary>
        /// Processes a AddActivities request
        /// </summary>

        public class AddActivitiesRequest
        {
            public List<Activity> Activities { get; set; }
        }

        public class Activity
        {
            public string Type { get; set; }
            public string Id { get; set; }
            public string Timestamp { get; set; }
            public ActivityData Data { get; set; }
        }

        public class ActivityData
        {
            public string ContentId { get; set; }
            public string ContentAction { get; set; }
        }



        private void HandleAddActivitiesRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: false))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            long size = storage.GetFileSize(requestData.Id);

            if (size == -1)
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            using (StreamReader reader = new StreamReader(context.Request.InputStream))
            {
                string requestBody = reader.ReadToEnd();
                var activitiesRequest = JsonConvert.DeserializeObject<AddActivitiesRequest>(requestBody);

                var activityResponses = new List<ActivityResponse>();

                foreach (var activity in activitiesRequest.Activities)
                {
                    var response = new ActivityResponse
                    {
                        Id = activity.Id,
                        Status = 0, 
                        Message = ""
                    };
                    activityResponses.Add(response);
                }
            
                AddActivitiesResponse responseData = new AddActivitiesResponse
                {
                    ActivityResponses = activityResponses
                };

                string jsonString = JsonConvert.SerializeObject(responseData);
                context.Response.ContentType = "application/json";
                context.Response.Write(jsonString);
                ReturnSuccess(context.Response);
            }
        }

        /// <summary>
        /// Processes a CheckFileInfo request
        /// </summary>
        /// <remarks>
        /// For full documentation on CheckFileInfo, see
        /// https://wopi.readthedocs.io/projects/wopirest/en/latest/files/CheckFileInfo.html
        /// </remarks>
        private void HandleCheckFileInfoRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: false))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            long size = storage.GetFileSize(requestData.Id);

            if (size == -1)
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            bool bRO = storage.GetReadOnlyStatus(requestData.Id);
            try
            {
                CheckFileInfoResponse responseData = new CheckFileInfoResponse()
                {
                    // required CheckFileInfo properties
                    BaseFileName = Path.GetFileName(requestData.Id),
                    OfficeCollaborationServiceEndpointUrl = "http://" + context.Request.Url.Host,
                    OpenInClientCommandUrl = "http://" + context.Request.Url.Host,
                    OpenInClientPostMessage = false,
                    OwnerId = "documentOwnerId",
                    Size = Convert.ToInt32(size),
                    Version = storage.GetFileVersion(requestData.Id),
                    UserId = "WOPITestUser",
                    UserPrincipalName = "A WOPI User",
                    FileExtension = Path.GetExtension(requestData.Id),

                    // optional CheckFileInfo properties
                    BreadcrumbBrandName = "LocalStorage WOPI Host",
                    //BreadcrumbFolderName = fileInfo.Directory != null ? fileInfo.Directory.Name : "",
                    BreadcrumbFolderName = "",
                    BreadcrumbDocName = Path.GetFileNameWithoutExtension(requestData.Id),
                    BreadcrumbBrandUrl = "http://" + context.Request.Url.Host,
                    BreadcrumbFolderUrl = "http://" + context.Request.Url.Host,

                    UserFriendlyName = "A WOPI User",
                    SupportsAddActivities = true,
                    SupportsLocks = true,
                    SupportsUpdate = true,
                    UserCanNotWriteRelative = false,
                    SupportsScenarioLinks = true,
                    SupportsSecureStore = true,
                    SupportsFolders = true,
                    SupportsRename = true,
                    UserCanRename = true,
                    SupportsGetLock = true,
                    SupportsUserInfo = true,
                    //SupportsDeleteFile = true,
                    SupportsExtendedLockLength = true,
                    ReadOnly = bRO,
                    UserCanWrite = !bRO,

                    SupportedShareUrlTypes = new string[] { "ReadOnly", "ReadWrite" },
                    UserInfo = string.Empty
                };

                string userName = AccessTokenUtil.GetUserFromToken(requestData.AccessToken);
                if (UserInfo.ContainsKey(userName))
                {
                    responseData.UserInfo = UserInfo[userName];
                }

                string jsonString = JsonConvert.SerializeObject(responseData);

                context.Response.Write(jsonString);
                ReturnSuccess(context.Response);
            }
            catch (UnauthorizedAccessException)
            {
                ReturnFileUnknown(context.Response);
            }
        }

        /// <summary>
        /// Processes a DeleteFile request
        /// </summary>
        /// <remarks>
        /// For full documentation on DeleteFile, see
        /// https://wopi.readthedocs.io/projects/wopirest/en/latest/files/DeleteFile.html
        /// </remarks>
        private void HandleDeleteFileRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: false))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            long size = storage.GetFileSize(requestData.Id);

            if (size == -1)
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            LockInfo existingLock;
            bool fLocked = TryGetLock(requestData.Id, out existingLock);
            if (fLocked)
            {
                context.Response.AddHeader("X-WOPI-Lock", existingLock.Lock);
                ReturnStatus(context.Response, 409, "Locked by another interface");
                return;
            }

            storage.DeleteFile(requestData.Id);

            ReturnSuccess(context.Response);
        }

        /// <summary>
        /// Processes a GetShareUrl request
        /// </summary>
        /// <remarks>
        /// For full documentation on GetShareUrl, see
        /// https://wopi.readthedocs.io/projects/wopirest/en/latest/files/GetShareUrl.html
        /// </remarks>
        private void HandleGetShareUrlRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: false))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            long size = storage.GetFileSize(requestData.Id);

            if (size == -1)
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            string urlType = requestData.Headers.Get("X-WOPI-UrlType");
            if (string.IsNullOrEmpty(urlType) || (!urlType.Equals("ReadOnly") && !urlType.Equals("ReadWrite")))
            {
                ReturnUnsupported(context.Response);
                return;
            }

            GetShareUrlResponse responseData = new GetShareUrlResponse()
            {
                ShareUrl = "http://test"
            };

            string jsonString = JsonConvert.SerializeObject(responseData);

            context.Response.Write(jsonString);
            ReturnSuccess(context.Response);
        }

        /// <summary>
        /// Processes a PutUserInfo request
        /// </summary>
        /// <remarks>
        /// For full documentation on PutUserInfo, see
        /// https://wopi.readthedocs.io/projects/wopirest/en/latest/files/PutUserInfo.html
        /// </remarks>
        private void HandlePutUserInfoRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: false))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            long size = storage.GetFileSize(requestData.Id);

            if (size == -1)
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            string userName = AccessTokenUtil.GetUserFromToken(requestData.AccessToken);
            StreamReader reader = new StreamReader(context.Request.InputStream);
            string userInfo = reader.ReadToEnd();
            if (!UserInfo.ContainsKey(userName))
            {
                UserInfo.Add(userName, userInfo);
            }

            UserInfo[userName] = userInfo;

            ReturnSuccess(context.Response);
        }

        static Dictionary<string, string> UserInfo = new Dictionary<string, string>();

        /// <summary>
        /// Processes a GetFile request
        /// </summary>
        /// <remarks>
        /// For full documentation on GetFile, see
        /// https://wopi.readthedocs.io/projects/wopirest/en/latest/files/GetFile.html
        /// </remarks>
        private void HandleGetFileRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: false))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            Stream stream = storage.GetFile(requestData.Id);

            if (null == stream)
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            try
            {
                int i = 0;
                List<byte> bytes = new List<byte>();
                do
                {
                    byte[] buffer = new byte[1024];
                    i = stream.Read(buffer, 0, 1024);
                    if (i > 0)
                    {
                        byte[] data = new byte[i];
                        Array.Copy(buffer, data, i);
                        bytes.AddRange(data);
                    }
                }
                while (i > 0);

                context.Response.OutputStream.Write(bytes.ToArray(), 0, bytes.Count);

                stream.Close();
                stream.Dispose();

                //context.Response.AddHeader(WopiHeaders.ItemVersion, storage.GetFileVersion(requestData.FullPath));
                ReturnSuccess(context.Response);
            }
            catch (UnauthorizedAccessException)
            {
                ReturnFileUnknown(context.Response);
            }
            catch (FileNotFoundException)
            {
                ReturnFileUnknown(context.Response);
            }
        }

        /// <summary>
        /// Processes a PutFile request
        /// </summary>
        /// <remarks>
        /// For full documentation on PutFile, see
        /// https://wopi.readthedocs.io/projects/wopirest/en/latest/files/PutFile.html
        /// </remarks>
        private void HandlePutFileRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: true))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            long size = storage.GetFileSize(requestData.Id);

            if (size == -1)
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            string newLock = context.Request.Headers[WopiHeaders.Lock];
            LockInfo existingLock;
            bool hasExistingLock;

            lock (Locks)
            {
                hasExistingLock = TryGetLock(requestData.Id, out existingLock);
            }

            if (hasExistingLock && existingLock.Lock != newLock)
            {
                // lock mismatch/locked by another interface
                ReturnLockMismatch(context.Response, existingLock.Lock);
                return;
            }

            //// The WOPI spec allows for a PutFile to succeed on a non-locked file if the file is currently zero bytes in length.
            //// This allows for a more efficient Create New File flow that saves the Lock roundtrips.
            //if (!hasExistingLock && size != 0)
            //{
            //    // With no lock and a non-zero file, a PutFile could potentially result in data loss by clobbering
            //    // existing content.  Therefore, return a lock mismatch error.
            //    ReturnLockMismatch(context.Response, reason: "PutFile on unlocked file with current size != 0");
            //    return;
            //}

            // Either the file has a valid lock that matches the lock in the request, or the file is unlocked
            // and is zero bytes.  Either way, proceed with the PutFile.
            try
            {
                // TODO: Should be replaced with proper file save logic to a real storage system and ensures write atomicity                
                int result = storage.UploadFile(requestData.Id, context.Request.InputStream);
                if (result != 0)
                {
                    ReturnServerError(context.Response);
                    return;
                }

                context.Response.AddHeader(WopiHeaders.ItemVersion, storage.GetFileVersion(requestData.FullPath));

                ReturnSuccess(context.Response);
            }
            catch (UnauthorizedAccessException)
            {
                ReturnFileUnknown(context.Response);
            }
            catch (IOException)
            {
                ReturnServerError(context.Response);
            }
        }

        /// <summary>
        /// Processes a PutRelativeFile request
        /// </summary>
        /// <remarks>
        /// For full documentation on PutRelativeFile, see
        /// https://wopi.readthedocs.io/projects/wopirest/en/latest/files/PutRelativeFile.html
        /// </remarks>
        private void HandlePutRelativeFileRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: true))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            long size = storage.GetFileSize(requestData.Id);

            if (size == -1)
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            string suggestedTarget = requestData.Headers.Get("X-WOPI-SuggestedTarget");
            if (suggestedTarget != null)
            {
                suggestedTarget = HttpUtility.UrlDecode(Uri.EscapeDataString(requestData.Headers.Get("X-WOPI-SuggestedTarget")), System.Text.Encoding.UTF7);
            }

            string relativeTarget = requestData.Headers.Get("X-WOPI-RelativeTarget");
            if (relativeTarget != null)
            {
                relativeTarget = HttpUtility.UrlDecode(Uri.EscapeDataString(requestData.Headers.Get("X-WOPI-RelativeTarget")), System.Text.Encoding.UTF7); ;
            }

            string overwriteRelativeTarget = requestData.Headers.Get("X-WOPI-OverwriteRelativeTarget");
            string fileSize = requestData.Headers.Get("X-WOPI-Size");

            if (string.IsNullOrEmpty(relativeTarget) && string.IsNullOrEmpty(suggestedTarget))
            {
                ReturnUnsupported(context.Response);
                return;
            }

            if (!string.IsNullOrEmpty(relativeTarget) && !string.IsNullOrEmpty(suggestedTarget))
            {
                ReturnUnsupported(context.Response);
                return;
            }

            string newFileName = relativeTarget ?? suggestedTarget;

            if (newFileName.StartsWith(".") && newFileName.Split('.').Length == 1)
            {
                newFileName = requestData.Id.Substring(0, requestData.Id.LastIndexOf('.') - 1) + newFileName;
            }


            size = storage.GetFileSize(newFileName);

            if (size != -1)
            {
                bool overwrite = string.IsNullOrEmpty(overwriteRelativeTarget) ? false : Boolean.Parse(overwriteRelativeTarget);

                LockInfo existingLock;
                bool fLocked = TryGetLock(newFileName, out existingLock);

                if (!string.IsNullOrEmpty(relativeTarget))
                {
                    if (!overwrite || (overwrite && fLocked))
                    {
                        ReturnStatus(context.Response, 409, "Can not overwrite");
                        return;
                    }
                }
                else
                {
                    newFileName = System.Guid.NewGuid() + newFileName;
                }
            }
            storage.CreateOrOverwriteFile(newFileName, context.Request.InputStream);

            PutRelativeFileResponse responseData = new PutRelativeFileResponse()
            {
                Name = newFileName,
                Url = "http://" + context.Request.Url.Authority + context.Request.Url.AbsolutePath.Replace(requestData.Id, newFileName) + "?access_token=" +
                        AccessTokenUtil.WriteToken(AccessTokenUtil.GenerateToken("TestUser".ToLower(), newFileName.ToLower())),
                HostViewUrl = "http://" + context.Request.Url.Authority + context.Request.Url.AbsolutePath.Replace(requestData.Id, newFileName) + "?access_token=" +
                        AccessTokenUtil.WriteToken(AccessTokenUtil.GenerateToken("TestUser".ToLower(), newFileName.ToLower())),
                HostEditUrl = "http://" + context.Request.Url.Authority + context.Request.Url.AbsolutePath.Replace(requestData.Id, newFileName) + "?access_token=" +
                        AccessTokenUtil.WriteToken(AccessTokenUtil.GenerateToken("TestUser".ToLower(), newFileName.ToLower()))
            };

            string jsonString = JsonConvert.SerializeObject(responseData);

            context.Response.Write(jsonString);
            ReturnSuccess(context.Response);
        }

        /// <summary>
        /// Processes a RenameFile request
        /// </summary>
        /// <remarks>
        /// For full documentation on RenameFile, see
        /// https://wopi.readthedocs.io/projects/wopirest/en/latest/files/RenameFile.html
        /// </remarks>
        private void HandleRenameFileRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: true))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            long size = storage.GetFileSize(requestData.Id);

            if (size == -1)
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            string lockStr = requestData.Headers.Get("X-WOPI-Lock");
            string requestedName = requestData.Headers.Get("X-WOPI-RequestedName");
            if (requestedName != null)
            {
                requestedName = HttpUtility.UrlDecode(Uri.EscapeDataString(requestData.Headers.Get("X-WOPI-RequestedName")), System.Text.Encoding.UTF7);
            }

            LockInfo existingLock;
            bool fLocked = TryGetLock(requestData.Id, out existingLock);
            if (fLocked && existingLock.Lock != lockStr)
            {
                ReturnLockMismatch(context.Response, existingLock.Lock);
                return;
            }

            if (!storage.RenameFile(requestData.Id, ref requestedName))
            {
                context.Response.Headers.Add("X-WOPI-InvalidFileNameError", "File with same name has existed.");
                ReturnStatus(context.Response, 400, "File with same name has existed.");
                return;
            }

            RenameFileResponse responseData = new RenameFileResponse()
            {
                Name = requestedName
            };

            string jsonString = JsonConvert.SerializeObject(responseData);

            context.Response.Write(jsonString);
            ReturnSuccess(context.Response);
        }

        /// <summary>
        /// Processes a Lock request
        /// </summary>
        /// <remarks>
        /// For full documentation on Lock, see
        /// https://wopi.readthedocs.io/projects/wopirest/en/latest/files/Lock.html
        /// </remarks>
        private void HandleLockRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: true))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            long size = storage.GetFileSize(requestData.Id);

            if (size == -1)
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            string newLock = context.Request.Headers[WopiHeaders.Lock];

            lock (Locks)
            {
                LockInfo existingLock;
                bool fLocked = TryGetLock(requestData.Id, out existingLock);
                if (fLocked && existingLock.Lock != newLock)
                {
                    // There is a valid existing lock on the file and it doesn't match the requested lockstring.

                    // This is a fairly common case and shouldn't be tracked as an error.  Office Online can store
                    // information about a current session in the lock value and expects to conflict when there's
                    // an existing session to join.
                    ReturnLockMismatch(context.Response, existingLock.Lock);
                }
                else
                {
                    // The file is not currently locked or the lock has already expired

                    if (fLocked)
                        Locks.Remove(requestData.Id);

                    // Create and store new lock information
                    // TODO: In a real implementation the lock should be stored in a persisted and shared system.
                    Locks[requestData.Id] = new LockInfo() { DateCreated = DateTime.UtcNow, Lock = newLock };

                    context.Response.AddHeader(WopiHeaders.ItemVersion, storage.GetFileVersion(requestData.FullPath));

                    // Return success
                    ReturnSuccess(context.Response);
                }
            }
        }

        /// <summary>
        /// Processes a GetLock request
        /// </summary>
        /// <remarks>
        /// For full documentation on GetLock, see
        /// https://wopi.readthedocs.io/projects/wopirest/en/latest/files/GetLock.html
        /// </remarks>
        private void HandleGetLockRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: true))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            long size = storage.GetFileSize(requestData.Id);

            if (size == -1)
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            LockInfo existingLock;
            bool fLocked = TryGetLock(requestData.Id, out existingLock);
            string lockStr = fLocked ? existingLock.Lock : string.Empty;

            context.Response.AddHeader("X-WOPI-Lock", lockStr);

            // Return success
            ReturnSuccess(context.Response);

        }

        /// <summary>
        /// Processes a RefreshLock request
        /// </summary>
        /// <remarks>
        /// For full documentation on RefreshLock, see
        /// ttps://wopi.readthedocs.io/projects/wopirest/en/latest/files/RefreshLock.html
        /// </remarks>
        private void HandleRefreshLockRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: true))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            if (!File.Exists(requestData.FullPath))
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            string newLock = context.Request.Headers[WopiHeaders.Lock];

            lock (Locks)
            {
                LockInfo existingLock;
                if (TryGetLock(requestData.Id, out existingLock))
                {
                    if (existingLock.Lock == newLock)
                    {
                        // There is a valid lock on the file and the existing lock matches the provided one

                        // Extend the lock timeout
                        existingLock.DateCreated = DateTime.UtcNow;
                        ReturnSuccess(context.Response);
                    }
                    else
                    {
                        // The existing lock doesn't match the requested one.  Return a lock mismatch error
                        // along with the current lock
                        ReturnLockMismatch(context.Response, existingLock.Lock);
                    }
                }
                else
                {
                    // The requested lock does not exist.  That's also a lock mismatch error.
                    ReturnLockMismatch(context.Response, reason: "File not locked");
                }
            }
        }

        /// <summary>
        /// Processes a Unlock request
        /// </summary>
        /// <remarks>
        /// For full documentation on Unlock, see
        /// https://wopi.readthedocs.io/projects/wopirest/en/latest/files/Unlock.html
        /// </remarks>
        private void HandleUnlockRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: true))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            long size = storage.GetFileSize(requestData.Id);

            if (size == -1)
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            string newLock = context.Request.Headers[WopiHeaders.Lock];

            lock (Locks)
            {
                LockInfo existingLock;
                if (TryGetLock(requestData.Id, out existingLock))
                {
                    if (existingLock.Lock == newLock)
                    {
                        // There is a valid lock on the file and the existing lock matches the provided one

                        // Remove the current lock
                        Locks.Remove(requestData.Id);
                        context.Response.AddHeader(WopiHeaders.ItemVersion, storage.GetFileVersion(requestData.FullPath));
                        ReturnSuccess(context.Response);
                    }
                    else
                    {
                        // The existing lock doesn't match the requested one.  Return a lock mismatch error
                        // along with the current lock
                        ReturnLockMismatch(context.Response, existingLock.Lock);
                    }
                }
                else
                {
                    // The requested lock does not exist.  That's also a lock mismatch error.
                    ReturnLockMismatch(context.Response, reason: "File not locked");
                }
            }
        }

        /// <summary>
        /// Processes a UnlockAndRelock request
        /// </summary>
        /// <remarks>
        /// For full documentation on UnlockAndRelock, see
        /// https://wopi.readthedocs.io/projects/wopirest/en/latest/files/UnlockAndRelock.html
        /// </remarks>
        private void HandleUnlockAndRelockRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: true))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            long size = storage.GetFileSize(requestData.Id);

            if (size == -1)
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            string newLock = context.Request.Headers[WopiHeaders.Lock];
            string oldLock = context.Request.Headers[WopiHeaders.OldLock];

            lock (Locks)
            {
                LockInfo existingLock;
                if (TryGetLock(requestData.Id, out existingLock))
                {
                    if (existingLock.Lock == oldLock)
                    {
                        // There is a valid lock on the file and the existing lock matches the provided one

                        // Replace the existing lock with the new one
                        Locks[requestData.Id] = new LockInfo() { DateCreated = DateTime.UtcNow, Lock = newLock };
                        context.Response.Headers[WopiHeaders.OldLock] = newLock;
                        ReturnSuccess(context.Response);
                    }
                    else
                    {
                        // The existing lock doesn't match the requested one.  Return a lock mismatch error
                        // along with the current lock
                        ReturnLockMismatch(context.Response, existingLock.Lock);
                    }
                }
                else
                {
                    // The requested lock does not exist.  That's also a lock mismatch error.
                    ReturnLockMismatch(context.Response, reason: "File not locked");
                }
            }
        }

        /// <summary>
        /// Processes a GetRestrictedLink request
        /// </summary>
        /// <remarks>
        /// For full documentation on GetRestrictedLink, see
        /// https://wopi.readthedocs.io/projects/wopirest/en/latest/files/GetRestrictedLink.html
        /// </remarks>
        private void HandleGetRestrictedLink(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: false))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            if (string.IsNullOrEmpty(requestData.Headers.Get(WopiHeaders.RestrictedLink))
                || !requestData.Headers[WopiHeaders.RestrictedLink].Equals("FORMS"))
            {
                ReturnUnsupported(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            long size = storage.GetFileSize(requestData.Id);

            if (size == -1)
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            if (WithoutRestrictedLinks.Contains(requestData.Id))
            {
                context.Response.AddHeader("X-WOPI-RestrictedUseLink", string.Empty);
            }
            else
            {
                context.Response.AddHeader("X-WOPI-RestrictedUseLink", "http://officeserver4/restricted/" + requestData.Id);
            }

            ReturnSuccess(context.Response);
        }

        /// <summary>
        /// Processes a RevokeRestrictedLink request
        /// </summary>
        /// <remarks>
        /// For full documentation on RevokeRestrictedLink, see
        /// https://wopi.readthedocs.io/projects/wopirest/en/latest/files/RevokeRestrictedLink.html
        /// </remarks>
        private void HandleRevokeRestrictedLink(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: false))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            if (string.IsNullOrEmpty(requestData.Headers.Get(WopiHeaders.RestrictedLink))
                || !requestData.Headers[WopiHeaders.RestrictedLink].Equals("FORMS"))
            {
                ReturnUnsupported(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            long size = storage.GetFileSize(requestData.Id);

            if (size == -1)
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            if (!WithoutRestrictedLinks.Contains(requestData.Id))
            {
                WithoutRestrictedLinks.Add(requestData.Id);
            }

            ReturnSuccess(context.Response);
        }

        /// <summary>
        /// Processes a ReadSecureStore request
        /// </summary>
        /// <remarks>
        /// For full documentation on ReadSecureStore, see
        /// https://wopi.readthedocs.io/projects/wopirest/en/latest/files/ReadSecureStore.html
        /// </remarks>
        private void HandleReadSecureStore(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: false))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            if (string.IsNullOrEmpty(requestData.Headers.Get(WopiHeaders.ApplicationId)))
            {
                ReturnUnsupported(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            long size = storage.GetFileSize(requestData.Id);

            if (size == -1)
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            ReadSecureStoreResponse responseData = new ReadSecureStoreResponse
            {
                UserName = "WOPITestUser",
                Password = "Password01!",
                IsWindowsCredentials = true,
                IsGroup = false
            };

            if (!string.IsNullOrEmpty(requestData.Headers.Get("X-WOPI-PerfTraceRequested")))
            {
                if (Boolean.Parse(requestData.Headers.Get("X-WOPI-PerfTraceRequested")))
                {
                    context.Response.AddHeader("X-WOPI-PerfTrace", "test data");
                }
            }

            string jsonString = JsonConvert.SerializeObject(responseData);

            context.Response.Write(jsonString);
            ReturnSuccess(context.Response);
        }

        /// <summary>
        /// Processes a CheckFolderInfo request
        /// </summary>
        private void HandleCheckFolderInfo(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: false))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            DirectoryInfo directory = storage.GetDirecotry();

            if (!requestData.Id.ToLower().Equals(directory.Name.ToLower()))
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            CheckFolderInfoResponse responseData = new CheckFolderInfoResponse
            {
                FolderName = directory.Name,
                OwnerId = "FolderOwnerId"
            };

            string jsonString = JsonConvert.SerializeObject(responseData);

            context.Response.Write(jsonString);
            ReturnSuccess(context.Response);
        }

        /// <summary>
        /// Processes an EnumerateAncestors (files) request
        /// </summary>
        /// <remarks>
        /// For full documentation on EnumerateAncestors (files), see
        /// https://wopi.readthedocs.io/projects/wopirest/en/latest/files/EnumerateAncestors.html
        /// </remarks>
        private void HandleEnumerateAncestorsForFile(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: false))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            long size = storage.GetFileSize(requestData.Id);

            if (size == -1)
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            DirectoryInfo directory = storage.GetDirecotry();
            EnumerateAncestorsResponse responseData = new EnumerateAncestorsResponse
            {
                AncestorsWithRootFirst = new Ancestor[]
                {
                    new Ancestor
                    {
                        Name = directory.Name,
                        Url = "http://" + context.Request.Url.Host + "wopi/folders/" + directory.Name + "?access_token=" +
                        AccessTokenUtil.WriteToken(AccessTokenUtil.GenerateToken("TestUser".ToLower(), directory.Name))
                    }
                }
            };

            string jsonString = JsonConvert.SerializeObject(responseData);
            context.Response.AddHeader("X-WOPI-EnumerationIncomplete", "true");
            context.Response.Write(jsonString);
            ReturnSuccess(context.Response);
        }

        /// <summary>
        /// Processes an EnumerateChildren request
        /// </summary>
        private void HandleEnumerateChildren(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: false))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            IFileStorage storage = FileStorageFactory.CreateFileStorage();
            if (!requestData.Id.Equals(storage.GetDirecotry().Name, StringComparison.InvariantCultureIgnoreCase))
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            string relativeTarget = requestData.Headers.Get("X-WOPI-RelativeTarget");

            DirectoryInfo directory = storage.GetDirecotry();

            List<Child> children = new List<Child>();

            foreach (FileInfo file in directory.EnumerateFiles())
            {
                Child child = new Child();
                child.Name = file.Name;
                child.Version = storage.GetFileVersion(file.Name);
                child.Url = "http://" + context.Request.Url.Authority+ "/wopi/files/" + file.Name + "?access_token=" +
                    AccessTokenUtil.WriteToken(AccessTokenUtil.GenerateToken("TestUser".ToLower(), file.Name.ToLower()));
                children.Add(child);
            }
            EnumerateChildrenResponse responseData = new EnumerateChildrenResponse
            {
                Children = children.ToArray()
            };

            string jsonString = JsonConvert.SerializeObject(responseData);
            context.Response.Write(jsonString);
            ReturnSuccess(context.Response);
        }

        #endregion

        /// <summary>
        /// Validate WOPI ProofKey to make sure request came from the expected Office Web Apps Server.
        /// </summary>
        /// <param name="request">Request information</param>
        /// <returns>true when WOPI ProofKey validation succeeded, false otherwise.</returns>
        private static bool ValidateWopiProofKey(HttpRequest request)
        {
            // TODO: WOPI proof key validation is not implemented in this sample.
            // For more details on proof keys, see the documentation
            // https://wopi.readthedocs.io/en/latest/scenarios/proofkeys.html

            // The proof keys are returned by WOPI Discovery. For more details, see
            // https://wopi.readthedocs.io/en/latest/discovery.html

            return true;
        }

        /// <summary>
        /// Validate that the provided access token is valid to get access to requested resource.
        /// </summary>
        /// <param name="requestData">Request information, including requested file Id</param>
        /// <param name="writeAccessRequired">Whether write permission is requested or not.</param>
        /// <returns>true when access token is correct and user has access to document, false otherwise.</returns>
        private static bool ValidateAccess(WopiRequest requestData, bool writeAccessRequired)
        {
            //// TODO: Access token validation is not implemented in this sample.
            //// For more details on access tokens, see the documentation
            //// https://wopi.readthedocs.io/projects/wopirest/en/latest/concepts.html#term-access-token
            //return !String.IsNullOrWhiteSpace(requestData.AccessToken);

            if (AccessTokenUtil.ValidateToken(requestData.AccessToken, requestData.Id.ToLower()))
            {
                string userName = AccessTokenUtil.GetUserFromToken(requestData.AccessToken);
                string userPermission = AccessTokenUtil.readUserXml(uid, requestData.Id);

                if (userPermission.Equals("none"))
                {
                    return false;
                }
                else if (!writeAccessRequired
                    || (userPermission.Equals("write") && writeAccessRequired))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ReturnSuccess(HttpResponse response)
        {
            ReturnStatus(response, 200, "Success");
        }

        private static void ReturnInvalidToken(HttpResponse response)
        {
            ReturnStatus(response, 401, "Invalid Token");
        }

        private static void ReturnFileUnknown(HttpResponse response)
        {
            ReturnStatus(response, 404, "File Unknown/User Unauthorized");
        }

        private static void ReturnLockMismatch(HttpResponse response, string existingLock = null, string reason = null)
        {
            response.Headers[WopiHeaders.Lock] = existingLock ?? String.Empty;
            if (!String.IsNullOrEmpty(reason))
            {
                response.Headers[WopiHeaders.LockFailureReason] = reason;
            }

            ReturnStatus(response, 409, "Lock mismatch/Locked by another interface");
        }

        private static void ReturnServerError(HttpResponse response)
        {
            ReturnStatus(response, 500, "Server Error");
        }

        private static void ReturnUnsupported(HttpResponse response)
        {
            ReturnStatus(response, 501, "Unsupported");
        }

        private static void ReturnStatus(HttpResponse response, int code, string description)
        {
            response.AddHeader("X-WOPI-ServerVersion", "TestServerVersion");
            response.AddHeader("X-WOPI-MachineName", "TestMachineName");
            response.StatusCode = code;
            response.StatusDescription = description;
        }

        private bool TryGetLock(string fileId, out LockInfo lockInfo)
        {
            // TODO: This lock implementation is not thread safe and not persisted and all in all just an example.
            if (Locks.TryGetValue(fileId, out lockInfo))
            {
                if (lockInfo.Expired)
                {
                    Locks.Remove(fileId);
                    return false;
                }
                return true;
            }

            return false;
        }
    }
}
