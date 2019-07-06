﻿/*
 * CKEditor Html Editor Provider for DNN
 * ========
 * https://github.com/w8tcha/dnnckeditor
 * Copyright (C) Ingo Herbote
 *
 * The software, this file and its contents are subject to the CKEditor Provider
 * License. Please read the license.txt file before using, installing, copying,
 * modifying or distribute this file or part of its contents. The contents of
 * this file is part of the Source Code of the CKEditor Provider.
 */

namespace WatchersNET.CKEditor.Browser
{
    #region

    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Drawing.Imaging;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Web;
    using System.Web.Script.Serialization;
    using System.Web.Script.Services;
    using System.Web.Services;
    using System.Web.UI;
    using System.Web.UI.HtmlControls;
    using System.Web.UI.WebControls;

    using DotNetNuke.Common.Utilities;
    using DotNetNuke.Entities.Controllers;
    using DotNetNuke.Entities.Portals;
    using DotNetNuke.Entities.Tabs;
    using DotNetNuke.Entities.Users;
    using DotNetNuke.Framework.Providers;
    using DotNetNuke.Security;
    using DotNetNuke.Security.Permissions;
    using DotNetNuke.Security.Roles;
    using DotNetNuke.Services.FileSystem;
    using DotNetNuke.Services.Localization;
    using DotNetNuke.UI.Utilities;

    using WatchersNET.CKEditor.Constants;
    using WatchersNET.CKEditor.Controls;
    using WatchersNET.CKEditor.Objects;
    using WatchersNET.CKEditor.Utilities;

    using Encoder = System.Drawing.Imaging.Encoder;
    using Globals = DotNetNuke.Common.Globals;
    using Image = System.Drawing.Image;

    #endregion

    /// <summary>
    /// The browser.
    /// </summary>
    [ScriptService]
    public partial class Browser : Page
    {
        #region Constants and Fields

        /// <summary>
        /// The Image or Link that is selected inside the Editor.
        /// </summary>
        private static string ckFileUrl;

        /// <summary>
        ///   The allowed flash ext.
        /// </summary>
        private readonly string[] allowedFlashExt = { "swf", "flv", "mp3" };

        /// <summary>
        ///   The allowed image ext.
        /// </summary>
        private readonly List<string> allowedImageExtensions = new List<string>();

        /// <summary>
        ///   The request.
        /// </summary>
        private readonly HttpRequest request = HttpContext.Current.Request;

        /// <summary>
        /// Current Settings Base
        /// </summary>
        private EditorProviderSettings currentSettings = new EditorProviderSettings();

        /// <summary>
        ///   The _portal settings.
        /// </summary>
        private PortalSettings _portalSettings;

        /// <summary>
        ///   The extension white list.
        /// </summary>
        private string extensionWhiteList;

        /// <summary>
        /// The browser Modus
        /// </summary>
        private string browserModus;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the accept file types.
        /// </summary>
        /// <value>
        /// The accept file types.
        /// </value>
        public string AcceptFileTypes
        {
            get => this.ViewState["AcceptFileTypes"]?.ToString() ?? ".*";

            set => this.ViewState["AcceptFileTypes"] = value;
        }

        /// <summary>
        ///   Gets Current Language from Url
        /// </summary>
        protected string LanguageCode =>
            !string.IsNullOrEmpty(this.request.QueryString["lang"]) ? this.request.QueryString["lang"] : "en-US";

        /// <summary>
        /// Gets the Name for the Current Resource file name
        /// </summary>
        /// <value>
        /// The resource executable file.
        /// </value>
        protected string ResXFile
        {
            get
            {
                var page = this.Request.ServerVariables["SCRIPT_NAME"].Split('/');

                var fileRoot =
                    $"{this.TemplateSourceDirectory}/{Localization.LocalResourceDirectory}/{page[page.GetUpperBound(0)]}.resx";

                return fileRoot;
            }
        }

        /// <summary>
        /// Gets the maximum size of the upload.
        /// </summary>
        /// <value>
        /// The maximum size of the upload.
        /// </value>
        protected long MaxUploadSize =>
            this.currentSettings.UploadFileSizeLimit > 0
            && this.currentSettings.UploadFileSizeLimit <= Utility.GetMaxUploadSize()
                ? this.currentSettings.UploadFileSizeLimit
                : Utility.GetMaxUploadSize();

        /// <summary>
        /// Gets the get folder information identifier.
        /// </summary>
        /// <value>
        /// The get folder information identifier.
        /// </value>
        protected int GetFolderInfoID =>
            Utility.ConvertFilePathToFolderInfo(this.lblCurrentDir.Text, this._portalSettings).FolderID;

