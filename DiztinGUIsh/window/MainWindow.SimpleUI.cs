﻿using System;
using System.Windows.Forms;
using Diz.Core.model;
using Diz.Core.util;
using DiztinGUIsh.window.dialog;

namespace DiztinGUIsh.window
{
    public partial class MainWindow
    {
        private void MainWindow_FormClosing(object sender, FormClosingEventArgs e) =>
            e.Cancel = !PromptContinueEvenIfUnsavedChanges();

        private void MainWindow_SizeChanged(object sender, EventArgs e) => UpdatePanels();
        private void MainWindow_ResizeEnd(object sender, EventArgs e) => UpdateDataGridView();
        private void MainWindow_Load(object sender, EventArgs e) => Init();
        private void newProjectToolStripMenuItem_Click(object sender, EventArgs e) => CreateNewProject();
        private void openProjectToolStripMenuItem_Click(object sender, EventArgs e) => OpenProject();

        private void saveProjectToolStripMenuItem_Click(object sender, EventArgs e) => SaveProject();

        private void saveProjectAsToolStripMenuItem_Click(object sender, EventArgs e) => PromptForFilenameToSave();
        private void exportLogToolStripMenuItem_Click(object sender, EventArgs e) => ExportAssembly();
        private void aboutToolStripMenuItem_Click(object sender, EventArgs e) => new About().ShowDialog();
        private void exitToolStripMenuItem_Click(object sender, EventArgs e) => Application.Exit();
        
        private void decimalToolStripMenuItem_Click(object sender, EventArgs e) => 
            UpdateBase(Util.NumberBase.Decimal);

        private void hexadecimalToolStripMenuItem_Click(object sender, EventArgs e) =>
            UpdateBase(Util.NumberBase.Hexadecimal);

        private void binaryToolStripMenuItem_Click(object sender, EventArgs e) => 
            UpdateBase(Util.NumberBase.Binary);
        
        private void importTraceLogBinary_Click(object sender, EventArgs e) => ImportBsnesBinaryTraceLog();
        private void addLabelToolStripMenuItem_Click(object sender, EventArgs e) => BeginAddingLabel();
        private void visualMapToolStripMenuItem_Click(object sender, EventArgs e) => ShowVisualizerForm();
        private void stepOverToolStripMenuItem_Click(object sender, EventArgs e) => Step(LastOffset);
        private void stepInToolStripMenuItem_Click(object sender, EventArgs e) => StepIn(LastOffset);
        private void autoStepSafeToolStripMenuItem_Click(object sender, EventArgs e) => AutoStepSafe(LastOffset);
        private void autoStepHarshToolStripMenuItem_Click(object sender, EventArgs e) => AutoStepHarsh(LastOffset);
        private void gotoToolStripMenuItem_Click(object sender, EventArgs e) => GoTo(PromptForGotoOffset());

        private void gotoIntermediateAddressToolStripMenuItem_Click(object sender, EventArgs e) =>
            GoToIntermediateAddress(LastOffset);

        private void gotoFirstUnreachedToolStripMenuItem_Click(object sender, EventArgs e) => 
            GoToUnreached(true, true);

        private void gotoNearUnreachedToolStripMenuItem_Click(object sender, EventArgs e) =>
            GoToUnreached(false, false);

        private void gotoNextUnreachedToolStripMenuItem_Click(object sender, EventArgs e) => 
            GoToUnreached(false, true);
        
        private void markOneToolStripMenuItem_Click(object sender, EventArgs e) => Mark(LastOffset);
        private void markManyToolStripMenuItem_Click(object sender, EventArgs e) => MarkMany(LastOffset, 7);
        private void setDataBankToolStripMenuItem_Click(object sender, EventArgs e) => MarkMany(LastOffset, 8);
        private void setDirectPageToolStripMenuItem_Click(object sender, EventArgs e) => MarkMany(LastOffset, 9);

        private void toggleAccumulatorSizeMToolStripMenuItem_Click(object sender, EventArgs e) => MarkMany(LastOffset, 10);

