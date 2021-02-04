using Diz.Core.export;
using Diz.Core.model;
using Diz.Core.util;
using DiztinGUIsh.controller;
using DiztinGUIsh.Properties;
using System.Collections.Generic;
using System.Windows.Forms;

namespace DiztinGUIsh.window
{
    public partial class MainWindow : Form, IProjectView
    {
        public MainWindow()
        {
            ProjectController = new ProjectController {
                ProjectView = this,
            };

            Document.PropertyChanged += Document_PropertyChanged;
            ProjectController.ProjectChanged += ProjectController_ProjectChanged;

            InitializeComponent();
        }
        
        private void Init()
        {
            InitMainTable();


            UpdatePanels();
            UpdateUiFromSettings();

            if (Settings.Default.OpenLastFileAutomatically)
                OpenLastProject();
        }


        private void Document_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DizDocument.LastProjectFilename))
            {
                UpdateUiFromSettings();
            }
        }

        private void ProjectController_ProjectChanged(object sender, ProjectController.ProjectChangedEventArgs e)
        {
            switch (e.ChangeType)
            {
                case ProjectController.ProjectChangedEventArgs.ProjectChangedType.Saved:
                    OnProjectSaved();
                    break;
                case ProjectController.ProjectChangedEventArgs.ProjectChangedType.Opened:
                    OnProjectOpened(e.Filename);
                    break;
                case ProjectController.ProjectChangedEventArgs.ProjectChangedType.Imported:
                    OnImportedProjectSuccess();
                    break;
                case ProjectController.ProjectChangedEventArgs.ProjectChangedType.Closing:
                    OnProjectClosing();
                    break;
            }

            RebindProject();
        }

        private void OnProjectClosing()
        {
            UpdateSaveOptionStates(saveEnabled: false, saveAsEnabled: false, closeEnabled: false);
        }

        public void OnProjectOpened(string filename)
        {
            if (visualForm != null)
                visualForm.Project = Project;

            // TODO: do this with aliaslist too.

            UpdateSaveOptionStates(saveEnabled: true, saveAsEnabled: true, closeEnabled: true);
            RefreshUi();

            Document.LastProjectFilename = filename; // do this last.
        }

        public void OnProjectOpenFail(string errorMsg)
        {
            Document.LastProjectFilename = "";
            ShowError(errorMsg, "Error opening project");
        }

        public void OnProjectSaved()
        {
            UpdateSaveOptionStates(saveEnabled: true, saveAsEnabled: true, closeEnabled: true);
            UpdateWindowTitle();
        }

        public void OnExportFinished(LogCreator.OutputResult result)
        {
            ShowExportResults(result);
        }

        private LogWriterSettings? PromptForExportSettingsAndConfirmation()
        {
            // TODO: use the controller to update the project settings from a new one we build
            // don't update directly.
            // probably make our Project property be fully readonly/const/whatever [ReadOnly] attribute

            var selectedSettings = ExportDisassembly.ConfirmSettingsAndAskToStart(Project);
            if (!selectedSettings.HasValue)
                return null;

            var settings = selectedSettings.Value;

            ProjectController.UpdateExportSettings(selectedSettings.Value);

            return settings;
        }
        public Data.FlagType GetFlagType(int i)
        {
            switch (i)
            {
                case 1: return Data.FlagType.Opcode;
                case 2: return Data.FlagType.Operand;
                case 3: return Data.FlagType.Data8Bit;
                case 4: return Data.FlagType.Graphics;
                case 5: return Data.FlagType.Music;
                case 6: return Data.FlagType.Empty;
                case 7: return Data.FlagType.Data16Bit;
                case 8: return Data.FlagType.Pointer16Bit;
                case 9: return Data.FlagType.Data24Bit;
                case 10: return Data.FlagType.Pointer24Bit;
                case 11: return Data.FlagType.Data32Bit;
                case 12: return Data.FlagType.Pointer32Bit;
                case 13: return Data.FlagType.Text;
                case 14: return Data.FlagType.Binary;
                case 0: default: return Data.FlagType.Unreached;
            }
        }

        private void labelView_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            int i = 0;
            foreach (int addr in Project.Data.Labels.Keys)
            {
                if(e.RowIndex == i++)
                {
                    e.Value = e.ColumnIndex switch
                    {
                        1 => Project.Data.Labels[addr].Name,
                        2 => Project.Data.Labels[addr].Comment,
                        _ => Util.NumberToBaseString(addr, Util.NumberBase.Hexadecimal, 6)
                    };

                }
            }
        }

    }
}