        /// <summary>
        /// Gets or sets the files table.
        /// </summary>
        /// <value>
        /// The files table.
        /// </value>
        private DataTable FilesTable
        {
            get => this.ViewState["FilesTable"] as DataTable;

            set => this.ViewState["FilesTable"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether [sort files descending].
        /// </summary>
        /// <value>
        ///   <c>true</c> if [sort files descending]; otherwise sort ascending.
        /// </value>
        private bool SortFilesDescending
        {
            get => this.ViewState["SortFilesDescending"] != null && (bool)this.ViewState["SortFilesDescending"];

            set
            {
                this.ViewState["SortFilesDescending"] = value;
                this.FilesTable = null;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Set the file url from JavaScript to code
        /// </summary>
        /// <param name="fileUrl">
        /// The file url.
        /// </param>
        [WebMethod]
        public static void SetFile(string fileUrl)
        {
            ckFileUrl = fileUrl;
        }

        /// <summary>
        /// Get all Files and Put them in a DataTable for the GridView
        /// </summary>
        /// <param name="currentFolderInfo">The current folder info.</param>
        /// <returns>
        /// The File Table
        /// </returns>
        public DataTable GetFiles(IFolderInfo currentFolderInfo)
        {
            var filesTable = new DataTable();

            filesTable.Columns.Add(new DataColumn("FileName", typeof(string)));
            filesTable.Columns.Add(new DataColumn("PictureURL", typeof(string)));
            filesTable.Columns.Add(new DataColumn("Info", typeof(string)));
            filesTable.Columns.Add(new DataColumn("FileId", typeof(int)));

            var httpRequest = HttpContext.Current.Request;

            var type = "Link";

            if (!string.IsNullOrEmpty(httpRequest.QueryString["Type"]))
            {
                type = httpRequest.QueryString["Type"];
            }

            // Get Folder Info Secure?
            var isSecure = this.GetStorageLocationType(currentFolderInfo.FolderID)
                .Equals(FolderController.StorageLocationTypes.SecureFileSystem);

            var isDatabaseSecure = this.GetStorageLocationType(currentFolderInfo.FolderID)
                .Equals(FolderController.StorageLocationTypes.DatabaseSecure);

            var files = FolderManager.Instance.GetFiles(currentFolderInfo).ToList();

            if (this.SortFilesDescending)
            {
                Utility.SortDescending(files, item => item.FileName);
            }

            foreach (var fileItem in files)
            {
                // Check if File Exists
                /*if (!File.Exists(string.Format("{0}{1}", fileItem.PhysicalPath, isSecure ? ".resources" : string.Empty)))
                {
                    continue;
                }*/
                var item = fileItem;

                var name = fileItem.FileName;
                var extension = fileItem.Extension;

                if (isSecure)
                {
                    name = GetFileNameCleaned(name);
                    extension = Path.GetExtension(name);
                }

                switch (type)
                {
                    case "Image":
                        {
                            foreach (var dr in from sAllowExt in this.allowedImageExtensions
                                               where name.ToLower().EndsWith(sAllowExt)
                                               select filesTable.NewRow())
                            {
                                if (isSecure || isDatabaseSecure)
                                {
                                    var link = $"fileID={fileItem.FileId}";

                                    dr["PictureURL"] = Globals.LinkClick(
                                        link,
                                        int.Parse(this.request.QueryString["tabid"]),
                                        Null.NullInteger);
                                }
                                else
                                {
                                    dr["PictureURL"] = MapUrl(fileItem.PhysicalPath);
                                }

                                dr["FileName"] = name;
                                dr["FileId"] = item.FileId;

                                dr["Info"] =
                                    $"<span class=\"FileName\">{name}</span><br /><span class=\"FileInfo\">Size: {fileItem.Size}</span><br /><span class=\"FileInfo\">Created: {fileItem.LastModificationTime}</span>";

                                filesTable.Rows.Add(dr);
                            }
                        }

                        break;
                    case "Flash":
                        {
                            foreach (var dr in from sAllowExt in this.allowedFlashExt
                                               where name.ToLower().EndsWith(sAllowExt)
                                               select filesTable.NewRow())
                            {
                                dr["PictureURL"] = "images/types/swf.png";

                                dr["Info"] =
                                    $"<span class=\"FileName\">{name}</span><br /><span class=\"FileInfo\">Size: {fileItem.Size}</span><br /><span class=\"FileInfo\">Created: {fileItem.LastModificationTime}</span>";

                                dr["FileName"] = name;
                                dr["FileId"] = item.FileId;

                                filesTable.Rows.Add(dr);
                            }
                        }

                        break;

                    default:
                        {
                            if (extension.StartsWith("."))
                            {
                                extension = extension.Replace(".", string.Empty);
                            }

                            if (extension.Count() <= 1 || !this.extensionWhiteList.Contains(extension.ToLower()))
                            {
                                continue;
                            }

                            var dr = filesTable.NewRow();

                            var imageExtension = $"images/types/{extension}.png";

                            if (File.Exists(this.MapPath(imageExtension)))
                            {
                                dr["PictureURL"] = imageExtension;
                            }
                            else
                            {
                                dr["PictureURL"] = "images/types/unknown.png";
                            }

                            if (this.allowedImageExtensions.Any(sAllowImgExt => name.ToLower().EndsWith(sAllowImgExt)))
                            {
                                if (isSecure || isDatabaseSecure)
                                {
                                    var link = $"fileID={fileItem.FileId}";

                                    dr["PictureURL"] = Globals.LinkClick(
                                        link,
                                        int.Parse(this.request.QueryString["tabid"]),
                                        Null.NullInteger);
                                }
                                else
                                {
                                    dr["PictureURL"] = MapUrl(fileItem.PhysicalPath);
                                }
                            }

                            dr["FileName"] = name;
                            dr["FileId"] = fileItem.FileId;

                            dr["Info"] =
                                $"<span class=\"FileName\">{name}</span><br /><span class=\"FileInfo\">Size: {fileItem.Size}</span><br /><span class=\"FileInfo\">Created: {fileItem.LastModificationTime}</span>";

                            filesTable.Rows.Add(dr);
                        }

                        break;
                }
            }

            return filesTable;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Register JavaScripts and CSS
        /// </summary>
        /// <param name="e">
        /// The Event Args.
        /// </param>
        protected override void OnPreRender(EventArgs e)
        {
            this.LoadFavIcon();

            var jqueryScriptLink = new HtmlGenericControl("script");

            jqueryScriptLink.Attributes["type"] = "text/javascript";
            jqueryScriptLink.Attributes["src"] = this.ResolveUrl("../Scripts/jquery-3.4.1.min.js");

            this.favicon.Controls.Add(jqueryScriptLink);

            var jqueryUiScriptLink = new HtmlGenericControl("script");

            jqueryUiScriptLink.Attributes["type"] = "text/javascript";
            jqueryUiScriptLink.Attributes["src"] = this.ResolveUrl("../Scripts/jquery-ui-1.12.1.min.js");

            this.favicon.Controls.Add(jqueryUiScriptLink);

            var jqueryImageSliderScriptLink = new HtmlGenericControl("script");

            jqueryImageSliderScriptLink.Attributes["type"] = "text/javascript";
            jqueryImageSliderScriptLink.Attributes["src"] = this.ResolveUrl("../Scripts/jquery.ImageSlider.js");

            this.favicon.Controls.Add(jqueryImageSliderScriptLink);

            var jqueryImageResizerScriptLink = new HtmlGenericControl("script");

            jqueryImageResizerScriptLink.Attributes["type"] = "text/javascript";
            jqueryImageResizerScriptLink.Attributes["src"] = this.ResolveUrl("../Scripts/jquery.cropzoom.js");

            this.favicon.Controls.Add(jqueryImageResizerScriptLink);

            var jqueryCropZoomScriptLink = new HtmlGenericControl("script");

            jqueryCropZoomScriptLink.Attributes["type"] = "text/javascript";
            jqueryCropZoomScriptLink.Attributes["src"] = this.ResolveUrl("../Scripts/jquery.ImageResizer.js");

            this.favicon.Controls.Add(jqueryCropZoomScriptLink);

            var jqueryPageMetodScriptLink = new HtmlGenericControl("script");

            jqueryPageMetodScriptLink.Attributes["type"] = "text/javascript";
            jqueryPageMetodScriptLink.Attributes["src"] = this.ResolveUrl("../Scripts/jquery.pagemethod.js");

            this.favicon.Controls.Add(jqueryPageMetodScriptLink);

            var jqueryFileUploadScriptLink = new HtmlGenericControl("script");

            jqueryFileUploadScriptLink.Attributes["type"] = "text/javascript";
            jqueryFileUploadScriptLink.Attributes["src"] = this.ResolveUrl("../Scripts/jquery.fileupload.comb.min.js");

            this.favicon.Controls.Add(jqueryFileUploadScriptLink);

            var objCssLink = new HtmlGenericSelfClosing("link");

            objCssLink.Attributes["rel"] = "stylesheet";
            objCssLink.Attributes["type"] = "text/css";
            objCssLink.Attributes["href"] = this.ResolveUrl("../Content/themes/base/jquery-ui.min.css");

            this.favicon.Controls.Add(objCssLink);

            this.GetSelectedImageOrLink();

            if (this.Page.IsPostBack)
            {
                var script = @"var elem = document.getElementById('{0}_SelectedNode');
                          if(elem != null )
                          {
                                var node = document.getElementById(elem.value);
                                if(node != null)
                                {
                                     node.scrollIntoView(true);
                                }
                          }
                        ";
                ScriptManager.RegisterStartupScript(
                    this,
                    this.GetType(),
                    "scrollIntoViewScript",
                    script.Replace("{0}", this.FoldersTree.ClientID),
                    true);
            }

            base.OnPreRender(e);
        }

        /// <summary>
        /// Close Browser Window
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        protected void CmdCloseClick(object sender, EventArgs e)
        {
            if (!this.panLinkMode.Visible && this.panPageMode.Visible)
            {
                if (this.dnntreeTabs.SelectedNode == null)
                {
                    return;
                }

                var tabController = new TabController();

                var selectTab = tabController.GetTab(
                    int.Parse(this.dnntreeTabs.SelectedValue),
                    this._portalSettings.PortalId,
                    true);

                string fileName = null;
                var domainName = $"http://{Globals.GetDomainName(this.Request, true)}";

                // Add Language Parameter ?!
                var localeSelected = this.LanguageRow.Visible && this.LanguageList.SelectedIndex > 0;

                var friendlyUrl = localeSelected
                                      ? Globals.FriendlyUrl(
                                          selectTab,
                                          $"{Globals.ApplicationURL(selectTab.TabID)}&language={this.LanguageList.SelectedValue}",
                                          this._portalSettings)
                                      : Globals.FriendlyUrl(
                                          selectTab,
                                          Globals.ApplicationURL(selectTab.TabID),
                                          this._portalSettings);

                var locale = localeSelected ? $"language/{this.LanguageList.SelectedValue}/" : string.Empty;

                // Relative or Absolute Url
                switch (this.rblLinkType.SelectedValue)
                {
                    case "relLnk":
                        {
                            if (this.chkHumanFriendy.Checked)
                            {
                                fileName = friendlyUrl;

                                fileName = Globals.ResolveUrl(
                                    Regex.Replace(fileName, domainName, "~", RegexOptions.IgnoreCase));
                            }
                            else
                            {
                                fileName = Globals.ResolveUrl($"~/tabid/{selectTab.TabID}/{locale}Default.aspx");
                            }

                            break;
                        }

                    case "absLnk":
                        {
                            if (this.chkHumanFriendy.Checked)
                            {
                                fileName = friendlyUrl;

                                fileName = Globals.ResolveUrl(
                                    Regex.Replace(fileName, domainName, "~", RegexOptions.IgnoreCase));

                                fileName =
                                    $"{HttpContext.Current.Request.Url.Scheme}://{HttpContext.Current.Request.Url.Authority}{fileName}";
                            }
                            else
                            {
                                fileName = string.Format(
                                    "{2}/tabid/{0}/{1}Default.aspx",
                                    selectTab.TabID,
                                    locale,
                                    domainName);
                            }
                        }

                        break;
                    case "lnkClick":
                        {
                            fileName = Globals.LinkClick(
                                selectTab.TabID.ToString(),
                                this.TrackClicks.Checked
                                    ? int.Parse(this.request.QueryString["tabid"])
                                    : Null.NullInteger,
                                Null.NullInteger);

                            if (fileName.Contains("&language"))
                            {
                                fileName = fileName.Remove(fileName.IndexOf("&language", StringComparison.Ordinal));
                            }

                            break;
                        }

                    case "lnkAbsClick":
                        {
                            fileName =
                                $"{HttpContext.Current.Request.Url.Scheme}://{HttpContext.Current.Request.Url.Authority}{Globals.LinkClick(selectTab.TabID.ToString(), this.TrackClicks.Checked ? int.Parse(this.request.QueryString["tabid"]) : Null.NullInteger, Null.NullInteger)}";

                            if (fileName.Contains("&language"))
                            {
                                fileName = fileName.Remove(fileName.IndexOf("&language", StringComparison.Ordinal));
                            }

                            break;
                        }
                }

                // Add Page Anchor if one is selected
                if (this.AnchorList.SelectedIndex > 0 && this.AnchorList.Items.Count > 1)
                {
                    fileName = $"{fileName}#{this.AnchorList.SelectedItem.Text}";
                }

                this.Response.Write("<script type=\"text/javascript\">");
                this.Response.Write(this.GetJavaScriptCode(fileName, null, true));
                this.Response.Write("</script>");

                this.Response.End();
            }
            else if (this.panLinkMode.Visible && !this.panPageMode.Visible)
            {
                if (!string.IsNullOrEmpty(this.lblFileName.Text) && !string.IsNullOrEmpty(this.FileId.Text))
                {
                    var fileInfo = FileManager.Instance.GetFile(int.Parse(this.FileId.Text));

                    var fileName = fileInfo.FileName;
                    var filePath = string.Empty;

                    // Relative or Absolute Url
                    switch (this.rblLinkType.SelectedValue)
                    {
                        case "relLnk":
                            {
                                filePath = MapUrl(this.lblCurrentDir.Text);
                                break;
                            }

                        case "absLnk":
                            {
                                filePath =
                                    $"{HttpContext.Current.Request.Url.Scheme}://{HttpContext.Current.Request.Url.Authority}{MapUrl(this.lblCurrentDir.Text)}";
                                break;
                            }

                        case "lnkClick":
                            {
                                var link = $"fileID={fileInfo.FileId}";

                                fileName = Globals.LinkClick(
                                    link,
                                    int.Parse(this.request.QueryString["tabid"]),
                                    Null.NullInteger,
                                    this.TrackClicks.Checked);
                                filePath = string.Empty;

                                break;
                            }

                        case "lnkAbsClick":
                            {
                                var link = $"fileID={fileInfo.FileId}";

                                fileName =
                                    $"{HttpContext.Current.Request.Url.Scheme}://{HttpContext.Current.Request.Url.Authority}{Globals.LinkClick(link, int.Parse(this.request.QueryString["tabid"]), Null.NullInteger, this.TrackClicks.Checked)}";

                                filePath = string.Empty;

                                break;
                            }
                    }

                    this.Response.Write("<script type=\"text/javascript\">");
                    this.Response.Write(this.GetJavaScriptCode(fileName, filePath, false));
                    this.Response.Write("</script>");

                    this.Response.End();
                }
                else
                {
                    this.Response.Write("<script type=\"text/javascript\">");
                    this.Response.Write(
                        $"javascript:alert('{Localization.GetString("Error5.Text", this.ResXFile, this.LanguageCode)}');");
                    this.Response.Write("</script>");

                    this.Response.End();
                }
            }
        }

        /// <summary>
        /// Gets the java script code.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <param name="fileUrl">The file URL.</param>
        /// <param name="isPageLink">if set to <c>true</c> [is page link].</param>
        /// <returns>
        /// Returns the java script code
        /// </returns>
        protected virtual string GetJavaScriptCode(string fileName, string fileUrl, bool isPageLink)
        {
            if (!string.IsNullOrEmpty(fileUrl))
            {
                fileUrl = !fileUrl.EndsWith("/") ? $"{fileUrl}/{fileName}" : $"{fileUrl}{fileName}";
            }
            else
            {
                fileUrl = $"{fileUrl}{fileName}";
            }

            if (!fileUrl.Contains("?") && !isPageLink)
            {
                fileUrl = Microsoft.JScript.GlobalObject.escape(fileUrl);

                if (fileUrl.Contains("%3A"))
                {
                    fileUrl = fileUrl.Replace("%3A", ":");
                }

                if (fileUrl.Contains(".aspx%23"))
                {
                    fileUrl = fileUrl.Replace("aspx%23", "aspx#");
                }
            }

            var httpRequest = HttpContext.Current.Request;

            // string _CKEditorName = httpRequest.QueryString["CKEditor"];
            var funcNum = httpRequest.QueryString["CKEditorFuncNum"];

            var errorMsg = string.Empty;

            funcNum = Regex.Replace(funcNum, @"[^0-9]", string.Empty, RegexOptions.None);

            return
                $"var E = window.top.opener;E.CKEDITOR.tools.callFunction({funcNum},'{fileUrl}','{errorMsg.Replace("'", "\\'")}') ;self.close();";
        }

        /// <summary>
        /// Gets the java script upload code.
        /// </summary>
        /// <param name="fileName">The file name.</param>
        /// <param name="fileUrl">The file url.</param>
        /// <returns>
        /// Returns the formatted java script block
        /// </returns>
        protected virtual string GetJsUploadCode(string fileName, string fileUrl)
        {
            fileUrl = string.Format(!fileUrl.EndsWith("/") ? "{0}/{1}" : "{0}{1}", fileUrl, fileName);

            var httpRequest = HttpContext.Current.Request;

            // var _CKEditorName = request.QueryString["CKEditor"];
            var funcNum = httpRequest.QueryString["CKEditorFuncNum"];

            var errorMsg = string.Empty;

            funcNum = Regex.Replace(funcNum, @"[^0-9]", string.Empty, RegexOptions.None);

            return
                $"var E = window.parent;E['CKEDITOR'].tools.callFunction({funcNum},'{Microsoft.JScript.GlobalObject.escape(fileUrl)}','{errorMsg.Replace("'", "\\'")}') ;";
        }

        /// <summary>
        /// Handles the Page Changed event of the Pager FileLinks control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        protected void PagerFileLinks_PageChanged(object sender, EventArgs e)
        {
            this.ShowFilesIn(this.lblCurrentDir.Text, true);

            // Reset selected file
            this.SetDefaultLinkTypeText();

            this.FileId.Text = null;
            this.lblFileName.Text = null;
        }

        /// <summary>
        /// Sorts the Files in ascending order
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        protected void SortAscendingClick(object sender, EventArgs e)
        {
            this.SortFilesDescending = false;

            this.SortAscending.CssClass = this.SortFilesDescending ? "ButtonNormal" : "ButtonSelected";
            this.SortDescending.CssClass = !this.SortFilesDescending ? "ButtonNormal" : "ButtonSelected";

            this.ShowFilesIn(this.lblCurrentDir.Text, true);

            // Reset selected file
            this.SetDefaultLinkTypeText();

            this.FileId.Text = null;
            this.lblFileName.Text = null;
        }

        /// <summary>
        /// Sorts the Files in descending order
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        protected void SortDescendingClick(object sender, EventArgs e)
        {
            this.SortFilesDescending = true;

            this.SortAscending.CssClass = this.SortFilesDescending ? "ButtonNormal" : "ButtonSelected";
            this.SortDescending.CssClass = !this.SortFilesDescending ? "ButtonNormal" : "ButtonSelected";

            this.ShowFilesIn(this.lblCurrentDir.Text, true);

            // Reset selected file
            this.SetDefaultLinkTypeText();

            this.FileId.Text = null;
            this.lblFileName.Text = null;
        }

        /// <summary>
        /// Raises the <see cref="E:System.Web.UI.Control.Init"/> event to initialize the page.
        /// </summary>
        /// <param name="e">An <see cref="T:System.EventArgs"/> that contains the event data.</param>
        protected override void OnInit(EventArgs e)
        {
            // CODEGEN: This call is required by the ASP.NET Web Form Designer.
            this.InitializeComponent();
            base.OnInit(e);
        }

        /// <summary>
        /// Handles the Load event of the Page control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        protected void Page_Load(object sender, EventArgs e)
        {
            this.SortAscending.CssClass = this.SortFilesDescending ? "ButtonNormal" : "ButtonSelected";
            this.SortDescending.CssClass = !this.SortFilesDescending ? "ButtonNormal" : "ButtonSelected";

            this.extensionWhiteList = HostController.Instance.GetString("FileExtensions").ToLower();

            if (!string.IsNullOrEmpty(this.request.QueryString["mode"]))
            {
                this.currentSettings.SettingMode = (SettingsMode)Enum.Parse(
                    typeof(SettingsMode),
                    this.request.QueryString["mode"]);
            }

            var providerConfiguration = ProviderConfiguration.GetProviderConfiguration("htmlEditor");
            var objProvider = (Provider)providerConfiguration.Providers[providerConfiguration.DefaultProvider];

            var settingsDictionary = Utility.GetEditorHostSettings();
            var portalRoles = new RoleController().GetRoles(this._portalSettings.PortalId);

            switch (this.currentSettings.SettingMode)
            {
                case SettingsMode.Default:
                    // Load Default Settings
                    this.currentSettings = SettingsUtil.GetDefaultSettings(
                        this._portalSettings,
                        this._portalSettings.HomeDirectoryMapPath,
                        objProvider.Attributes["ck_configFolder"],
                        portalRoles);
                    break;
                case SettingsMode.Portal:
                    this.currentSettings = SettingsUtil.LoadPortalOrPageSettings(
                        this._portalSettings,
                        this.currentSettings,
                        settingsDictionary,
                        $"DNNCKP#{this.request.QueryString["PortalID"]}#",
                        portalRoles);
                    break;
                case SettingsMode.Page:
                    this.currentSettings = SettingsUtil.LoadPortalOrPageSettings(
                        this._portalSettings,
                        this.currentSettings,
                        settingsDictionary,
                        $"DNNCKT#{this.request.QueryString["tabid"]}#",
                        portalRoles);
                    break;
                case SettingsMode.ModuleInstance:
                    this.currentSettings = SettingsUtil.LoadModuleSettings(
                        this._portalSettings,
                        this.currentSettings,
                        $"DNNCKMI#{this.request.QueryString["mid"]}#INS#{this.request.QueryString["ckId"]}#",
                        int.Parse(this.request.QueryString["mid"]),
                        portalRoles);
                    break;
            }

            // set current Upload file size limit
            this.currentSettings.UploadFileSizeLimit = SettingsUtil.GetCurrentUserUploadSize(
                this.currentSettings,
                this._portalSettings,
                HttpContext.Current.Request);

            // Set image extensionslist
            this.allowedImageExtensions.AddRange(this.currentSettings.AllowedImageExtensions.Split(','));

            for (var i = 0; i < this.allowedImageExtensions.Count; i++)
            {
                if (!this.extensionWhiteList.Contains(this.allowedImageExtensions[i]))
                {
                    this.allowedImageExtensions.RemoveAt(i);
                }
            }

            if (this.currentSettings.BrowserMode.Equals(Constants.Browser.StandardBrowser)
                && HttpContext.Current.Request.IsAuthenticated)
            {
                string command = null;

                try
                {
                    if (this.request.QueryString["Command"] != null)
                    {
                        command = this.request.QueryString["Command"];
                    }
                }
                catch (Exception)
                {
                    command = null;
                }

                try
                {
                    if (this.request.QueryString["Type"] != null)
                    {
                        this.browserModus = this.request.QueryString["Type"];
                        this.lblModus.Text = $"Browser-Modus: {this.browserModus}";

                        if (!this.IsPostBack)
                        {
                            this.GetAcceptedFileTypes();

                            this.title.InnerText = $"{this.lblModus.Text} - WatchersNET.FileBrowser";

                            this.AnchorList.Visible = this.currentSettings.UseAnchorSelector;
                            this.LabelAnchor.Visible = this.currentSettings.UseAnchorSelector;

                            this.ListViewState.Value = this.currentSettings.FileListViewMode.ToString();

                            // Set default link mode
                            switch (this.currentSettings.DefaultLinkMode)
                            {
                                case LinkMode.RelativeURL:
                                    this.rblLinkType.SelectedValue = "relLink";
                                    break;
                                case LinkMode.AbsoluteURL:
                                    this.rblLinkType.SelectedValue = "absLnk";
                                    break;
                                case LinkMode.RelativeSecuredURL:
                                    this.rblLinkType.SelectedValue = "lnkClick";
                                    break;
                                case LinkMode.AbsoluteSecuredURL:
                                    this.rblLinkType.SelectedValue = "lnkAbsClick";
                                    break;
                            }

                            switch (this.browserModus)
                            {
                                case "Link":
                                    this.BrowserMode.Visible = true;

                                    if (this.currentSettings.ShowPageLinksTabFirst)
                                    {
                                        this.BrowserMode.SelectedValue = "page";
                                        this.panLinkMode.Visible = false;
                                        this.panPageMode.Visible = true;

                                        this.TrackClicks.Visible = false;
                                        this.lblModus.Text = $"Browser-Modus: {$"Page {this.browserModus}"}";
                                        this.title.InnerText = $"{this.lblModus.Text} - WatchersNET.FileBrowser";

                                        this.RenderTabs();
                                    }
                                    else
                                    {
                                        this.BrowserMode.SelectedValue = "file";
                                        this.panPageMode.Visible = false;
                                    }

                                    break;
                                case "Image":
                                    this.BrowserMode.Visible = false;
                                    this.panPageMode.Visible = false;
                                    break;
                                case "Flash":
                                    this.BrowserMode.Visible = false;
                                    this.panPageMode.Visible = false;
                                    break;
                                default:
                                    this.BrowserMode.Visible = false;
                                    this.panPageMode.Visible = false;
                                    break;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    this.browserModus = null;
                }

                if (command != null)
                {
                    if (!command.Equals("FileUpload") && !command.Equals("FlashUpload")
                                                      && !command.Equals("ImageUpload")
                                                      && !command.Equals("ImageAutoUpload"))
                    {
                        return;
                    }

                    var uploadedFile = HttpContext.Current.Request.Files[HttpContext.Current.Request.Files.AllKeys[0]];

                    if (uploadedFile == null)
                    {
                        return;
                    }

                    if (command.Equals("ImageAutoUpload"))
                    {
                        // Upload Auto Image Upload
                        this.UploadAutoImageFile(uploadedFile);
                    }
                    else
                    {
                        // Upload QuickFile
                        this.UploadQuickFile(uploadedFile, command);
                    }
                }
                else
                {
                    if (!this.IsPostBack)
                    {
                        this.OverrideFile.Checked = this.currentSettings.OverrideFileOnUpload;

                        this.SetLanguage();

                        this.GetLanguageList();

                        var startFolder = this.StartingDir();

                        /*if (!Utility.IsInRoles(this._portalSettings.AdministratorRoleName, this._portalSettings))
                        {
                            // Hide physical file Path
                            this.lblCurrentDir.Visible = false;
                            this.lblCurrent.Visible = false;
                        }*/
                        this.FillFolderTree(startFolder);

                        var folderSelected = false;

                        if (!string.IsNullOrEmpty(ckFileUrl))
                        {
                            try
                            {
                                folderSelected = this.SelectFolderFile(ckFileUrl);
                                ckFileUrl = null;
                            }
                            catch (Exception)
                            {
                                folderSelected = false;
                                ckFileUrl = null;
                            }
                        }

                        if (!folderSelected)
                        {
                            this.lblCurrentDir.Text = startFolder.PhysicalPath;

                            this.ShowFilesIn(startFolder);
                        }
                    }

                    this.FillQualityPrecentages();
                }
            }
            else
            {
                var errorScript =
                    $"javascript:alert('{Localization.GetString("Error1.Text", this.ResXFile, this.LanguageCode)}');self.close();";

                this.Response.Write("<script type=\"text/javascript\">");
                this.Response.Write(errorScript);
                this.Response.Write("</script>");

                this.Response.End();
            }
        }

        /// <summary>
        /// Show Create New Folder Panel
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        protected void Create_Click(object sender, EventArgs e)
        {
            this.panCreate.Visible = true;

            if (this.panUploadDiv.Visible)
            {
                this.panUploadDiv.Visible = false;
            }

            if (this.panThumb.Visible)
            {
                this.panThumb.Visible = false;
            }
        }

        /// <summary>
        /// Synchronize Current Folder With the Database
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        protected void Syncronize_Click(object sender, EventArgs e)
        {
            this.SyncCurrentFolder();
        }

        /// <summary>
        /// Delete Selected File
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        protected void Delete_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(this.FileId.Text))
            {
                return;
            }

            var deleteFile = FileManager.Instance.GetFile(int.Parse(this.FileId.Text));

            var thumbFolder = Path.Combine(this.lblCurrentDir.Text, "_thumbs");

            var thumbPath = Path.Combine(thumbFolder, this.lblFileName.Text).Replace(
                this.lblFileName.Text.Substring(this.lblFileName.Text.LastIndexOf(".", StringComparison.Ordinal)),
                ".png");

            try
            {
                FileManager.Instance.DeleteFile(deleteFile);

                // Also Delete Thumbnail?);
                if (File.Exists(thumbPath))
                {
                    File.Delete(thumbPath);
                }
            }
            catch (Exception exception)
            {
                this.Response.Write("<script type=\"text/javascript\">");

                var message = exception.Message.Replace("'", string.Empty).Replace("\r\n", string.Empty)
                    .Replace("\n", string.Empty).Replace("\r", string.Empty);

                this.Response.Write($"javascript:alert('{this.Context.Server.HtmlEncode(message)}');");

                this.Response.Write("</script>");
            }
            finally
            {
                this.ShowFilesIn(this.lblCurrentDir.Text);

                this.SetDefaultLinkTypeText();

                this.FileId.Text = null;
                this.lblFileName.Text = null;
            }
        }

        /// <summary>
        /// Download selected File
        /// </summary>
        /// <param name="sender">
        /// The sender.
        /// </param>
        /// <param name="e">
        /// The EventArgs e.
        /// </param>
        protected void Download_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(this.FileId.Text))
            {
                return;
            }

            var downloadFile = FileManager.Instance.GetFile(int.Parse(this.FileId.Text));

            FileManager.Instance.WriteFileToResponse(downloadFile, ContentDisposition.Attachment);
        }

        /// <summary>
        /// Opens the Re-sizing Panel
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        protected void Resizer_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(this.lblFileName.Text))
            {
                return;
            }

            // Hide Link Panel and show Image Editor
            this.panThumb.Visible = true;
            this.panImagePreview.Visible = true;
            this.panImageEdHead.Visible = true;

            this.imgOriginal.Visible = true;

            this.cmdRotate.Visible = true;
            this.cmdCrop.Visible = true;
            this.cmdZoom.Visible = true;
            this.cmdResize2.Visible = false;

            this.panLinkMode.Visible = false;
            this.BrowserMode.Visible = false;

            this.lblResizeHeader.Text = Localization.GetString(
                "lblResizeHeader.Text",
                this.ResXFile,
                this.LanguageCode);
            this.title.InnerText = $"{this.lblResizeHeader.Text} - WatchersNET.FileBrowser";

            // Hide all Unwanted Elements from the Image Editor
            this.cmdClose.Visible = false;
            this.panInfo.Visible = false;

            this.panImageEditor.Visible = false;
            this.lblCropInfo.Visible = false;

            ////
            var filePath = Path.Combine(this.lblCurrentDir.Text, this.lblFileName.Text);

            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);

            this.txtThumbName.Text = $"{fileNameWithoutExtension}_resized";

            var extension = Path.GetExtension(filePath);
            extension = extension.TrimStart('.');

            var enable = this.allowedImageExtensions.Any(sAllowExt => sAllowExt.Equals(extension.ToLower()));

            if (!enable)
            {
                return;
            }

            var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            var image = Image.FromStream(fs);

            var script1 = new StringBuilder();

            // Show Preview Images
            this.imgOriginal.ImageUrl = MapUrl(filePath);
            this.imgResized.ImageUrl = MapUrl(filePath);

            var w = image.Width;
            var h = image.Height;

            var longestDimension = w > h ? w : h;
            var shortestDimension = w < h ? w : h;

            var factor = (float)longestDimension / shortestDimension;

            double newWidth = 400;
            double newHeight = 300 / factor;

            if (w < h)
            {
                newWidth = 400 / factor;
                newHeight = 300;
            }

            if (newWidth > image.Width)
            {
                newWidth = image.Width;
            }

            if (newHeight > image.Height)
            {
                newHeight = image.Height;
            }

            int defaultWidth, defaultHeight;

            if (this.currentSettings.ResizeWidth > 0)
            {
                defaultWidth = this.currentSettings.ResizeWidth;

                // Check if Default Value is greater the Image Value
                if (defaultWidth > image.Width)
                {
                    defaultWidth = image.Width;
                }
            }
            else
            {
                defaultWidth = (int)newWidth;
            }

            if (this.currentSettings.ResizeHeight > 0)
            {
                defaultHeight = this.currentSettings.ResizeHeight;

                // Check if Default Value is greater the Image Value
                if (defaultHeight > image.Height)
                {
                    defaultHeight = image.Height;
                }
            }
            else
            {
                defaultHeight = (int)newHeight;
            }

            this.txtHeight.Text = defaultHeight.ToString();
            this.txtWidth.Text = defaultWidth.ToString();

            this.imgOriginal.Height = (int)newHeight;
            this.imgOriginal.Width = (int)newWidth;

            this.imgResized.Height = defaultHeight;
            this.imgResized.Width = defaultWidth;

            this.imgOriginal.ToolTip = Localization.GetString("imgOriginal.Text", this.ResXFile, this.LanguageCode);
            this.imgOriginal.AlternateText = this.imgOriginal.ToolTip;

            this.imgResized.ToolTip = Localization.GetString("imgResized.Text", this.ResXFile, this.LanguageCode);
            this.imgResized.AlternateText = this.imgResized.ToolTip;

            script1.Append("ResizeMe('#imgResized', 360, 300);");

            //////////////
            script1.AppendFormat(
                "SetupSlider('#SliderWidth', 1, {0}, 1, 'horizontal', {1}, '#txtWidth');",
                image.Width,
                defaultWidth);
            script1.AppendFormat(
                "SetupSlider('#SliderHeight', 1, {0}, 1, 'vertical', {1}, '#txtHeight');",
                image.Height,
                defaultHeight);

            this.Page.ClientScript.RegisterStartupScript(this.GetType(), "SliderScript", script1.ToString(), true);

            image.Dispose();
        }

        /// <summary>
        /// Show Upload Controls
        /// </summary>
        /// <param name="sender">
        /// The sender.
        /// </param>
        /// <param name="e">
        /// The e.
        /// </param>
        protected void Upload_Click(object sender, EventArgs e)
        {
            this.panUploadDiv.Visible = true;

            if (this.panCreate.Visible)
            {
                this.panCreate.Visible = false;
            }

            if (this.panThumb.Visible)
            {
                this.panThumb.Visible = false;
            }
        }

        /// <summary>
        /// Shows the files in directory.
        /// </summary>
        /// <param name="directory">The directory.</param>
        /// <param name="pagerChanged">if set to <c>true</c> [pager changed].</param>
        protected void ShowFilesIn(string directory, bool pagerChanged = false)
        {
            var currentFolderInfo = Utility.ConvertFilePathToFolderInfo(directory, this._portalSettings);

            this.ShowFilesIn(currentFolderInfo, pagerChanged);
        }

        /// <summary>
        /// Formats a MapPath into relative MapUrl
        /// </summary>
        /// <param name="sPath">
        /// MapPath Input string
        /// </param>
        /// <returns>
        /// The output URL string
        /// </returns>
        private static string MapUrl(string sPath)
        {
            var appPath = HttpContext.Current.Server.MapPath("~");

            var url =
                $"{HttpContext.Current.Request.ApplicationPath + sPath.Replace(appPath, string.Empty).Replace("\\", "/")}";

            return url;
        }

        /// <summary>
        /// Get File Name without .resources extension
        /// </summary>
        /// <param name="fileName">File Name</param>
        /// <returns>Cleaned File Name</returns>
        private static string GetFileNameCleaned(string fileName)
        {
            return fileName.Replace(".resources", string.Empty);
        }

        /// <summary>
        /// The get encoder.
        /// </summary>
        /// <param name="format">
        /// The format.
        /// </param>
        /// <returns>
        /// The Encoder
        /// </returns>
        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            var codecs = ImageCodecInfo.GetImageDecoders();

            return codecs.FirstOrDefault(codec => codec.FormatID == format.Guid);
        }

