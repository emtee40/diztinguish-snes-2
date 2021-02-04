using Diz.Core.model;
using Diz.Core.util;
using DiztinGUIsh.Properties;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DiztinGUIsh.window
{
    public partial class MainWindow
    {
        private void OpenLastProject()
        {
            if (Document.LastProjectFilename == "")
                return;

            // safeguard: if we crash opening this project,
            // then next time we load make sure we don't try it again.
            // this will be reset later
            var projectToOpen = Document.LastProjectFilename;
            Document.LastProjectFilename = "";

            ProjectController.OpenProject(projectToOpen);
            
            labelView.RowCount = Project.Data.Labels.Count+1;
            labelView.Invalidate();
        }

        private void OpenProject()
        {
            if (!PromptForOpenProjectFilename()) 
                return;

            ProjectController.OpenProject(openProjectFile.FileName);
        }

        private void CreateNewProject()
        {
            if (!PromptContinueEvenIfUnsavedChanges())
                return;

            var romFilename = PromptForOpenFilename();
            if (romFilename == "")
                return;

            ProjectController.ImportRomAndCreateNewProject(openFileDialog.FileName);
        }
        
        private void ExportAssembly()
        {
            var adjustedSettings = PromptForExportSettingsAndConfirmation();
            if (!adjustedSettings.HasValue)
                return;

            ProjectController.UpdateExportSettings(adjustedSettings.Value);
            ProjectController.WriteAssemblyOutput();
        }


        private void Step(int offset)
        {
            if (!RomDataPresent()) 
                return;
            
            ProjectController.MarkChanged();
            SelectOffset(Project.Data.Step(offset, false, false, offset - 1));
            UpdateUI_Tmp3();
        }

        private void StepIn(int offset)
        {
            if (!RomDataPresent()) 
                return;
            
            ProjectController.MarkChanged();
            SelectOffset(Project.Data.Step(offset, true, false, historyView.SelectedNode != null ? (int) historyView.SelectedNode.Tag : offset - 1));
            UpdateUI_Tmp3();
        }

        private void AutoStepSafe(int offset)
        {
            if (!RomDataPresent()) 
                return;
            
            ProjectController.MarkChanged();
            var destination = Project.Data.AutoStep(offset, false, 0);
            if (moveWithStep) 
                SelectOffset(destination);
            
            UpdateUI_Tmp3();
        }

        private void AutoStepHarsh(int offset)
        {
            if (!RomDataPresent()) 
                return;
            
            if (!PromptHarshAutoStep(offset, out var newOffset, out var count))
                return;

            ProjectController.MarkChanged();
            var destination = Project.Data.AutoStep(newOffset, true, count);
            
            if (moveWithStep) 
                SelectOffset(destination);

            UpdateUI_Tmp3();
        }

        private void Mark(int offset, bool select = true)
        {
            if (!RomDataPresent())
                return;

            ProjectController.MarkChanged();
            var newOffset = Project.Data.MarkTypeFlag(offset, markFlag, RomUtil.GetByteLengthForFlag(markFlag));

            if (select) SelectOffset(newOffset);

            UpdateUI_Tmp3();
        }

        private void MarkMany(int offset, string column)
        {
            if (!RomDataPresent() || table.IsCurrentCellInEditMode) 
                return;
            
            var mark = PromptMarkMany(offset, column);
            if (mark == null)
                return;

            MarkMany(mark.Property, mark.Start, mark.Value, mark.Count, mark.UnreachedOnly);

            UpdateSomeUI2();
        }

        private void MarkMany(int markProperty, int markStart, object markValue, int markCount, bool markUnreachedOnly = false)
        {
            var destination = markProperty switch
            {
                0 => Project.Data.MarkTypeFlag(markStart, (Data.FlagType) markValue, markCount, markUnreachedOnly),
                1 => Project.Data.MarkDataBank(markStart, (int) markValue, markCount, markUnreachedOnly),
                2 => Project.Data.MarkDirectPage(markStart, (int) markValue, markCount, markUnreachedOnly),
                3 => Project.Data.MarkMFlag(markStart, (bool) markValue, markCount, markUnreachedOnly),
                4 => Project.Data.MarkXFlag(markStart, (bool) markValue, markCount, markUnreachedOnly),
                5 => Project.Data.MarkBaseAddr(markStart, (int)markValue, markCount, markUnreachedOnly),
                6 => Project.Data.MarkArchitecture(markStart, (Data.Architecture) markValue, markCount, markUnreachedOnly),
                _ => 0
            };

            ProjectController.MarkChanged();

            if (moveWithStep)
                SelectOffset(destination);
        }

        private void GoToIntermediateAddress(int offset)
        {
            var snesOffset = FindIntermediateAddress(offset);
            if (snesOffset == -1)
                return;

            SelectOffset(snesOffset, 1);
        }

        private void GoTo(int offset)
        {
            if (IsOffsetInRange(offset))
                SelectOffset(offset);
            else
                ShowOffsetOutOfRangeMsg();
        }

        private void GoToUnreached(bool end, bool direction)
        {
            if (!FindUnreached(SelectedOffset, end, direction, out var unreached))
                return;
            
            SelectOffset(unreached, 1);
        }


        private void FixMisalignedInstructions()
        {
            if (!PromptForMisalignmentCheck())
                return;

            var count = Project.Data.FixMisalignedFlags();

            if (count > 0)
                ProjectController.MarkChanged();
            InvalidateTable();
            
            ShowInfo($"Modified {count} flags!", "Done!");
        }

        private void RescanForInOut()
        {
            if (!PromptForInOutChecking()) 
                return;

            Project.Data.RescanInOutPoints();
            ProjectController.MarkChanged();
            
            InvalidateTable();
            ShowInfo("Scan complete!", "Done!");
        }

        private void SaveProject()
        {
            ProjectController.SaveProject(Project.ProjectFileName);
        }

        private void ShowVisualizerForm()
        {
            visualForm ??= new VisualizerForm(this);
            visualForm.Show();
        }

        private void ShowCommentList()
        {
            AliasList.Show();
        }

        private void SetMarkerLabel(Data.FlagType flagType)
        {
            markFlag = flagType;
            UpdateMarkerLabel();
            if (autoMarkToolStripMenuItem.Checked)
            for (int i = 0; i < table.SelectedCells.Count; i++)
                Mark(table.SelectedCells[i].RowIndex + ViewOffset, i + 1 == table.SelectedCells.Count);
        }

        private void SaveSettings()
        {
            Settings.Default.MoveWithStep = moveWithStepToolStripMenuItem.Checked;
            Settings.Default.FindReferencesWithStep = findReferencesWithStepToolStripMenuItem.Checked;
            Settings.Default.OpenLastFileAutomatically = openLastProjectAutomaticallyToolStripMenuItem.Checked;
            Settings.Default.Save();
            UpdateUiFromSettings();
        }
        public int SearchOffset(int direction = 1)
        {
            direction = direction > 0 ? 1 : -1;
            int offset = SelectedOffset > 0 ? SelectedOffset : 0;
            Data.FlagType flag = Project.Data.GetFlag(offset), current;
            try
            {
                if (toolStripFlagType.SelectedIndex > 0)
                {
                    flag = GetFlagType(toolStripFlagType.SelectedIndex - 1);
                }

                if (toolStripSearchBox.Text.Length > 0)
                {
                    while ((offset += direction) > 0 && offset < Project.Data.GetRomSize())
                    {
                        if (toolStripFlagType.SelectedIndex > 0 && Project.Data.GetFlag(offset) != flag) continue;
                        if (Regex.IsMatch(Project.Data.GetInstruction(offset, true), toolStripSearchBox.Text)) break;
                    }
                    return offset;
                }

                if (toolStripFlagType.SelectedIndex > 0)
                {
                    if (direction > 0)
                    {
                        while (++offset < Project.Data.GetRomSize() && Project.Data.GetFlag(offset) == flag) continue;
                        while (++offset < Project.Data.GetRomSize() && Project.Data.GetFlag(offset) != flag) continue;
                    }
                    else
                    {
                        while (--offset >= 0 && Project.Data.GetFlag(offset) != flag) continue;
                        while (--offset >= 0 && Project.Data.GetFlag(offset) == flag) continue;
                        offset++;
                    }
                }
                else
                {
                    while ((offset += direction) > 0 && offset < Project.Data.GetRomSize())
                    {
                        current = Project.Data.GetFlag(offset);
                        if (flag == Data.FlagType.Opcode && current == Data.FlagType.Operand) continue;
                        if (flag == Data.FlagType.Operand && current == Data.FlagType.Opcode) continue;
                        if (flag != current) break;
                    }
                }

            }
            catch (System.Exception exception)
            {
                ShowError(exception.Message);
            }
            return offset >= 0 && offset < Project.Data.GetRomSize() ? offset : -1;
        }
    }
}