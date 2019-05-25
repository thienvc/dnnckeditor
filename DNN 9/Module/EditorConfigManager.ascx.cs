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

namespace WatchersNET.CKEditor.Module
{
    #region

    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Web.UI;
    using System.Web.UI.HtmlControls;
    using System.Web.UI.WebControls;

    using DotNetNuke.Entities.Modules;
    using DotNetNuke.Entities.Portals;
    using DotNetNuke.Entities.Tabs;
    using DotNetNuke.Services.Exceptions;
    using DotNetNuke.Services.Localization;
    using DotNetNuke.Web.Client.ClientResourceManagement;

    using WatchersNET.CKEditor.Objects;
    using WatchersNET.CKEditor.Utilities;

    #endregion

    /// <summary>
    /// The Editor Config Manger Module
    /// </summary>
    public partial class EditorConfigManager : ModuleSettingsBase
    {
        /// <summary>
        /// The page type
        /// </summary>
        private Type pageType;

        /// <summary>
        ///   Gets Current Language from Url
        /// </summary>
        protected string LangCode => CultureInfo.CurrentCulture.Name;

        /// <summary>
        ///   Gets the Name for the Current Resource file name
        /// </summary>
        protected string ResXFile => this.ResolveUrl(
            $"~/Providers/HtmlEditorProviders/CKEditor/{Localization.LocalResourceDirectory}/Options.aspx.resx");

        /// <summary>
        /// Gets or sets the editor options control
        /// </summary>
        private CKEditorOptions EditorOptions { get; set; }

        /// <summary>
        /// Raises the <see cref="E:System.Web.UI.Control.Init" /> event.
        /// </summary>
        /// <param name="e">An <see cref="T:System.EventArgs" /> object that contains the event data.</param>
        protected override void OnInit(EventArgs e)
        {
            this.InitializeComponent();
            base.OnInit(e);
        }

        /// <summary>
        /// Handles the Load event of the Page control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        protected void Page_Load(object sender, EventArgs e)
        {
            this.AddJavaScripts();

            try
            {
                this.EditorOptions =
                   (CKEditorOptions)
                   this.Page.LoadControl("~/Providers/HtmlEditorProviders/CKEditor/CKEditorOptions.ascx");

                this.EditorOptions.IsHostMode = true;

                this.EditorOptions.CurrentPortalOnly = this.PortalOnly.Checked;

                this.EditorOptions.CurrentOrSelectedTabId = this.PortalSettings.ActiveTab.TabID;
                this.EditorOptions.CurrentOrSelectedPortalId = this.PortalSettings.PortalId;

                this.EditorOptions.DefaultHostLoadMode = 0;

                this.EditorOptions.ID = "CKEditor_Options";

                this.OptionsPlaceHolder.Controls.Add(this.EditorOptions);

                if (this.Page.IsPostBack)
                {
                    return;
                }

                this.SetLanguage();
            }
            catch (Exception exception)
            {
                Exceptions.ProcessPageLoadException(exception);
            }
        }

        /// <summary>
        /// Required method for Designer support - do not modify
        ///  the contents of the method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            try
            {
                if (!this.Page.IsPostBack)
                {
                    this.BindPortalTabsAndModulesTree();
                }

                this.PortalTabsAndModulesTree.SelectedNodeChanged += this.PortalTabsAndModulesTree_SelectedNodeChanged;
                this.PortalOnly.CheckedChanged += this.PortalOnly_CheckedChanged;
            }
            catch (Exception exception)
            {
                Exceptions.ProcessModuleLoadException(this, exception);
            }
        }

