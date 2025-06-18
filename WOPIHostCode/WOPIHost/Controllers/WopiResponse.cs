using System.Collections.Generic;

namespace WOPIHost.Controllers
{

    class ActivityResponse
    {
        public string Id { get; set; }
        public int Status { get; set; }
        public string Message { get; set; }
    }


    class AddActivitiesResponse
    {
        public List<ActivityResponse> ActivityResponses { get; set; }
    }

    class CheckFileInfoResponse
    {
        // This is a subset of all CheckFileInfo properties.
        // Use optional properties in accordance with the [MS-WOPI] Web Application Open Platform Interface Protocol specification.

        public string BaseFileName { get; set; }
        public string OfficeCollaborationServiceEndpointUrl { get; set; }
        public string OpenInClientCommandUrl { get; set; }
        public bool OpenInClientPostMessage { get; set; }
        public string OwnerId { get; set; }
        public int Size { get; set; }
        public string UserId { get; set; }
        public string UserPrincipalName { get; set; }
        public string Version { get; set; }
        public string FileExtension { get; set; }

        public string BreadcrumbBrandName { get; set; }
        public string BreadcrumbBrandUrl { get; set; }
        public string BreadcrumbFolderName { get; set; }
        public string BreadcrumbFolderUrl { get; set; }
        public string BreadcrumbDocName { get; set; }

        public bool UserCanWrite { get; set; }
        public bool ReadOnly { get; set; }

        public bool SupportsAddActivities { get; set; }
        public bool SupportsLocks { get; set; }
        public bool SupportsUpdate { get; set; }
        public bool UserCanNotWriteRelative { get; set; }

        public string UserFriendlyName { get; set; }

        //public bool WebEditingDisabled { get; set; }
        public bool RestrictedWebViewOnly { get; set; }
        //public bool SupportsCoauth { get; set; }
        public bool SupportsCobalt { get; set; }

        //public string HostEditUrl { get; set; }
        public bool EditModePostMessage { get; set; }
        //public bool DisableBrowserCachingOfUserContent { get; set; }

        //public string SHA256 { get; set; }

        public bool SupportsScenarioLinks { get; set; }
        public bool SupportsSecureStore { get; set; }
        public bool SupportsFolders { get; set; }
        public bool SupportsRename { get; set; }
        public bool UserCanRename { get; set; }
        public bool SupportsGetLock { get; set; }
        public bool SupportsUserInfo { get; set; }
        //public bool SupportsDeleteFile { get; set; }
        public bool SupportsExtendedLockLength { get; set; }
        public string[] SupportedShareUrlTypes { get; set; }
        public string UserInfo { get; set; }
    }

    class PutRelativeFileResponse
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string HostViewUrl { get; set; }
        public string HostEditUrl { get; set; }
    }

    class ReadSecureStoreResponse
    {
        public string UserName { get; set; }
        public string Password { get; set; }
        public bool IsWindowsCredentials { get; set; }
        public bool IsGroup { get; set; }
    }

    class RenameFileResponse
    {
        public string Name { get; set; }
    }

    class GetShareUrlResponse
    {
        public string ShareUrl { get; set; }
    }

    class EnumerateAncestorsResponse
    {
        public Ancestor[] AncestorsWithRootFirst { get; set; }
    }

    class Ancestor
    {
        public string Url { get; set; }
        public string Name { get; set; }
    }

    class EnumerateChildrenResponse
    {
        public Child[] Children { get; set; }
    }

    class Child
    {
        public string Url { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
    }

    class CheckFolderInfoResponse
    {
        public string FolderName { get; set; }
        //public string BreadcrumbBrandIconUrl { get; set; }
        //public string BreadcrumbBrandName { get; set; }
        //public string BreadcrumbBrandUrl { get; set; }
        //public string BreadcrumbDocName { get; set; }
        //public string BreadcrumbDocUrl { get; set; }
        //public string BreadcrumbFolderName { get; set; }
        //public string BreadcrumbFolderUrl { get; set; }
        //public string ClientUrl { get; set; }
        //public bool CloseButtonClosesWindow { get; set; }
        //public string CloseUrl { get; set; }
        //public string FileSharingUrl { get; set; }
        //public string HostAuthenticationId { get; set; }
        //public string HostEditUrl { get; set; }
        //public string HostEmbeddedEditUrl { get; set; }
        //public string HostEmbeddedViewUrl { get; set; }
        //public string HostName { get; set; }
        //public string HostViewUrl { get; set; }
        public string OwnerId { get; set; }
        //public string PresenceProvider { get; set; }
        //public string PresenceUserId { get; set; }
        //public string PrivacyUrl { get; set; }
        //public string SignoutUrl { get; set; }
        //public bool SupportsSecureStore { get; set; }
        //public string TenantId { get; set; }
        //public string TermsOfUseUrl { get; set; }
        //public bool UserCanWrite { get; set; }
        //public string UserFriendlyName { get; set; }
        //public string UserId { get; set; }
        //public bool WebEditingDisabled { get; set; }
    }
}