        /*
        /// <summary>
        ///  Get an Resized Image
        /// </summary>
        /// <param name="imgPhoto">
        /// Original Image
        /// </param>
        /// <param name="ts">
        /// New Size
        /// </param>
        /// <returns>
        /// The Resized Image
        /// </returns>
        private static Image GetResizedImage(Image imgPhoto, Size ts)
        {
            int sourceWidth = imgPhoto.Width;
            int sourceHeight = imgPhoto.Height;
            const int sourceX = 0;
            const int sourceY = 0;
            int destX = 0;
            int destY = 0;

            float nPercent;

            bool sourceVertical = sourceWidth < sourceHeight;
            bool targetVeritcal = ts.Width < ts.Height;

            if (sourceVertical != targetVeritcal)
            {
                int t = ts.Width;
                ts.Width = ts.Height;
                ts.Height = t;
            }

            float nPercentW = ts.Width / (float)sourceWidth;
            float nPercentH = ts.Height / (float)sourceHeight;

            if (nPercentH < nPercentW)
            {
                nPercent = nPercentH;
                destX = Convert.ToInt16((ts.Width - (sourceWidth * nPercent)) / 2);
            }
            else
            {
                nPercent = nPercentW;
                destY = Convert.ToInt16((ts.Height - (sourceHeight * nPercent)) / 2);
            }

            int destWidth = (int)(sourceWidth * nPercent);
            int destHeight = (int)(sourceHeight * nPercent);

            Bitmap bmPhoto = new Bitmap(ts.Width, ts.Height, PixelFormat.Format24bppRgb);

            bmPhoto.MakeTransparent(Color.Transparent);

            bmPhoto.SetResolution(imgPhoto.HorizontalResolution, imgPhoto.VerticalResolution);

            Graphics grPhoto = Graphics.FromImage(bmPhoto);

            // grPhoto.Clear(Color.White);
            grPhoto.Clear(Color.Transparent);

            grPhoto.InterpolationMode = InterpolationMode.HighQualityBicubic;

            grPhoto.DrawImage(
                imgPhoto,
                new Rectangle(destX, destY, destWidth, destHeight),
                new Rectangle(sourceX, sourceY, sourceWidth, sourceHeight),
                GraphicsUnit.Pixel);

            grPhoto.Dispose();
            return bmPhoto;
        }*/

        /// <summary>
        /// Check if Folder is a Secure Folder
        /// </summary>
        /// <param name="folderId">The folder id.</param>
        /// <returns>
        /// Returns if folder is Secure
        /// </returns>
        private FolderController.StorageLocationTypes GetStorageLocationType(int folderId)
        {
            FolderController.StorageLocationTypes storagelocationType;

            try
            {
                var folderInfo = FolderManager.Instance.GetFolder(folderId);

                storagelocationType = (FolderController.StorageLocationTypes)folderInfo.StorageLocation;
            }
            catch (Exception)
            {
                storagelocationType = FolderController.StorageLocationTypes.InsecureFileSystem;
            }

            return storagelocationType;
        }

        /// <summary>
        /// Check if Folder is a Secure Folder
        /// </summary>
        /// <param name="folderPath">The folder path.</param>
        /// <returns>
        /// Returns if folder is Secure
        /// </returns>
        private FolderController.StorageLocationTypes GetStorageLocationType(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                return FolderController.StorageLocationTypes.InsecureFileSystem;
            }