        private void toggleIndexSizeToolStripMenuItem_Click(object sender, EventArgs e) => MarkMany(LastOffset, 11);
        private void addCommentToolStripMenuItem_Click(object sender, EventArgs e) => BeginEditingComment();

        private void unreachedToolStripMenuItem_Click(object sender, EventArgs e) =>
            SetMarkerLabel(Data.FlagType.Unreached);

        private void opcodeToolStripMenuItem_Click(object sender, EventArgs e) => SetMarkerLabel(Data.FlagType.Opcode);

        private void operandToolStripMenuItem_Click(object sender, EventArgs e) =>
            SetMarkerLabel(Data.FlagType.Operand);

        private void bitDataToolStripMenuItem_Click(object sender, EventArgs e) =>
            SetMarkerLabel(Data.FlagType.Data8Bit);

        private void graphicsToolStripMenuItem_Click(object sender, EventArgs e) =>
            SetMarkerLabel(Data.FlagType.Graphics);

        private void musicToolStripMenuItem_Click(object sender, EventArgs e) => SetMarkerLabel(Data.FlagType.Music);
        private void emptyToolStripMenuItem_Click(object sender, EventArgs e) => SetMarkerLabel(Data.FlagType.Empty);

        private void bitDataToolStripMenuItem1_Click(object sender, EventArgs e) =>
            SetMarkerLabel(Data.FlagType.Data16Bit);

        private void wordPointerToolStripMenuItem_Click(object sender, EventArgs e) =>
            SetMarkerLabel(Data.FlagType.Pointer16Bit);

        private void bitDataToolStripMenuItem2_Click(object sender, EventArgs e) =>
            SetMarkerLabel(Data.FlagType.Data24Bit);

        private void longPointerToolStripMenuItem_Click(object sender, EventArgs e) =>
            SetMarkerLabel(Data.FlagType.Pointer24Bit);

        private void bitDataToolStripMenuItem3_Click(object sender, EventArgs e) =>
            SetMarkerLabel(Data.FlagType.Data32Bit);

        private void dWordPointerToolStripMenuItem_Click(object sender, EventArgs e) =>
            SetMarkerLabel(Data.FlagType.Pointer32Bit);

        private void textToolStripMenuItem_Click(object sender, EventArgs e) => SetMarkerLabel(Data.FlagType.Text);

        private void binToolStripMenuItem_Click(object sender, System.EventArgs e) => SetMarkerLabel(Data.FlagType.Binary);
        private void fixMisalignedInstructionsToolStripMenuItem_Click(object sender, EventArgs e) =>
            FixMisalignedInstructions();

        private void labelListToolStripMenuItem_Click(object sender, EventArgs e) => ShowCommentList();

        private void openLastProjectAutomaticallyToolStripMenuItem_Click(object sender, EventArgs e) =>
            SaveSettings();

        private void closeProjectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // TODO
        }

        private void referenceView_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e) => SelectOffset((int) e.Node.Tag);

        private void historyView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            SelectOffset((int) e.Node.Tag, -1, false);

        }
        private void referenceView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if((int) e.Node.Tag != SelectedOffset)
                ViewOffset = (int) e.Node.Tag;
            UpdateDataGridView();

        }
        private void importCDLToolStripMenuItem_Click_1(object sender, EventArgs e) => ImportBizhawkCDL();

        private void importBsnesTracelogText_Click(object sender, EventArgs e) => ImportBsnesTraceLogText();

        private void graphicsWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // TODO
            // graphics view window
        }

        private void toolStripOpenLast_Click(object sender, EventArgs e)
        {
            OpenLastProject();
        }

        private void rescanForInOutPointsToolStripMenuItem_Click(object sender, EventArgs e) => RescanForInOut();
        private void importUsageMapToolStripMenuItem_Click_1(object sender, EventArgs e) => ImportBSNESUsageMap();
        private void table_MouseWheel(object sender, MouseEventArgs e) => ScrollTableBy(e.Delta);
        private void toolStripSearchNext_Click(object sender, System.EventArgs e) => SelectOffset(SearchOffset(1));
        private void toolStripSearchPrevious_Click(object sender, System.EventArgs e) => SelectOffset(SearchOffset(-1));

    }
}