        /// <summary>
        /// Handles the CheckedChanged event of the PortalOnly control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        private void PortalOnly_CheckedChanged(object sender, EventArgs e)
        {
            this.BindPortalTabsAndModulesTree();

            this.EditorOptions.Visible = true;
            this.ModuleInstanceInfoPlaceHolder.Visible = false;

            this.PortalTabsAndModulesTree.Nodes[0].Selected = true;

            ////
            this.PortalTabsAndModulesTree.SelectedNode.ExpandAll();

            this.EditorOptions.IsHostMode = true;

            this.EditorOptions.CurrentPortalOnly = this.PortalOnly.Checked;

            // Load Portal Settings for the selected Portal if exist
            var portalId = this.PortalTabsAndModulesTree.SelectedValue.Substring(1);
            var tabId = this.PortalTabsAndModulesTree.SelectedNode.ChildNodes[0].Value.Substring(1);

            this.EditorOptions.CurrentOrSelectedPortalId = Convert.ToInt32(portalId);
            this.EditorOptions.CurrentOrSelectedTabId = Convert.ToInt32(tabId);

            this.EditorOptions.DefaultHostLoadMode = 0;

            this.BindPortalTabsAndModulesTree();

            // Load Settings
            this.EditorOptions.BindOptionsData(true);
        }

        /// <summary>
        /// Loads the Settings based on the Selected Portal/Tab/Module
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        private void PortalTabsAndModulesTree_SelectedNodeChanged(object sender, EventArgs e)
        {
            if (this.PortalTabsAndModulesTree.SelectedNode == null)
            {
                return;
            }

            this.PortalTabsAndModulesTree.SelectedNode.ExpandAll();

            this.EditorOptions.IsHostMode = true;

            this.EditorOptions.CurrentPortalOnly = this.PortalOnly.Checked;

            if (this.PortalTabsAndModulesTree.SelectedNode == null)
            {
                return;
            }

            this.EditorOptions.IsHostMode = true;

            this.EditorOptions.CurrentPortalOnly = this.PortalOnly.Checked;

            if (this.PortalTabsAndModulesTree.SelectedValue.StartsWith("p"))
            {
                this.EditorOptions.Visible = true;
                this.ModuleInstanceInfoPlaceHolder.Visible = false;

                // Load Portal Settings for the selected Portal if exist
                var portalId = this.PortalTabsAndModulesTree.SelectedValue.Substring(1);
                var tabId = this.PortalTabsAndModulesTree.SelectedNode.ChildNodes[0].Value.Substring(1);

                this.EditorOptions.CurrentOrSelectedPortalId = Convert.ToInt32(portalId);
                this.EditorOptions.CurrentOrSelectedTabId = Convert.ToInt32(tabId);

                this.EditorOptions.DefaultHostLoadMode = 0;

                this.BindPortalTabsAndModulesTree();

                // Load Settings
                this.EditorOptions.BindOptionsData(true);
            }
            else if (this.PortalTabsAndModulesTree.SelectedValue.StartsWith("t"))
            {
                this.EditorOptions.Visible = true;
                this.ModuleInstanceInfoPlaceHolder.Visible = false;

                // Load Tab Settings for the selected Tab if exist
                var portalId = this.PortalTabsAndModulesTree.SelectedNode.Parent.Value.Substring(1);
                var tabId = this.PortalTabsAndModulesTree.SelectedValue.Substring(1);

                this.EditorOptions.CurrentOrSelectedPortalId = Convert.ToInt32(portalId);
                this.EditorOptions.CurrentOrSelectedTabId = Convert.ToInt32(tabId);

                this.EditorOptions.DefaultHostLoadMode = 1;

                this.BindPortalTabsAndModulesTree();

                // Load Settings
                this.EditorOptions.BindOptionsData(true);
            }
            else if (this.PortalTabsAndModulesTree.SelectedValue.StartsWith("m"))
            {
                this.EditorOptions.Visible = false;

                // Show Info Notice
                this.ModuleInstanceInfoPlaceHolder.Visible = true;
            }
        }