            try
            {
                folderPath = folderPath.Substring(this._portalSettings.HomeDirectoryMapPath.Length).Replace("\\", "/");
            }
            catch (Exception)
            {
                folderPath = folderPath.Replace("\\", "/");
            }

            FolderController.StorageLocationTypes storagelocationType;

            try
            {
                var folderInfo = FolderManager.Instance.GetFolder(this._portalSettings.PortalId, folderPath);

                storagelocationType = (FolderController.StorageLocationTypes)folderInfo.StorageLocation;
            }
            catch (Exception)
            {
                storagelocationType = FolderController.StorageLocationTypes.InsecureFileSystem;
            }

            return storagelocationType;
        }

        /// <summary>
        /// Hide Create Items if User has no write access to the Current Folder
        /// </summary>
        /// <param name="folderId">The folder id to check</param>
        /// <param name="isFileSelected">if set to <c>true</c> [is file selected].</param>
        private void CheckFolderAccess(int folderId, bool isFileSelected)
        {
            var hasWriteAccess = Utility.CheckIfUserHasFolderWriteAccess(folderId, this._portalSettings);

            this.cmdUpload.Enabled = hasWriteAccess;
            this.cmdCreate.Enabled = hasWriteAccess;
            this.Syncronize.Enabled = hasWriteAccess;
            this.cmdDelete.Enabled = hasWriteAccess && isFileSelected;
            this.cmdResizer.Enabled = hasWriteAccess && isFileSelected;
            this.cmdDownload.Enabled = isFileSelected;

            this.cmdUpload.CssClass = hasWriteAccess ? "LinkNormal" : "LinkDisabled";
            this.cmdCreate.CssClass = hasWriteAccess ? "LinkNormal" : "LinkDisabled";
            this.Syncronize.CssClass = hasWriteAccess ? "LinkNormal" : "LinkDisabled";
            this.cmdDelete.CssClass = hasWriteAccess && isFileSelected ? "LinkNormal" : "LinkDisabled";
            this.cmdResizer.CssClass = hasWriteAccess && isFileSelected ? "LinkNormal" : "LinkDisabled";
            this.cmdDownload.CssClass = isFileSelected ? "LinkNormal" : "LinkDisabled";
        }

        /// <summary>
        /// Set Folder Permission
        /// </summary>
        /// <param name="folderId">The Folder Id.</param>
        private void SetFolderPermission(int folderId)
        {
            var folder = FolderManager.Instance.GetFolder(folderId);

            this.SetFolderPermission(folder);
        }

        /// <summary>
        /// Set Folder Permission
        /// </summary>
        /// <param name="folderInfo">The folder info.</param>
        private void SetFolderPermission(IFolderInfo folderInfo)
        {
            FolderManager.Instance.CopyParentFolderPermissions(folderInfo);
        }

        /// <summary>
        /// Set Folder Permission for the Current User
        /// </summary>
        /// <param name="folderInfo">The folder info.</param>
        /// <param name="currentUserInfo">The current user info.</param>
        private void SetUserFolderPermission(IFolderInfo folderInfo, UserInfo currentUserInfo)
        {
            if (FolderPermissionController.CanManageFolder((FolderInfo)folderInfo))
            {
                return;
            }

            foreach (var folderPermission in from PermissionInfo permission in
                                                 PermissionController.GetPermissionsByFolder()
                                             where permission.PermissionKey.ToUpper() == "READ"
                                                   || permission.PermissionKey.ToUpper() == "WRITE"
                                                   || permission.PermissionKey.ToUpper() == "BROWSE"
                                             select new FolderPermissionInfo(permission)
                                                        {
                                                            FolderID = folderInfo.FolderID,
                                                            UserID = currentUserInfo.UserID,
                                                            RoleID = Null.NullInteger,
                                                            AllowAccess = true
                                                        })
            {
                folderInfo.FolderPermissions.Add(folderPermission);
            }

            FolderPermissionController.SaveFolderPermissions((FolderInfo)folderInfo);
        }

        /// <summary>
        /// Sets the default link type text.
        /// </summary>
        private void SetDefaultLinkTypeText()
        {
            this.rblLinkType.Items[0].Text = Localization.GetString("relLnk.Text", this.ResXFile, this.LanguageCode);
            this.rblLinkType.Items[1].Text = Localization.GetString("absLnk.Text", this.ResXFile, this.LanguageCode);

            if (this.rblLinkType.Items.Count <= 2)
            {
                return;
            }

            this.rblLinkType.Items[2].Text = Localization.GetString("lnkClick.Text", this.ResXFile, this.LanguageCode);
            this.rblLinkType.Items[3].Text = Localization.GetString(
                "lnkAbsClick.Text",
                this.ResXFile,
                this.LanguageCode);
        }

        /// <summary>
        /// Fill the Folder TreeView with all (Sub)Directories
        /// </summary>
        /// <param name="currentFolderInfo">The current folder information.</param>
        private void FillFolderTree(IFolderInfo currentFolderInfo)
        {
            this.FoldersTree.Nodes.Clear();

            var dirInfo = new DirectoryInfo(currentFolderInfo.PhysicalPath);

            var folderNode = new TreeNode
                                 {
                                     Text = dirInfo.Name, Value = dirInfo.FullName, ImageUrl = "Images/folder.gif"
                                 };

            /*switch (this.GetStorageLocationType(currentFolderInfo.PhysicalPath))
            {
                case FolderController.StorageLocationTypes.SecureFileSystem:
                    {
                       folderNode.ImageUrl = "Images/folderLocked.gif";
                    }

                    break;
                case FolderController.StorageLocationTypes.DatabaseSecure:
                    {
                        folderNode.ImageUrl = "Images/folderdb.gif";
                    }

                    break;
            }*/

            this.FoldersTree.Nodes.Add(folderNode);

            var folders = FolderManager.Instance.GetFolders(currentFolderInfo);

            foreach (var node in folders.Cast<FolderInfo>().Select(this.RenderFolder).Where(node => node != null))
            {
                folderNode.ChildNodes.Add(node);
            }
        }

        /// <summary>
        /// Fill Quality Values 1-100 %
        /// </summary>
        private void FillQualityPrecentages()
        {
            for (var i = 00; i < 101; i++)
            {
                this.dDlQuality.Items.Add(new ListItem { Text = i.ToString(), Value = i.ToString() });
            }

            this.dDlQuality.Items[100].Selected = true;
        }

        /// <summary>
        /// The get portal settings.
        /// </summary>
        /// <returns>
        /// Current Portal Settings
        /// </returns>
        private PortalSettings GetPortalSettings()
        {
            int tabId = 0, portalId = 0;

            PortalSettings portalSettings;

            try
            {
                if (this.request.QueryString["tabid"] != null)
                {
                    tabId = int.Parse(this.request.QueryString["tabid"]);
                }

                if (this.request.QueryString["PortalID"] != null)
                {
                    portalId = int.Parse(this.request.QueryString["PortalID"]);
                }

                var domainName = Globals.GetDomainName(this.Request, true);

                var portalAlias = PortalAliasController.GetPortalAliasByPortal(portalId, domainName);

                var objPortalAliasInfo = PortalAliasController.Instance.GetPortalAlias(portalAlias);

                portalSettings = new PortalSettings(tabId, objPortalAliasInfo);
            }
            catch (Exception)
            {
                portalSettings = (PortalSettings)HttpContext.Current.Items["PortalSettings"];
            }

            return portalSettings;
        }

        /// <summary>
        /// Get the Current Starting Directory
        /// </summary>
        /// <returns>
        /// Returns the Starting Directory.
        /// </returns>
        private IFolderInfo StartingDir()
        {
            IFolderInfo startingFolderInfo = null;

            if (!this.currentSettings.BrowserRootDirId.Equals(-1))
            {
                var rootFolder = FolderManager.Instance.GetFolder(this.currentSettings.BrowserRootDirId);

                if (rootFolder != null)
                {
                    startingFolderInfo = rootFolder;
                }
            }
            else
            {
                startingFolderInfo = FolderManager.Instance.GetFolder(this._portalSettings.PortalId, string.Empty);
            }

            if (Utility.IsInRoles(this._portalSettings.AdministratorRoleName, this._portalSettings))
            {
                return startingFolderInfo;
            }

            if (this.currentSettings.SubDirs)
            {
                startingFolderInfo = this.GetUserFolderInfo(startingFolderInfo.PhysicalPath);
            }
            else
            {
                return startingFolderInfo;
            }

            if (Directory.Exists(startingFolderInfo.PhysicalPath))
            {
                return startingFolderInfo;
            }

            var folderStart = startingFolderInfo.PhysicalPath;

            folderStart = folderStart.Substring(this._portalSettings.HomeDirectoryMapPath.Length).Replace("\\", "/");

            startingFolderInfo = FolderManager.Instance.AddFolder(this._portalSettings.PortalId, folderStart);

            Directory.CreateDirectory(startingFolderInfo.PhysicalPath);

            this.SetFolderPermission(startingFolderInfo);

            return startingFolderInfo;
        }

        /// <summary>
        /// Gets the user folder Info.
        /// </summary>
        /// <param name="startingDir">The Starting Directory.</param>
        /// <returns>Returns the user folder path</returns>
        private IFolderInfo GetUserFolderInfo(string startingDir)
        {
            IFolderInfo userFolderInfo;

            var userFolderPath = Path.Combine(startingDir, "userfiles");

            // Create "userfiles" folder if not exists
            if (!Directory.Exists(userFolderPath))
            {
                var folderStart = userFolderPath;

                folderStart = folderStart.Substring(this._portalSettings.HomeDirectoryMapPath.Length)
                    .Replace("\\", "/");

                userFolderInfo = FolderManager.Instance.AddFolder(this._portalSettings.PortalId, folderStart);

                Directory.CreateDirectory(userFolderPath);

                this.SetFolderPermission(userFolderInfo);
            }

            // Create user folder based on the user id
            userFolderPath = Path.Combine(userFolderPath, $"{UserController.Instance.GetCurrentUserInfo().UserID}\\");

            if (!Directory.Exists(userFolderPath))
            {
                var folderStart = userFolderPath;

                folderStart = folderStart.Substring(this._portalSettings.HomeDirectoryMapPath.Length)
                    .Replace("\\", "/");

                userFolderInfo = FolderManager.Instance.AddFolder(this._portalSettings.PortalId, folderStart);

                Directory.CreateDirectory(userFolderPath);

                this.SetFolderPermission(userFolderInfo);

                this.SetUserFolderPermission(userFolderInfo, UserController.Instance.GetCurrentUserInfo());
            }
            else
            {
                userFolderInfo = Utility.ConvertFilePathToFolderInfo(userFolderPath, this._portalSettings);

                // make sure the user has the correct permissions set
                this.SetUserFolderPermission(userFolderInfo, UserController.Instance.GetCurrentUserInfo());
            }

            return userFolderInfo;
        }

        /// <summary>
        /// Required method for Designer support - do not modify
        ///   the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this._portalSettings = this.GetPortalSettings();

            this.cmdCancel.Click += this.Cancel_Click;
            this.cmdUploadNow.Click += this.UploadNow_Click;
            this.cmdUploadCancel.Click += this.UploadCancel_Click;
            this.cmdCreateFolder.Click += this.CreateFolder_Click;
            this.cmdCreateCancel.Click += this.CreateCancel_Click;
            this.cmdResizeCancel.Click += this.ResizeCancel_Click;
            this.cmdResizeNow.Click += this.ResizeNow_Click;
            this.cmdRotate.Click += this.Rotate_Click;
            this.cmdCrop.Click += this.Rotate_Click;
            this.cmdZoom.Click += this.Rotate_Click;
            this.cmdResize2.Click += this.Resizer_Click;
            this.cmdCropCancel.Click += this.ResizeCancel_Click;
            this.cmdCropNow.Click += this.CropNow_Click;

            this.BrowserMode.SelectedIndexChanged += this.BrowserMode_SelectedIndexChanged;
            this.dnntreeTabs.SelectedNodeChanged += this.TreeTabs_NodeClick;
            this.rblLinkType.SelectedIndexChanged += this.LinkType_SelectedIndexChanged;

            // this.FoldersTree.SelectedNodeChanged += new EventHandler(FoldersTree_SelectedNodeChanged);
            this.FoldersTree.SelectedNodeChanged += this.FoldersTree_NodeClick;

            this.FilesList.ItemCommand += this.FilesList_ItemCommand;
        }

        /// <summary>
        /// Load Favicon from Current Portal Home Directory
        /// </summary>
        private void LoadFavIcon()
        {
            if (!File.Exists(Path.Combine(this._portalSettings.HomeDirectoryMapPath, "favicon.ico")))
            {
                return;
            }

            var faviconUrl = Path.Combine(this._portalSettings.HomeDirectory, "favicon.ico");

            var objLink = new HtmlGenericSelfClosing("link");

            objLink.Attributes["rel"] = "shortcut icon";
            objLink.Attributes["href"] = faviconUrl;

            this.favicon.Controls.Add(objLink);
        }

        /// <summary>
        /// Render all Directories and sub directories recursive
        /// </summary>
        /// <param name="folderInfo">The folder Info.</param>
        /// <returns>
        /// TreeNode List
        /// </returns>
        private TreeNode RenderFolder(FolderInfo folderInfo)
        {
            if (!FolderPermissionController.CanViewFolder(folderInfo))
            {
                return null;
            }

            var folder = new TreeNode
                               {
                                   Text = folderInfo.FolderName,
                                   Value = folderInfo.PhysicalPath,
                                   ImageUrl = "Images/folder.gif",
                                   ToolTip = folderInfo.FolderID.ToString()
                               };

            switch (folderInfo.StorageLocation)
            {
                case (int)FolderController.StorageLocationTypes.SecureFileSystem:
                    folder.ImageUrl = "Images/folderLocked.gif";
                    break;
                case (int)FolderController.StorageLocationTypes.DatabaseSecure:
                    folder.ImageUrl = "Images/folderdb.gif";
                    break;
            }

            var folders = FolderManager.Instance.GetFolders(folderInfo).ToList();

            if (!folders.Any())
            {
                return folder;
            }

            foreach (var node in folders.Cast<FolderInfo>().Select(this.RenderFolder).Where(node => node != null))
            {
                switch (this.GetStorageLocationType(Convert.ToInt32(node.ToolTip)))
                {
                    case FolderController.StorageLocationTypes.SecureFileSystem:
                        {
                            node.ImageUrl = "Images/folderLocked.gif";
                        }

                        break;
                    case FolderController.StorageLocationTypes.DatabaseSecure:
                        {
                            node.ImageUrl = "Images/folderdb.gif";
                        }

                        break;
                }

                folder.ChildNodes.Add(node);
            }

            return folder;
        }

        /// <summary>
        /// Render all Tabs including Child Tabs
        /// </summary>
        /// <param name="nodeParent">
        /// Parent Node(Tab)
        /// </param>
        /// <param name="parentTabId">
        /// Parent Tab ID
        /// </param>
        private void RenderTabLevels(TreeNode nodeParent, int parentTabId)
        {
            foreach (var objTab in TabController.GetPortalTabs(
                this._portalSettings.PortalId,
                -1,
                false,
                null,
                true,
                false,
                true,
                true,
                false))
            {
                if (!objTab.ParentId.Equals(parentTabId))
                {
                    continue;
                }

                var nodeTab = new TreeNode();

                if (nodeParent != null)
                {
                    nodeParent.ChildNodes.Add(nodeTab);
                }
                else
                {
                    this.dnntreeTabs.Nodes.Add(nodeTab);
                }

                nodeTab.Text = objTab.TabName;
                nodeTab.Value = objTab.TabID.ToString();
                nodeTab.ImageUrl = "Images/Page.gif";

                // nodeTab.ExpandedImageUrl = "Images/folderOpen.gif";
                if (!string.IsNullOrEmpty(objTab.IconFile))
                {
                    nodeTab.ImageUrl = this.ResolveUrl(objTab.IconFile);
                }

                this.RenderTabLevels(nodeTab, objTab.TabID);
            }
        }

        /// <summary>
        /// Gets the language list, and sets the default locale if Content Localization is Enabled
        /// </summary>
        private void GetLanguageList()
        {
            foreach (var languageListItem in new LocaleController().GetLocales(this._portalSettings.PortalId).Values
                .Select(language => new ListItem { Text = language.Text, Value = language.Code }))
            {
                this.LanguageList.Items.Add(languageListItem);
            }

            if (this.LanguageList.Items.Count.Equals(1))
            {
                this.LanguageRow.Visible = false;
            }
            else
            {
                // Set default locale and remove no locale if Content Localization is Enabled
                if (!this._portalSettings.ContentLocalizationEnabled)
                {
                    return;
                }

                var currentTab = new TabController().GetTab(
                    int.Parse(this.request.QueryString["tabid"]),
                    this._portalSettings.PortalId,
                    false);

                if (currentTab == null || string.IsNullOrEmpty(currentTab.CultureCode))
                {
                    return;
                }

                this.LanguageList.Items.RemoveAt(0);

                var currentTabCultureItem = this.LanguageList.Items.FindByValue(currentTab.CultureCode);

                if (currentTabCultureItem != null)
                {
                    currentTabCultureItem.Selected = true;
                }
            }
        }

        /// <summary>
        /// Load the Portal Tabs for the Page Links TreeView Selector
        /// </summary>
        private void RenderTabs()
        {
            if (this.dnntreeTabs.Nodes.Count > 0)
            {
                return;
            }

            this.RenderTabLevels(null, -1);
        }

        /// <summary>
        /// Scroll to a Selected File or Uploaded File
        /// </summary>
        /// <param name="elementId">
        /// The element Id.
        /// </param>
        private void ScrollToSelectedFile(string elementId)
        {
            var script1 = new StringBuilder();

            script1.AppendFormat("document.getElementById('{0}').scrollIntoView();", elementId);

            this.Page.ClientScript.RegisterStartupScript(
                this.GetType(),
                $"ScrollToSelected{Guid.NewGuid()}",
                script1.ToString(),
                true);
        }

        /// <summary>
        /// Select a folder and the file inside the Browser
        /// </summary>
        /// <param name="fileUrl">
        /// The file url.
        /// </param>
        /// <returns>
        /// if folder was selected
        /// </returns>
        private bool SelectFolderFile(string fileUrl)
        {
            var fileName = fileUrl.Substring(fileUrl.LastIndexOf("/", StringComparison.Ordinal) + 1);

            if (fileName.StartsWith("LinkClick") || fileUrl.StartsWith("http:") || fileUrl.StartsWith("https:")
                || fileUrl.StartsWith("mailto:"))
            {
                ckFileUrl = null;
                return false;
            }

            var selectedDir = this.MapPath(fileUrl).Replace(fileName, string.Empty);

            if (!Directory.Exists(selectedDir))
            {
                ckFileUrl = null;
                return false;
            }

            this.lblCurrentDir.Text = selectedDir;

            var newDir = this.lblCurrentDir.Text;

            var newFolder = this.FoldersTree.FindNode(newDir);

            if (newFolder != null)
            {
                newFolder.Selected = true;
                newFolder.Expand();
                newFolder.Expanded = true;
            }

            this.ShowFilesIn(newDir);

            this.GoToSelectedFile(fileName);

            return true;
        }

        /// <summary>
        /// JS Code that gets the selected File Url
        /// </summary>
        private void GetSelectedImageOrLink()
        {
            var scriptSelected = new StringBuilder();

            scriptSelected.Append("var parentCKEDITOR = window.top.opener.CKEDITOR;");

            scriptSelected.Append("if (typeof(parentCKEDITOR) !== 'undefined') {");
            scriptSelected.AppendFormat(
                "var selection = parentCKEDITOR.instances.{0}.getSelection(),",
                this.request.QueryString["CKEditor"]);
            scriptSelected.Append("element = selection.getStartElement();");

            scriptSelected.Append("if (element !== null) {");

            scriptSelected.Append("if( element.getName()  == 'img')");
            scriptSelected.Append("{");

            scriptSelected.Append("var imageUrl = element.getAttribute('src');");

            scriptSelected.Append(
                "if (element.getAttribute('src') && imageUrl.indexOf('LinkClick') == -1 && imageUrl.indexOf('http:') == -1 && imageUrl.indexOf('https:') == -1) {");
            scriptSelected.Append(
                "jQuery.PageMethod('Browser.aspx', 'SetFile', function(message){if (location.href.indexOf('reload')==-1) location.replace(location.href+'&reload=true');}, null, 'fileUrl', imageUrl);");

            scriptSelected.Append("} else {");
            scriptSelected.Append(
                "if (location.href.indexOf('reload')==-1) location.replace(location.href+'&reload=true');");

            scriptSelected.Append("} }");
            scriptSelected.Append("else if (element.getName() == 'a')");
            scriptSelected.Append("{");

            scriptSelected.Append("var fileUrl = element.getAttribute('href');");

            scriptSelected.Append(
                "if (element.getAttribute('href') && fileUrl.indexOf('LinkClick') == -1 && fileUrl.indexOf('http:') == -1 && fileUrl.indexOf('https:') == -1) {");

            scriptSelected.Append(
                "jQuery.PageMethod('Browser.aspx', 'SetFile', function(message){if (location.href.indexOf('reload')==-1) location.replace(location.href+'&reload=true');}, null, 'fileUrl', fileUrl);");
            scriptSelected.Append("} else {");

            scriptSelected.Append(
                "if (location.href.indexOf('reload')==-1) location.replace(location.href+'&reload=true');");

            scriptSelected.Append("} }");

            scriptSelected.Append("}");

            scriptSelected.Append("}");

            this.Page.ClientScript.RegisterStartupScript(
                this.GetType(),
                "GetSelectedImageLink",
                scriptSelected.ToString(),
                true);
        }

        /// <summary>
        /// Set Language for all Controls on this Page
        /// </summary>
        private void SetLanguage()
        {
            // Buttons
            this.cmdResizeCancel.Text = Localization.GetString(
                "cmdResizeCancel.Text",
                this.ResXFile,
                this.LanguageCode);
            this.cmdResizeNow.Text = Localization.GetString("cmdResizeNow.Text", this.ResXFile, this.LanguageCode);
            this.cmdUploadCancel.Text = Localization.GetString(
                "cmdUploadCancel.Text",
                this.ResXFile,
                this.LanguageCode);
            this.cmdCancel.Text = Localization.GetString("cmdCancel.Text", this.ResXFile, this.LanguageCode);
            this.cmdClose.Text = Localization.GetString("cmdClose.Text", this.ResXFile, this.LanguageCode);
            this.cmdCreateFolder.Text = Localization.GetString(
                "cmdCreateFolder.Text",
                this.ResXFile,
                this.LanguageCode);
            this.cmdCreateCancel.Text = Localization.GetString(
                "cmdCreateCancel.Text",
                this.ResXFile,
                this.LanguageCode);
            this.cmdCrop.Text = Localization.GetString("cmdCrop.Text", this.ResXFile, this.LanguageCode);
            this.cmdZoom.Text = Localization.GetString("cmdZoom.Text", this.ResXFile, this.LanguageCode);
            this.cmdRotate.Text = Localization.GetString("cmdRotate.Text", this.ResXFile, this.LanguageCode);
            this.cmdResize2.Text = Localization.GetString("cmdResize2.Text", this.ResXFile, this.LanguageCode);
            this.cmdCropNow.Text = Localization.GetString("cmdCropNow.Text", this.ResXFile, this.LanguageCode);
            this.cmdCropCancel.Text = Localization.GetString("cmdCropCancel.Text", this.ResXFile, this.LanguageCode);

            // Labels
            this.lblConFiles.Text = Localization.GetString("lblConFiles.Text", this.ResXFile, this.LanguageCode);
            this.lblCurrent.Text = Localization.GetString("lblCurrent.Text", this.ResXFile, this.LanguageCode);
            this.lblSubDirs.Text = Localization.GetString("lblSubDirs.Text", this.ResXFile, this.LanguageCode);
            this.lblUrlType.Text = Localization.GetString("lblUrlType.Text", this.ResXFile, this.LanguageCode);
            this.rblLinkType.ToolTip = Localization.GetString("lblUrlType.Text", this.ResXFile, this.LanguageCode);
            this.lblChoosetab.Text = Localization.GetString("lblChoosetab.Text", this.ResXFile, this.LanguageCode);
            this.lblHeight.Text = Localization.GetString("lblHeight.Text", this.ResXFile, this.LanguageCode);
            this.lblWidth.Text = Localization.GetString("lblWidth.Text", this.ResXFile, this.LanguageCode);
            this.lblThumbName.Text = Localization.GetString("lblThumbName.Text", this.ResXFile, this.LanguageCode);
            this.lblImgQuality.Text = Localization.GetString("lblImgQuality.Text", this.ResXFile, this.LanguageCode);
            this.lblResizeHeader.Text = Localization.GetString(
                "lblResizeHeader.Text",
                this.ResXFile,
                this.LanguageCode);
            this.lblOtherTools.Text = Localization.GetString("lblOtherTools.Text", this.ResXFile, this.LanguageCode);
            this.lblCropImageName.Text = Localization.GetString("lblThumbName.Text", this.ResXFile, this.LanguageCode);
            this.lblCropInfo.Text = Localization.GetString("lblCropInfo.Text", this.ResXFile, this.LanguageCode);
            this.lblShowPreview.Text = Localization.GetString("lblShowPreview.Text", this.ResXFile, this.LanguageCode);
            this.lblClearPreview.Text = Localization.GetString(
                "lblClearPreview.Text",
                this.ResXFile,
                this.LanguageCode);
            this.lblOriginal.Text = Localization.GetString("lblOriginal.Text", this.ResXFile, this.LanguageCode);
            this.lblPreview.Text = Localization.GetString("lblPreview.Text", this.ResXFile, this.LanguageCode);
            this.lblNewFoldName.Text = Localization.GetString("lblNewFoldName.Text", this.ResXFile, this.LanguageCode);
            this.LabelAnchor.Text = Localization.GetString("LabelAnchor.Text", this.ResXFile, this.LanguageCode);
            this.NewFolderTitle.Text = Localization.GetString("cmdCreate.Text", this.ResXFile, this.LanguageCode);
            this.UploadTitle.Text = Localization.GetString("cmdUpload.Text", this.ResXFile, this.LanguageCode);
            this.AddFiles.Text = Localization.GetString("AddFiles.Text", this.ResXFile, this.LanguageCode);
            this.Wait.Text = Localization.GetString("Wait.Text", this.ResXFile, this.LanguageCode);
            this.WaitMessage.Text = Localization.GetString("WaitMessage.Text", this.ResXFile, this.LanguageCode);
            this.ExtraTabOptions.Text = Localization.GetString(
                "ExtraTabOptions.Text",
                this.ResXFile,
                this.LanguageCode);
            this.LabelTabLanguage.Text = Localization.GetString(
                "LabelTabLanguage.Text",
                this.ResXFile,
                this.LanguageCode);

            this.MaximumUploadSizeInfo.Text = string.Format(
                Localization.GetString("FileSizeRestriction", this.ResXFile, this.LanguageCode),
                this.MaxUploadSize / (1024 * 1024),
                this.AcceptFileTypes.Replace("|", ","));

            // RadioButtonList
            this.BrowserMode.Items[0].Text = Localization.GetString("FileLink.Text", this.ResXFile, this.LanguageCode);
            this.BrowserMode.Items[1].Text = Localization.GetString("PageLink.Text", this.ResXFile, this.LanguageCode);

            // DropDowns
            this.LanguageList.Items[0].Text = Localization.GetString("None.Text", this.ResXFile, this.LanguageCode);
            this.AnchorList.Items[0].Text = Localization.GetString("None.Text", this.ResXFile, this.LanguageCode);

            // CheckBoxes
            this.chkAspect.Text = Localization.GetString("chkAspect.Text", this.ResXFile, this.LanguageCode);
            this.chkHumanFriendy.Text = Localization.GetString(
                "chkHumanFriendy.Text",
                this.ResXFile,
                this.LanguageCode);
            this.TrackClicks.Text = Localization.GetString("TrackClicks.Text", this.ResXFile, this.LanguageCode);
            this.OverrideFile.Text = Localization.GetString("OverrideFile.Text", this.ResXFile, this.LanguageCode);

            // LinkButtons (with Image)
            this.Syncronize.Text =
                $"<img src=\"Images/SyncFolder.png\" alt=\"{Localization.GetString("Syncronize.Text", this.ResXFile, this.LanguageCode)}\" title=\"{Localization.GetString("Syncronize.Help", this.ResXFile, this.LanguageCode)}\" />";
            this.Syncronize.ToolTip = Localization.GetString("Syncronize.Help", this.ResXFile, this.LanguageCode);

            this.cmdCreate.Text =
                $"<img src=\"Images/CreateFolder.png\" alt=\"{Localization.GetString("cmdCreate.Text", this.ResXFile, this.LanguageCode)}\" title=\"{Localization.GetString("cmdCreate.Help", this.ResXFile, this.LanguageCode)}\" />";
            this.cmdCreate.ToolTip = Localization.GetString("cmdCreate.Help", this.ResXFile, this.LanguageCode);

            this.cmdDownload.Text =
                $"<img src=\"Images/DownloadButton.png\" alt=\"{Localization.GetString("cmdDownload.Text", this.ResXFile, this.LanguageCode)}\" title=\"{Localization.GetString("cmdDownload.Help", this.ResXFile, this.LanguageCode)}\" />";
            this.cmdDownload.ToolTip = Localization.GetString("cmdDownload.Help", this.ResXFile, this.LanguageCode);

            this.cmdUpload.Text =
                $"<img src=\"Images/UploadButton.png\" alt=\"{Localization.GetString("cmdUpload.Text", this.ResXFile, this.LanguageCode)}\" title=\"{Localization.GetString("cmdUpload.Help", this.ResXFile, this.LanguageCode)}\" />";
            this.cmdUpload.ToolTip = Localization.GetString("cmdUpload.Help", this.ResXFile, this.LanguageCode);

            this.cmdDelete.Text =
                $"<img src=\"Images/DeleteFile.png\" alt=\"{Localization.GetString("cmdDelete.Text", this.ResXFile, this.LanguageCode)}\" title=\"{Localization.GetString("cmdDelete.Help", this.ResXFile, this.LanguageCode)}\" />";
            this.cmdDelete.ToolTip = Localization.GetString("cmdDelete.Help", this.ResXFile, this.LanguageCode);

            this.cmdResizer.Text =
                $"<img src=\"Images/ResizeImage.png\" alt=\"{Localization.GetString("cmdResizer.Text", this.ResXFile, this.LanguageCode)}\" title=\"{Localization.GetString("cmdResizer.Help", this.ResXFile, this.LanguageCode)}\" />";
            this.cmdResizer.ToolTip = Localization.GetString("cmdResizer.Help", this.ResXFile, this.LanguageCode);

            const string SwitchContent =
                "<a class=\"Switch{0}\" onclick=\"javascript: SwitchView('{0}');\" href=\"javascript:void(0)\"><img src=\"Images/{0}.png\" alt=\"{1}\" title=\"{2}\" />{1}</a>";

            this.SwitchDetailView.Text = string.Format(
                SwitchContent,
                "DetailView",
                Localization.GetString("DetailView.Text", this.ResXFile, this.LanguageCode),
                Localization.GetString("DetailViewTitle.Text", this.ResXFile, this.LanguageCode));
            this.SwitchDetailView.ToolTip = Localization.GetString(
                "DetailViewTitle.Text",
                this.ResXFile,
                this.LanguageCode);

            this.SwitchListView.Text = string.Format(
                SwitchContent,
                "ListView",
                Localization.GetString("ListView.Text", this.ResXFile, this.LanguageCode),
                Localization.GetString("ListViewTitle.Text", this.ResXFile, this.LanguageCode));
            this.SwitchListView.ToolTip = Localization.GetString(
                "ListViewTitle.Text",
                this.ResXFile,
                this.LanguageCode);

            this.SwitchIconsView.Text = string.Format(
                SwitchContent,
                "IconsView",
                Localization.GetString("IconsView.Text", this.ResXFile, this.LanguageCode),
                Localization.GetString("IconsViewTitle.Text", this.ResXFile, this.LanguageCode));
            this.SwitchIconsView.ToolTip = Localization.GetString(
                "IconsViewTitle.Text",
                this.ResXFile,
                this.LanguageCode);

            this.SortAscending.Text =
                $"<img src=\"Images/SortAscending.png\" alt=\"{Localization.GetString("SortAscending.Text", this.ResXFile, this.LanguageCode)}\" title=\"{Localization.GetString("SortAscending.Help", this.ResXFile, this.LanguageCode)}\" />";
            this.SortAscending.ToolTip = Localization.GetString("SortAscending.Help", this.ResXFile, this.LanguageCode);

            this.SortDescending.Text =
                $"<img src=\"Images/SortDescending.png\" alt=\"{Localization.GetString("SortDescending.Text", this.ResXFile, this.LanguageCode)}\" title=\"{Localization.GetString("SortDescending.Help", this.ResXFile, this.LanguageCode)}\" />";
            this.SortDescending.ToolTip = Localization.GetString(
                "SortDescending.Help",
                this.ResXFile,
                this.LanguageCode);

            ClientAPI.AddButtonConfirm(
                this.cmdDelete,
                Localization.GetString("AreYouSure.Text", this.ResXFile, this.LanguageCode));

            this.SetDefaultLinkTypeText();
        }

        /// <summary>
        /// Goes to selected file.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        private void GoToSelectedFile(string fileName)
        {
            // Find the File inside the Repeater
            foreach (RepeaterItem item in this.FilesList.Items)
            {
                var listRow = (HtmlGenericControl)item.FindControl("ListRow");

                switch (item.ItemType)
                {
                    case ListItemType.Item:
                        listRow.Attributes["class"] = "FilesListRow";
                        break;
                    case ListItemType.AlternatingItem:
                        listRow.Attributes["class"] = "FilesListRowAlt";
                        break;
                }

                if (listRow.Attributes["title"] != fileName)
                {
                    continue;
                }

                listRow.Attributes["class"] += " Selected";

                var fileListItem = (LinkButton)item.FindControl("FileListItem");

                if (fileListItem == null)
                {
                    return;
                }

                var fileId = Convert.ToInt32(fileListItem.CommandArgument);

                var fileInfo = FileManager.Instance.GetFile(fileId);

                this.ShowFileHelpUrl(fileInfo.FileName, fileInfo);

                this.ScrollToSelectedFile(fileListItem.ClientID);
            }
        }

        /// <summary>
        /// Show Preview for the URLs
        /// </summary>
        /// <param name="fileName">
        /// Selected FileName
        /// </param>
        /// <param name="fileInfo">
        /// The file Info.
        /// </param>
        private void ShowFileHelpUrl(string fileName, IFileInfo fileInfo)
        {
            try
            {
                this.SetDefaultLinkTypeText();

                // Enable Buttons
                this.CheckFolderAccess(fileInfo.FolderId, true);

                // Hide other Items if Secure Folder
                var folderPath = this.lblCurrentDir.Text;

                var isSecureFolder = false;

                var storageLocationType = this.GetStorageLocationType(folderPath);

                switch (storageLocationType)
                {
                    case FolderController.StorageLocationTypes.SecureFileSystem:
                        {
                            isSecureFolder = true;

                            fileName += ".resources";

                            this.cmdResizer.Enabled = false;
                            this.cmdResizer.CssClass = "LinkDisabled";

                            this.rblLinkType.Items[2].Selected = true;
                        }

                        break;
                    case FolderController.StorageLocationTypes.DatabaseSecure:
                        {
                            isSecureFolder = true;

                            this.cmdResizer.Enabled = false;
                            this.cmdResizer.CssClass = "LinkDisabled";

                            this.rblLinkType.Items[2].Selected = true;
                        }

                        break;
                    default:
                        {
                            this.rblLinkType.Items[0].Selected = true;

                            var extension = Path.GetExtension(fileName);
                            extension = extension.TrimStart('.');

                            var isAllowedExtension =
                                this.allowedImageExtensions.Any(sAllowExt => sAllowExt.Equals(extension.ToLower()));

                            this.cmdResizer.Enabled = isAllowedExtension;
                            this.cmdResizer.CssClass = isAllowedExtension ? "LinkNormal" : "LinkDisabled";
                        }

                        break;
                }

                this.rblLinkType.Items[0].Enabled = !isSecureFolder;
                this.rblLinkType.Items[1].Enabled = !isSecureFolder;

                //////
                this.FileId.Text = fileInfo.FileId.ToString();
                this.lblFileName.Text = fileName;
                /*
                // Relative Url
                this.rblLinkType.Items[0].Text = Regex.Replace(
                    this.rblLinkType.Items[0].Text,
                    "/Images/MyImage.jpg",
                    MapUrl(Path.Combine(this.lblCurrentDir.Text, fileName)),
                    RegexOptions.IgnoreCase);

                var absoluteUrl = string.Format(
                    "{0}://{1}{2}{3}",
                    HttpContext.Current.Request.Url.Scheme,
                    HttpContext.Current.Request.Url.Authority,
                    MapUrl(this.lblCurrentDir.Text),
                    fileName);

                // Absolute Url
                this.rblLinkType.Items[1].Text = Regex.Replace(
                    this.rblLinkType.Items[1].Text,
                    "http://www.MyWebsite.com/Images/MyImage.jpg",
                    absoluteUrl,
                    RegexOptions.IgnoreCase);

                if (this.rblLinkType.Items.Count <= 2)
                {
                    return;
                }

                // LinkClick Url
                var link = string.Format("fileID={0}", fileInfo.FileId);

                var secureLink = Globals.LinkClick(
                    link,
                    int.Parse(this.request.QueryString["tabid"]),
                    Null.NullInteger);

                this.rblLinkType.Items[2].Text = this.rblLinkType.Items[2]
                    .Text.Replace(@"/LinkClick.aspx?fileticket=xyz", secureLink);

                absoluteUrl = string.Format(
                    "{0}://{1}{2}",
                    HttpContext.Current.Request.Url.Scheme,
                    HttpContext.Current.Request.Url.Authority,
                    secureLink);

                this.rblLinkType.Items[3].Text = this.rblLinkType.Items[3]
                    .Text.Replace(@"http://www.MyWebsite.com/LinkClick.aspx?fileticket=xyz", absoluteUrl);

                ////////
                */
            }
            catch (Exception)
            {
                this.SetDefaultLinkTypeText();
            }
        }

        /// <summary>
        /// Shows the files in directory.
        /// </summary>
        /// <param name="currentFolderInfo">The current folder information.</param>
        /// <param name="pagerChanged">if set to <c>true</c> [pager changed].</param>
        private void ShowFilesIn(IFolderInfo currentFolderInfo, bool pagerChanged = false)
        {
            this.CurrentPathInfo.Text =
                $"{Localization.GetString("Root.Text", this.ResXFile, this.LanguageCode)}/{currentFolderInfo.FolderPath}";

            this.CheckFolderAccess(currentFolderInfo.FolderID, false);

            if (!pagerChanged)
            {
                this.FilesTable = this.GetFiles(currentFolderInfo);

                this.GetDiskSpaceUsed();
            }
            else
            {
                if (this.FilesTable == null)
                {
                    this.FilesTable = this.GetFiles(currentFolderInfo);
                }
            }

            var filesPagedDataSource = new PagedDataSource { DataSource = this.FilesTable.DefaultView };

            if (this.currentSettings.FileListPageSize > 0)
            {
                filesPagedDataSource.AllowPaging = true;
                filesPagedDataSource.PageSize = this.currentSettings.FileListPageSize;
                filesPagedDataSource.CurrentPageIndex = pagerChanged ? this.PagerFileLinks.CurrentPageIndex : 0;
            }

            this.PagerFileLinks.PageCount = filesPagedDataSource.PageCount;
            this.PagerFileLinks.RessourceFile = this.ResXFile;
            this.PagerFileLinks.LanguageCode = this.LanguageCode;

            this.PagerFileLinks.Visible = filesPagedDataSource.PageCount > 1;

            this.FilesList.DataSource = filesPagedDataSource;
            this.FilesList.DataBind();
        }

        /// <summary>
        /// Automatically Uploads the Pasted Image
        /// </summary>
        /// <param name="file">
        /// The Uploaded Image File
        /// </param>
        private void UploadAutoImageFile(HttpPostedFile file)
        {
            var fileName = Path.GetFileName(file.FileName).Trim();

            if (!string.IsNullOrEmpty(fileName))
            {
                // Replace dots in the name with underscores (only one dot can be there... security issue).
                fileName = Regex.Replace(fileName, @"\.(?![^.]*$)", "_", RegexOptions.None);

                // Check for Illegal Chars
                if (Utility.ValidateFileName(fileName))
                {
                    fileName = Utility.CleanFileName(fileName);
                }

                // Convert Unicode Chars
                fileName = Utility.ConvertUnicodeChars(fileName);
            }
            else
            {
                return;
            }

            // Check if file is to big for that user
            if (this.currentSettings.UploadFileSizeLimit > 0
                && file.ContentLength > this.currentSettings.UploadFileSizeLimit)
            {
                var upload = new UploadImage
                                 {
                                     uploaded = 0,
                                     fileName = string.Empty,
                                     url = string.Empty,
                                     error = new Error
                                                 {
                                                     message = Localization.GetString(
                                                         "FileToBigMessage.Text",
                                                         this.ResXFile,
                                                         this.LanguageCode)
                                                 }
                                 };

                this.Response.ContentType = "application/json";
                this.Response.ContentEncoding = Encoding.UTF8;

                this.Response.Write(new JavaScriptSerializer().Serialize(upload));

                HttpContext.Current.ApplicationInstance.CompleteRequest();

                return;
            }

            if (fileName.Length > 220)
            {
                fileName = fileName.Substring(fileName.Length - 220);
            }

            var extension = Path.GetExtension(file.FileName);
            extension = extension.TrimStart('.');

            var allowUpload = this.allowedImageExtensions.Any(sAllowExt => sAllowExt.Equals(extension.ToLower()));

            if (allowUpload)
            {
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

                var counter = 0;

                var uploadPhysicalPath = this.StartingDir().PhysicalPath;

                var currentFolderInfo = Utility.ConvertFilePathToFolderInfo(
                    this.lblCurrentDir.Text,
                    this._portalSettings);

                if (!this.currentSettings.UploadDirId.Equals(-1) && !this.currentSettings.SubDirs)
                {
                    var uploadFolder = FolderManager.Instance.GetFolder(this.currentSettings.UploadDirId);

                    if (uploadFolder != null)
                    {
                        uploadPhysicalPath = uploadFolder.PhysicalPath;

                        currentFolderInfo = uploadFolder;
                    }
                }

                var filePath = Path.Combine(uploadPhysicalPath, fileName);

                var imageResizer = new ImageResizer
                                       {
                                           ImageQuality = this.currentSettings.Config.ResizeImageQuality,
                                           MaxHeight = this.currentSettings.Config.Resize_MaxHeight,
                                           MaxWidth = this.currentSettings.Config.Resize_MaxWidth
                                       };


                // Automatically Resize Image on Upload
                var fileStream =
                    this.currentSettings.Config.ResizeImageOnQuickUpload && Utility.IsImageFile(file.FileName)
                        ? imageResizer.Resize(file)
                        : file.InputStream;

                if (File.Exists(filePath))
                {
                    counter++;
                    fileName = $"{fileNameWithoutExtension}_{counter}{Path.GetExtension(file.FileName)}";

                    FileManager.Instance.AddFile(currentFolderInfo, fileName, fileStream);
                }
                else
                {
                    FileManager.Instance.AddFile(currentFolderInfo, fileName, fileStream);
                }

                var imageUrl = MapUrl(uploadPhysicalPath);

                var upload = new UploadImage
                                 {
                                     uploaded = 1,
                                     fileName = fileName,
                                     url = string.Format(
                                         !imageUrl.EndsWith("/") ? "{0}/{1}" : "{0}{1}",
                                         imageUrl,
                                         fileName),
                                     error = new Error { message = string.Empty }
                                 };

                this.Response.ContentType = "application/json";
                this.Response.ContentEncoding = Encoding.UTF8;

                this.Response.Write(new JavaScriptSerializer().Serialize(upload));

                HttpContext.Current.ApplicationInstance.CompleteRequest();

                this.Response.End();
            }
            else
            {
                var upload = new UploadImage
                                 {
                                     uploaded = 0,
                                     fileName = string.Empty,
                                     url = string.Empty,
                                     error = new Error
                                                 {
                                                     message = Localization.GetString(
                                                         "Error2.Text",
                                                         this.ResXFile,
                                                         this.LanguageCode)
                                                 }
                                 };

                this.Response.ContentType = "application/json";
                this.Response.ContentEncoding = Encoding.UTF8;

                this.Response.Write(new JavaScriptSerializer().Serialize(upload));

                HttpContext.Current.ApplicationInstance.CompleteRequest();

                this.Response.End();
            }
        }

        /// <summary>
        /// Uploads a File
        /// </summary>
        /// <param name="file">
        /// The Uploaded File
        /// </param>
        /// <param name="command">
        /// The Upload Command Type
        /// </param>
        private void UploadQuickFile(HttpPostedFile file, string command)
        {
            var fileName = Path.GetFileName(file.FileName).Trim();

            if (!string.IsNullOrEmpty(fileName))
            {
                // Replace dots in the name with underscores (only one dot can be there... security issue).
                fileName = Regex.Replace(fileName, @"\.(?![^.]*$)", "_", RegexOptions.None);

                // Check for Illegal Chars
                if (Utility.ValidateFileName(fileName))
                {
                    fileName = Utility.CleanFileName(fileName);
                }

                // Convert Unicode Chars
                fileName = Utility.ConvertUnicodeChars(fileName);
            }
            else
            {
                return;
            }

            // Check if file is to big for that user
            if (this.currentSettings.UploadFileSizeLimit > 0
                && file.ContentLength > this.currentSettings.UploadFileSizeLimit)
            {
                this.Page.ClientScript.RegisterStartupScript(
                    this.GetType(),
                    "errorcloseScript",
                    $"javascript:alert('{Localization.GetString("FileToBigMessage.Text", this.ResXFile, this.LanguageCode)}')",
                    true);

                this.Response.End();

                return;
            }

            if (fileName.Length > 220)
            {
                fileName = fileName.Substring(fileName.Length - 220);
            }

            var extension = Path.GetExtension(file.FileName);
            extension = extension.TrimStart('.');

            var allowUpl = false;

            switch (command)
            {
                case "FlashUpload":
                    if (this.allowedFlashExt.Any(sAllowExt => sAllowExt.Equals(extension.ToLower())))
                    {
                        allowUpl = true;
                    }

                    break;
                case "ImageUpload":
                    if (this.allowedImageExtensions.Any(sAllowExt => sAllowExt.Equals(extension.ToLower())))
                    {
                        allowUpl = true;
                    }

                    break;
                case "FileUpload":
                    if (this.extensionWhiteList.Contains(extension.ToLower()))
                    {
                        allowUpl = true;
                    }

                    break;
            }

            if (allowUpl)
            {
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

                var counter = 0;

                var uploadPhysicalPath = this.StartingDir().PhysicalPath;

                var currentFolderInfo = Utility.ConvertFilePathToFolderInfo(
                    this.lblCurrentDir.Text,
                    this._portalSettings);

                if (!this.currentSettings.UploadDirId.Equals(-1) && !this.currentSettings.SubDirs)
                {
                    var uploadFolder = FolderManager.Instance.GetFolder(this.currentSettings.UploadDirId);

                    if (uploadFolder != null)
                    {
                        uploadPhysicalPath = uploadFolder.PhysicalPath;

                        currentFolderInfo = uploadFolder;
                    }
                }

                var filePath = Path.Combine(uploadPhysicalPath, fileName);

                var imageResizer = new ImageResizer
                                       {
                                           ImageQuality = this.currentSettings.Config.ResizeImageQuality,
                                           MaxHeight = this.currentSettings.Config.Resize_MaxHeight,
                                           MaxWidth = this.currentSettings.Config.Resize_MaxWidth
                                       };


                // Automatically Resize Image on Upload
                var fileStream =
                    this.currentSettings.Config.ResizeImageOnQuickUpload && Utility.IsImageFile(file.FileName)
                        ? imageResizer.Resize(file)
                        : file.InputStream;

                if (File.Exists(filePath))
                {
                    counter++;
                    fileName = $"{fileNameWithoutExtension}_{counter}{Path.GetExtension(file.FileName)}";

                    FileManager.Instance.AddFile(currentFolderInfo, fileName, fileStream);
                }
                else
                {
                    FileManager.Instance.AddFile(currentFolderInfo, fileName, fileStream);
                }

                this.Response.Write("<script type=\"text/javascript\">");
                this.Response.Write(this.GetJsUploadCode(fileName, MapUrl(uploadPhysicalPath)));
                this.Response.Write("</script>");

                this.Response.End();
            }
            else
            {
                this.Page.ClientScript.RegisterStartupScript(
                    this.GetType(),
                    "errorcloseScript",
                    $"javascript:alert('{Localization.GetString("Error2.Text", this.ResXFile, this.LanguageCode)}')",
                    true);

                this.Response.End();
            }
        }

        /// <summary>
        /// Exit Dialog
        /// </summary>
        /// <param name="sender">
        /// The sender.
        /// </param>
        /// <param name="e">
        /// The e.
        /// </param>
        private void Cancel_Click(object sender, EventArgs e)
        {
            this.Page.ClientScript.RegisterStartupScript(
                this.GetType(),
                "closeScript",
                "javascript:self.close();",
                true);
        }

        /// <summary>
        /// Hide Create New Folder Panel
        /// </summary>
        /// <param name="sender">
        /// The sender.
        /// </param>
        /// <param name="e">
        /// The e.
        /// </param>
        private void CreateCancel_Click(object sender, EventArgs e)
        {
            this.panCreate.Visible = false;
        }

        /// <summary>
        /// Create a New Sub Folder
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void CreateFolder_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(this.tbFolderName.Text))
            {
                if (Utility.ValidatePath(this.tbFolderName.Text))
                {
                    this.tbFolderName.Text = Utility.CleanPath(this.tbFolderName.Text);
                }

                this.tbFolderName.Text = Utility.CleanPath(this.tbFolderName.Text);

                var newDirPath = Path.Combine(this.lblCurrentDir.Text, this.tbFolderName.Text);

                try
                {
                    var folderPath = newDirPath;

                    folderPath = folderPath.Substring(this._portalSettings.HomeDirectoryMapPath.Length)
                        .Replace("\\", "/");

                    var storageLocation = (int)FolderController.StorageLocationTypes.InsecureFileSystem;

                    var currentStorageLocationType = this.GetStorageLocationType(this.lblCurrentDir.Text);

                    switch (currentStorageLocationType)
                    {
                        case FolderController.StorageLocationTypes.SecureFileSystem:
                            storageLocation = (int)FolderController.StorageLocationTypes.SecureFileSystem;
                            break;
                        case FolderController.StorageLocationTypes.DatabaseSecure:
                            storageLocation = (int)FolderController.StorageLocationTypes.DatabaseSecure;
                            break;
                    }

                    if (!Directory.Exists(newDirPath))
                    {
                        Directory.CreateDirectory(newDirPath);

                        var newFolder = new FolderInfo
                                            {
                                                UniqueId = Guid.NewGuid(),
                                                VersionGuid = Guid.NewGuid(),
                                                PortalID = this._portalSettings.PortalId,
                                                FolderPath = folderPath,
                                                StorageLocation = storageLocation,
                                                IsProtected = false,
                                                IsCached = false
                                            };

                        var folderId = FolderManager.Instance.AddFolder(
                            FolderMappingController.Instance.GetFolderMapping(
                                newFolder.PortalID,
                                newFolder.FolderMappingID),
                            newFolder.FolderPath).FolderID;

                        this.SetFolderPermission(folderId);
                    }

                    this.lblCurrentDir.Text = $"{newDirPath}\\";
                }
                catch (Exception exception)
                {
                    this.Response.Write("<script type=\"text/javascript\">");

                    var message = exception.Message.Replace("'", string.Empty).Replace("\r\n", string.Empty)
                        .Replace("\n", string.Empty).Replace("\r", string.Empty);

                    this.Response.Write($"javascript:alert('{this.Context.Server.HtmlEncode(message)}');");

                    this.Response.Write("</script>");
                }
                finally
                {
                    this.FillFolderTree(this.StartingDir());

                    this.ShowFilesIn(newDirPath);

                    var newFolder = this.FoldersTree.FindNode(this.tbFolderName.Text);

                    if (newFolder != null)
                    {
                        newFolder.Selected = true;
                        newFolder.Expand();
                    }
                }
            }

            this.panCreate.Visible = false;
        }

        /// <summary>
        /// Save the New Cropped Image
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        private void CropNow_Click(object sender, EventArgs e)
        {
            // Hide Image Editor Panels
            this.panImagePreview.Visible = false;
            this.panImageEdHead.Visible = false;
            this.panImageEditor.Visible = false;
            this.panThumb.Visible = false;

            // Show Link Panel
            this.panLinkMode.Visible = true;
            this.cmdClose.Visible = true;
            this.panInfo.Visible = PortalSecurity.IsInRoles(this._portalSettings.AdministratorRoleName);

            if (this.browserModus.Equals("Link"))
            {
                this.BrowserMode.Visible = true;
            }

            this.title.InnerText = $"{this.lblModus.Text} - WatchersNET.FileBrowser";

            // Add new file to database
            var currentFolderInfo = Utility.ConvertFilePathToFolderInfo(this.lblCurrentDir.Text, this._portalSettings);

            FolderManager.Instance.Synchronize(
                this._portalSettings.PortalId,
                currentFolderInfo.FolderPath,
                false,
                true);

            this.ShowFilesIn(this.lblCurrentDir.Text);

            var extension = Path.GetExtension(this.lblFileName.Text);

            this.GoToSelectedFile($"{this.txtCropImageName.Text}{extension}");
        }

        /// <summary>
        /// Hide Image Re-sizing Panel
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        private void ResizeCancel_Click(object sender, EventArgs e)
        {
            // Hide Image Editor Panels
            this.panImagePreview.Visible = false;
            this.panImageEdHead.Visible = false;
            this.panImageEditor.Visible = false;
            this.panThumb.Visible = false;

            // Show Link Panel
            this.panLinkMode.Visible = true;
            this.cmdClose.Visible = true;
            this.panInfo.Visible = PortalSecurity.IsInRoles(this._portalSettings.AdministratorRoleName);
            this.title.InnerText = $"{this.lblModus.Text} - WatchersNET.FileBrowser";

            if (this.browserModus.Equals("Link"))
            {
                this.BrowserMode.Visible = true;
            }
        }

        /// <summary>
        /// Resize Image based on User Input
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        private void ResizeNow_Click(object sender, EventArgs e)
        {
            var filePath = Path.Combine(this.lblCurrentDir.Text, this.lblFileName.Text);

            var extension = Path.GetExtension(filePath);

            var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            var oldImage = Image.FromStream(fileStream);

            string imageFullPath;

            int newWidth, newHeight;

            try
            {
                newWidth = int.Parse(this.txtWidth.Text);
            }
            catch (Exception)
            {
                newWidth = oldImage.Width;
            }

            try
            {
                newHeight = int.Parse(this.txtHeight.Text);
            }
            catch (Exception)
            {
                newHeight = oldImage.Height;
            }

            if (!string.IsNullOrEmpty(this.txtThumbName.Text))
            {
                imageFullPath = Path.Combine(this.lblCurrentDir.Text, this.txtThumbName.Text + extension);
            }
            else
            {
                imageFullPath = Path.Combine(
                    this.lblCurrentDir.Text,
                    $"{Path.GetFileNameWithoutExtension(filePath)}_resized{extension}");
            }

            // Create an Resized Thumbnail
            if (this.chkAspect.Checked)
            {
                var finalHeight = Math.Abs(oldImage.Height * newWidth / oldImage.Width);

                if (finalHeight > newHeight)
                {
                    // Height resize if necessary
                    newWidth = oldImage.Width * newHeight / oldImage.Height;
                    finalHeight = newHeight;
                }

                newHeight = finalHeight;
            }

            var counter = 0;

            while (File.Exists(imageFullPath))
            {
                counter++;

                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(imageFullPath);

                imageFullPath = Path.Combine(
                    this.lblCurrentDir.Text,
                    $"{fileNameWithoutExtension}_{counter}{Path.GetExtension(imageFullPath)}");
            }

            // Add Compression to Jpeg Images
            if (oldImage.RawFormat.Equals(ImageFormat.Jpeg))
            {
                var jgpEncoder = GetEncoder(oldImage.RawFormat);

                var myEncoder = Encoder.Quality;
                var encodParams = new EncoderParameters(1);
                var encodParam = new EncoderParameter(myEncoder, long.Parse(this.dDlQuality.SelectedValue));
                encodParams.Param[0] = encodParam;

                using (var dst = new Bitmap(newWidth, newHeight))
                {
                    using (var g = Graphics.FromImage(dst))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(oldImage, 0, 0, dst.Width, dst.Height);
                    }

                    dst.Save(imageFullPath, jgpEncoder, encodParams);
                }
            }
            else
            {
                // Finally Create a new Resized Image
                var newImage = oldImage.GetThumbnailImage(newWidth, newHeight, null, IntPtr.Zero);
                oldImage.Dispose();

                newImage.Save(imageFullPath);
                newImage.Dispose();
            }

            // Add new file to database
            var currentFolderInfo = Utility.ConvertFilePathToFolderInfo(this.lblCurrentDir.Text, this._portalSettings);

            FolderManager.Instance.Synchronize(
                this._portalSettings.PortalId,
                currentFolderInfo.FolderPath,
                false,
                true);

            /*else if (OldImage.RawFormat.Equals(ImageFormat.Gif))
            {
                // Finally Create a new Resized Gif Image
                GifHelper gifHelper = new GifHelper();

                gifHelper.GetThumbnail(sFilePath,new Size(iNewWidth, iNewHeight), sImageFullPath);
            }*/

            // Hide Image Editor Panels
            this.panImagePreview.Visible = false;
            this.panImageEdHead.Visible = false;
            this.panImageEditor.Visible = false;
            this.panThumb.Visible = false;

            // Show Link Panel
            this.panLinkMode.Visible = true;
            this.cmdClose.Visible = true;
            this.panInfo.Visible = PortalSecurity.IsInRoles(this._portalSettings.AdministratorRoleName);
            this.title.InnerText = $"{this.lblModus.Text} - WatchersNET.FileBrowser";

            if (this.browserModus.Equals("Link"))
            {
                this.BrowserMode.Visible = true;
            }

            this.ShowFilesIn(this.lblCurrentDir.Text);

            this.GoToSelectedFile(Path.GetFileName(imageFullPath));
        }

        /// <summary>
        /// Hide Resize Panel and Show CropZoom Panel
        /// </summary>
        /// <param name="sender">
        /// The sender.
        /// </param>
        /// <param name="e">
        /// The Event Args e.
        /// </param>
        private void Rotate_Click(object sender, EventArgs e)
        {
            this.panThumb.Visible = false;
            this.panImageEditor.Visible = true;

            this.imgOriginal.Visible = false;

            this.lblCropInfo.Visible = true;

            this.cmdRotate.Visible = false;
            this.cmdCrop.Visible = false;
            this.cmdZoom.Visible = false;
            this.cmdResize2.Visible = true;

            this.lblResizeHeader.Text = Localization.GetString(
                "lblResizeHeader2.Text",
                this.ResXFile,
                this.LanguageCode);
            this.title.InnerText = $"{this.lblResizeHeader.Text} - WatchersNET.FileBrowser";

            var filePath = Path.Combine(this.lblCurrentDir.Text, this.lblFileName.Text);

            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);

            this.txtCropImageName.Text = $"{fileNameWithoutExtension}_Crop";

            var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            var image = Image.FromStream(fs);

            var cropZoom = new StringBuilder();

            cropZoom.Append("jQuery(document).ready(function () {");

            cropZoom.Append("jQuery('#imgResized').hide();");

            cropZoom.Append("var cropzoom = jQuery('#ImageOriginal').cropzoom({");
            cropZoom.Append("width: 400,");
            cropZoom.Append("height: 300,");
            cropZoom.Append("bgColor: '#CCC',");
            cropZoom.Append("enableRotation: true,");
            cropZoom.Append("enableZoom: true,");

            cropZoom.Append("selector: {");

            cropZoom.Append("w:100,");
            cropZoom.Append("h:80,");
            cropZoom.Append("showPositionsOnDrag: true,");
            cropZoom.Append("showDimetionsOnDrag: true,");
            cropZoom.Append("bgInfoLayer: '#FFF',");
            cropZoom.Append("infoFontSize: 10,");
            cropZoom.Append("infoFontColor: 'blue',");
            cropZoom.Append("showPositionsOnDrag: true,");
            cropZoom.Append("showDimetionsOnDrag: true,");
            cropZoom.Append("maxHeight: null,");
            cropZoom.Append("maxWidth: null,");
            cropZoom.Append("centered: true,");
            cropZoom.Append("borderColor: 'blue',");
            cropZoom.Append("borderColorHover: '#9eda29'");

            cropZoom.Append("},");

            cropZoom.Append("image: {");
            cropZoom.AppendFormat("source: '{0}',", MapUrl(filePath));
            cropZoom.AppendFormat("width: {0},", image.Width);
            cropZoom.AppendFormat("height: {0},", image.Height);
            cropZoom.Append("minZoom: 10,");
            cropZoom.Append("maxZoom: 150");
            cropZoom.Append("}");
            cropZoom.Append("});");

            // Preview Button
            cropZoom.Append("jQuery('#PreviewCrop').click(function () {");

            cropZoom.Append("jQuery('#lblCropInfo').hide();");
            cropZoom.Append(
                "jQuery('#imgResized').attr('src', 'ProcessImage.ashx?' + cropzoom.PreviewParams()).show();");

            cropZoom.Append("ResizeMe('#imgResized', 360, 300);");

            cropZoom.Append("});");

            // Reset Button
            cropZoom.Append("jQuery('#ClearCrop').click(function(){");
            cropZoom.Append("jQuery('#imgResized').hide();");
            cropZoom.Append("jQuery('#lblCropInfo').show();");
            cropZoom.Append("cropzoom.restore();");
            cropZoom.Append("});");

            // Save Button
            cropZoom.Append("jQuery('#CropNow').click(function(e) {");
            cropZoom.Append("e.preventDefault();");
            cropZoom.Append(
                "cropzoom.send('ProcessImage.ashx', 'POST', { newFileName:  jQuery('#txtCropImageName').val(), saveFile: true }, function(){ javascript: __doPostBack('cmdCropNow', ''); });");
            cropZoom.Append("});");

            cropZoom.Append("});");

            this.Page.ClientScript.RegisterStartupScript(
                this.GetType(),
                $"CropZoomScript{Guid.NewGuid()}",
                cropZoom.ToString(),
                true);
        }

        /// <summary>
        /// Cancel Upload - Hide Upload Controls
        /// </summary>
        /// <param name="sender">
        /// The sender.
        /// </param>
        /// <param name="e">
        /// The Event Args e.
        /// </param>
        private void UploadCancel_Click(object sender, EventArgs e)
        {
            this.panUploadDiv.Visible = false;
        }

        /// <summary>
        /// Upload Selected File
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        private void UploadNow_Click(object sender, EventArgs e)
        {
            Thread.Sleep(1000);
            this.SyncCurrentFolder();

            this.GoToSelectedFile(this.SelectedFile.Value);

            this.SelectedFile.Value = string.Empty;

            this.panUploadDiv.Visible = false;
        }

        /// <summary>
        /// Show Preview of the Page links
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        private void TreeTabs_NodeClick(object sender, EventArgs e)
        {
            if (this.dnntreeTabs.SelectedNode == null)
            {
                return;
            }

            this.SetDefaultLinkTypeText();

            var tabController = new TabController();

            var selectTab = tabController.GetTab(
                int.Parse(this.dnntreeTabs.SelectedValue),
                this._portalSettings.PortalId,
                true);

            var domainName = $"http://{Globals.GetDomainName(this.Request, true)}";

            // Add Language Parameter ?!
            var localeSelected = this.LanguageRow.Visible && this.LanguageList.SelectedIndex > 0;

            if (this.chkHumanFriendy.Checked)
            {
                var fileName = localeSelected
                                   ? Globals.FriendlyUrl(
                                       selectTab,
                                       $"{Globals.ApplicationURL(selectTab.TabID)}&language={this.LanguageList.SelectedValue}",
                                       this._portalSettings)
                                   : Globals.FriendlyUrl(
                                       selectTab,
                                       Globals.ApplicationURL(selectTab.TabID),
                                       this._portalSettings);

                // Relative Url
                fileName = Globals.ResolveUrl(Regex.Replace(fileName, domainName, "~", RegexOptions.IgnoreCase));

                this.rblLinkType.Items[0].Text = Regex.Replace(
                    this.rblLinkType.Items[0].Text,
                    "/Images/MyImage.jpg",
                    Globals.ResolveUrl(Regex.Replace(fileName, domainName, "~", RegexOptions.IgnoreCase)),
                    RegexOptions.IgnoreCase);

                // Absolute Url
                this.rblLinkType.Items[1].Text = Regex.Replace(
                    this.rblLinkType.Items[1].Text,
                    "http://www.MyWebsite.com/Images/MyImage.jpg",
                    $"{HttpContext.Current.Request.Url.Scheme}://{HttpContext.Current.Request.Url.Authority}{fileName}",
                    RegexOptions.IgnoreCase);
            }
            else
            {
                var locale = localeSelected ? $"language/{this.LanguageList.SelectedValue}/" : string.Empty;

                // Relative Url
                this.rblLinkType.Items[0].Text = Regex.Replace(
                    this.rblLinkType.Items[0].Text,
                    "/Images/MyImage.jpg",
                    Globals.ResolveUrl($"~/tabid/{selectTab.TabID}/{locale}Default.aspx"),
                    RegexOptions.IgnoreCase);

                // Absolute Url
                this.rblLinkType.Items[1].Text = Regex.Replace(
                    this.rblLinkType.Items[1].Text,
                    "http://www.MyWebsite.com/Images/MyImage.jpg",
                    string.Format("{2}/tabid/{0}/{1}Default.aspx", selectTab.TabID, locale, domainName),
                    RegexOptions.IgnoreCase);
            }

            /////
            var secureLink = Globals.LinkClick(
                selectTab.TabID.ToString(),
                int.Parse(this.request.QueryString["tabid"]),
                Null.NullInteger);

            if (secureLink.Contains("&language"))
            {
                secureLink = secureLink.Remove(secureLink.IndexOf("&language", StringComparison.Ordinal));
            }

            this.rblLinkType.Items[2].Text = this.rblLinkType.Items[2].Text.Replace(
                @"/LinkClick.aspx?fileticket=xyz",
                secureLink);

            var absoluteUrl =
                $"{HttpContext.Current.Request.Url.Scheme}://{HttpContext.Current.Request.Url.Authority}{secureLink}";

            this.rblLinkType.Items[3].Text = this.rblLinkType.Items[3].Text.Replace(
                @"http://www.MyWebsite.com/LinkClick.aspx?fileticket=xyz",
                absoluteUrl);

            if (this.currentSettings.UseAnchorSelector)
            {
                this.FindAnchorsOnTab(selectTab);
            }

            this.Page.ClientScript.RegisterStartupScript(
                this.GetType(),
                $"hideLoadingScript{Guid.NewGuid()}",
                "jQuery('#panelLoading').hide();",
                true);
        }

        /// <summary>
        /// Find and List all Anchors from the Selected Page.
        /// </summary>
        /// <param name="selectedTab">
        /// The selected tab.
        /// </param>
        private void FindAnchorsOnTab(TabInfo selectedTab)
        {
            // Clear Item list first...
            this.AnchorList.Items.Clear();

            var noneText = Localization.GetString("None.Text", this.ResXFile, this.LanguageCode);

            try
            {
                var wc = new WebClient();

                var tabUrl = selectedTab.FullUrl;

                if (tabUrl.StartsWith("/"))
                {
                    tabUrl =
                        $"{HttpContext.Current.Request.Url.Scheme}://{HttpContext.Current.Request.Url.Authority}{tabUrl}";
                }

                var page = wc.DownloadString(tabUrl);

                foreach (var i in AnchorFinder.ListAll(page).Where(i => !string.IsNullOrEmpty(i.Anchor)))
                {
                    this.AnchorList.Items.Add(i.Anchor);
                }

                // Add No Anchor item
                this.AnchorList.Items.Insert(0, noneText);
            }
            catch (Exception)
            {
                // Add No Anchor item
                this.AnchorList.Items.Add(noneText);
            }
        }

        /// <summary>
        /// Show Info for Selected File
        /// </summary>
        /// <param name="source">The source of the event.</param>
        /// <param name="e">The <see cref="System.Web.UI.WebControls.RepeaterCommandEventArgs"/> instance containing the event data.</param>
        private void FilesList_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            foreach (RepeaterItem item in this.FilesList.Items)
            {
                var listRowItem = (HtmlGenericControl)item.FindControl("ListRow");

                switch (item.ItemType)
                {
                    case ListItemType.Item:
                        listRowItem.Attributes["class"] = "FilesListRow";
                        break;
                    case ListItemType.AlternatingItem:
                        listRowItem.Attributes["class"] = "FilesListRowAlt";
                        break;
                }
            }

            var listRow = (HtmlGenericControl)e.Item.FindControl("ListRow");
            listRow.Attributes["class"] += " Selected";

            var fileListItem = (LinkButton)e.Item.FindControl("FileListItem");

            if (fileListItem == null)
            {
                return;
            }

            var fileId = Convert.ToInt32(fileListItem.CommandArgument);

            var currentFile = FileManager.Instance.GetFile(fileId);

            this.ShowFileHelpUrl(currentFile.FileName, currentFile);

            this.ScrollToSelectedFile(fileListItem.ClientID);
        }

        /// <summary>
        /// Switch Browser in Link Modus between Link and Page Mode
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        private void BrowserMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (this.BrowserMode.SelectedValue)
            {
                case "file":
                    this.panLinkMode.Visible = true;
                    this.panPageMode.Visible = false;
                    this.lblModus.Text = $"Browser-Modus: {this.browserModus}";
                    break;
                case "page":
                    this.panLinkMode.Visible = false;
                    this.panPageMode.Visible = true;
                    this.TrackClicks.Visible = false;
                    this.lblModus.Text = $"Browser-Modus: {$"Page {this.browserModus}"}";

                    this.RenderTabs();
                    break;
            }

            this.title.InnerText = $"{this.lblModus.Text} - WatchersNET.FileBrowser";

            this.SetDefaultLinkTypeText();
        }

        /// <summary>
        /// Show / Hide "Track Clicks" Setting
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        private void LinkType_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (this.rblLinkType.SelectedValue)
            {
                case "lnkClick":
                    this.TrackClicks.Visible = true;
                    break;
                case "lnkAbsClick":
                    this.TrackClicks.Visible = true;
                    break;
                default:
                    this.TrackClicks.Visible = false;
                    break;
            }
        }

        /// <summary>
        /// Load Files of Selected Folder
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        private void FoldersTree_NodeClick(object sender, EventArgs e)
        {
            var newDir = this.FoldersTree.SelectedNode.Value;

            this.lblCurrentDir.Text = !newDir.EndsWith("\\") ? $"{newDir}\\" : newDir;
            this.ShowFilesIn(newDir);

            // Reset selected file
            this.SetDefaultLinkTypeText();

            this.FileId.Text = null;
            this.lblFileName.Text = null;

            // Expand Sub folders (if) exists
            this.FoldersTree.SelectedNode.Expanded = true;
        }

        /// <summary>
        /// Gets the disk space used.
        /// </summary>
        private void GetDiskSpaceUsed()
        {
            var spaceAvailable = this._portalSettings.HostSpace.Equals(0)
                                     ? Localization.GetString("UnlimitedSpace.Text", this.ResXFile, this.LanguageCode)
                                     : $"{this._portalSettings.HostSpace}MB";

            var spaceUsed = new PortalController().GetPortalSpaceUsedBytes(this._portalSettings.PortalId);

            string usedSpace;

            string[] suffix = { "B", "KB", "MB", "GB", "TB" };

            var index = 0;

            double spaceUsedDouble = spaceUsed;

            if (spaceUsed > 1024)
            {
                for (index = 0; spaceUsed / 1024 > 0; index++, spaceUsed /= 1024)
                {
                    spaceUsedDouble = spaceUsed / 1024.0;
                }

                usedSpace = $"{spaceUsedDouble:0.##}{suffix[index]}";
            }
            else
            {
                usedSpace = $"{spaceUsedDouble:0.##}{suffix[index]}";
            }

            this.FileSpaceUsedLabel.Text = string.Format(
                Localization.GetString("SpaceUsed.Text", this.ResXFile, this.LanguageCode),
                usedSpace,
                spaceAvailable);
        }

        /// <summary>
        /// Gets the accepted file types.
        /// </summary>
        private void GetAcceptedFileTypes()
        {
            switch (this.browserModus)
            {
                case "Flash":
                    this.AcceptFileTypes = string.Join("|", this.allowedFlashExt);

                    break;
                case "Image":
                    this.AcceptFileTypes = string.Join("|", this.allowedImageExtensions);

                    break;
                default:
                    this.AcceptFileTypes = this.extensionWhiteList.Replace(",", "|");
                    break;
            }
        }

        /// <summary>
        /// Synchronizes the current folder.
        /// </summary>
        private void SyncCurrentFolder()
        {
            var currentFolderInfo = Utility.ConvertFilePathToFolderInfo(this.lblCurrentDir.Text, this._portalSettings);

            FolderManager.Instance.Synchronize(
                this._portalSettings.PortalId,
                currentFolderInfo.FolderPath,
                false,
                true);

            // Reload Folder
            this.ShowFilesIn(this.lblCurrentDir.Text);
        }

        #endregion
    }
}