        /// <summary>
        /// Sets the language for all Controls
        /// </summary>
        private void SetLanguage()
        {
            this.ModuleHeader.Text = Localization.GetString("ModuleHeader.Text", this.ResXFile, this.LangCode);
            this.PortalOnlyLabel.Text = Localization.GetString("PortalOnlyLabel.Text", this.ResXFile, this.LangCode);
            this.PortalOnly.Text = Localization.GetString("PortalOnly.Text", this.ResXFile, this.LangCode);
            this.PortalHasSettingLabel.Text = Localization.GetString(
                "PortalHasSettingLabel.Text", this.ResXFile, this.LangCode);
            this.PortalNoSettingLabel.Text = Localization.GetString(
                "PortalNoSettingLabel.Text", this.ResXFile, this.LangCode);
            this.PageHasSettingLabel.Text = Localization.GetString(
                "PageHasSettingLabel.Text", this.ResXFile, this.LangCode);
            this.PageNoSettingLabel.Text = Localization.GetString(
                "PageNoSettingLabel.Text", this.ResXFile, this.LangCode);
            this.ModuleHasSettingLabel.Text = Localization.GetString(
                "ModuleHasSettingLabel.Text", this.ResXFile, this.LangCode);
            this.ModuleNoSettingLabel.Text = Localization.GetString(
                "ModuleNoSettingLabel.Text", this.ResXFile, this.LangCode);
            this.IconLegendLabel.Text = Localization.GetString(
                "IconLegendLabel.Text", this.ResXFile, this.LangCode);
            this.ModuleInstanceInfo.Text = Localization.GetString("ModuleError.Text", this.ResXFile, this.LangCode);
        }

        /// <summary>
        /// Adds the Java scripts.
        /// </summary>
        private void AddJavaScripts()
        {
            ClientResourceManager.RegisterStyleSheet(
                this.Page,
                "//ajax.googleapis.com/ajax/libs/jqueryui/1.11.2/themes/blitzer/jquery-ui.css");

            ClientResourceManager.RegisterStyleSheet(
                this.Page,
                this.ResolveUrl("~/Providers/HtmlEditorProviders/CKEditor/css/jquery.notification.css"));

            ClientResourceManager.RegisterStyleSheet(
                this.Page,
                this.ResolveUrl("~/Providers/HtmlEditorProviders/CKEditor/css/Options.css"));

            this.pageType = typeof(Page);

            var scriptHolder = this.Page.FindControl("SCRIPTS") as PlaceHolder;

            if (scriptHolder != null)
            {
                var jqueryScriptLink = new HtmlGenericControl("script");

                jqueryScriptLink.Attributes["type"] = "text/javascript";
                jqueryScriptLink.Attributes["src"] = "//ajax.googleapis.com/ajax/libs/jquery/1/jquery.min.js";

                scriptHolder.Controls.Add(jqueryScriptLink);

                var jqueryMigrateScriptLink = new HtmlGenericControl("script");

                jqueryMigrateScriptLink.Attributes["type"] = "text/javascript";
                jqueryMigrateScriptLink.Attributes["src"] = "//code.jquery.com/jquery-migrate-1.1.0.min.js";

                scriptHolder.Controls.Add(jqueryMigrateScriptLink);

                var jqueryUiScriptLink = new HtmlGenericControl("script");

                jqueryUiScriptLink.Attributes["type"] = "text/javascript";
                jqueryUiScriptLink.Attributes["src"] = "//ajax.googleapis.com/ajax/libs/jqueryui/1.11.2/jquery-ui.min.js";

                scriptHolder.Controls.Add(jqueryUiScriptLink);
            }
            else
            {
                ScriptManager.RegisterClientScriptInclude(
                    this, this.pageType, "jquery.Latest", "//ajax.googleapis.com/ajax/libs/jquery/1/jquery.min.js");

                ScriptManager.RegisterClientScriptInclude(
                        this, this.pageType, "jquery.Migrate", "//code.jquery.com/jquery-migrate-1.0.0.js");

                ScriptManager.RegisterClientScriptInclude(
                    this,
                    this.pageType,
                    "jquery.ui.Latest",
                    "//ajax.googleapis.com/ajax/libs/jqueryui/1.11.2/jquery-ui.min.js");
            }

           ScriptManager.RegisterClientScriptInclude(
                this,
                this.pageType,
                "jquery.notification",
                this.ResolveUrl("~/Providers/HtmlEditorProviders/CKEditor/js/jquery.notification.js"));

            ScriptManager.RegisterClientScriptInclude(
                this,
                this.pageType,
                "OptionsJs",
                this.ResolveUrl("~/Providers/HtmlEditorProviders/CKEditor/js/Options.js"));
        }

        /// <summary>
        /// Renders the Portal <see cref="Tabs"/> and Module Tree
        /// </summary>
        private void BindPortalTabsAndModulesTree()
        {
            this.PortalTabsAndModulesTree.Nodes.Clear();

            var moduleController = new ModuleController();

            var settingsDictionary = Utility.GetEditorHostSettings();

            if (this.PortalOnly.Checked)
            {
                this.RenderPortalNode(
                    new PortalController().GetPortal(this.PortalSettings.PortalId), moduleController, settingsDictionary);
            }
            else
            {
                foreach (PortalInfo portal in new PortalController().GetPortals())
                {
                    this.RenderPortalNode(portal, moduleController, settingsDictionary);
                }
            }
            
            this.PortalTabsAndModulesTree.DataBind();
        }

        /// <summary>
        /// Renders the <paramref name="portal" /> node.
        /// </summary>
        /// <param name="portal">The <paramref name="portal" />.</param>
        /// <param name="moduleController">The module controller.</param>
        /// <param name="editorHostSettings">The editor host settings.</param>
        private void RenderPortalNode(PortalInfo portal, ModuleController moduleController, List<EditorHostSetting> editorHostSettings)
        {
            var portalKey = $"DNNCKP#{portal.PortalID}#";

            var portalSettingsExists = SettingsUtil.CheckExistsPortalOrPageSettings(editorHostSettings, portalKey);

            // Portals
            var portalNode = new TreeNode
            {
                Text = portal.PortalName,
                Value = $"p{portal.PortalID}",
                ImageUrl =
                    portalSettingsExists
                        ? "../images/PortalHasSetting.png"
                        : "../images/PortalNoSetting.png",
                Expanded = this.PortalOnly.Checked
            };

            foreach (var tabInfo in TabController.GetTabsByParent(-1, portal.PortalID))
            {
                this.RenderTabNode(portalNode, tabInfo, moduleController, editorHostSettings);
            }

            this.PortalTabsAndModulesTree.Nodes.Add(portalNode);
        }

        /// <summary>
        /// Renders the tab node.
        /// </summary>
        /// <param name="parentNode">The parent node.</param>
        /// <param name="tabInfo">The tab info.</param>
        /// <param name="moduleController">The module controller.</param>
        /// <param name="editorHostSettings">The editor host settings.</param>
        private void RenderTabNode(
            TreeNode parentNode,
            TabInfo tabInfo,
            ModuleController moduleController,
            List<EditorHostSetting> editorHostSettings)
        {
            var tabKey = $"DNNCKT#{tabInfo.TabID}#";

            var tabSettingsExists = SettingsUtil.CheckExistsPortalOrPageSettings(editorHostSettings, tabKey);

            // Tabs
            var tabNode = new TreeNode
                              {
                                  Text = tabInfo.TabName,
                                  Value = $"t{tabInfo.TabID}",
                                  ImageUrl =
                                      tabSettingsExists
                                          ? "../images/PageHasSetting.png"
                                          : "../images/PageNoSetting.png"
                              };

            if (tabInfo.HasChildren)
            {
                foreach (var childTab in TabController.GetTabsByParent(tabInfo.TabID, tabInfo.PortalID))
                {
                    this.RenderTabNode(tabNode, childTab, moduleController, editorHostSettings);
                }
            }

            var modules = moduleController.GetTabModules(tabInfo.TabID).Values;

            foreach (var moduleNode in from moduleInfo in modules
                                       let moduleKey = $"DNNCKMI#{moduleInfo.ModuleID}#INS#"
                                       let moduleSettingsExists =
                                           SettingsUtil.CheckExistsModuleSettings(moduleKey, moduleInfo.ModuleID)
                                       select
                                           new TreeNode
                                               {
                                                   Text = moduleInfo.ModuleTitle,
                                                   ImageUrl =
                                                       moduleSettingsExists
                                                           ? "../images/ModuleHasSetting.png"
                                                           : "../images/ModuleNoSetting.png",
                                                   Value = $"m{moduleInfo.ModuleID}"
                                               })
            {
                tabNode.ChildNodes.Add(moduleNode);
            }

            parentNode.ChildNodes.Add(tabNode);
        }
    }
}