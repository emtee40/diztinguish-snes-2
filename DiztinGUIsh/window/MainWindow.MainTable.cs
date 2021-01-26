using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Diz.Core.model;
using Diz.Core.util;

namespace DiztinGUIsh.window
{
    // Everything in here should probably go in its own usercontrol for JUST the table.
    // It's a complicated little beast.
    public partial class MainWindow
    {
        // Data offset of the selected row
        public int SelectedOffset => table.CurrentCell != null ? SelectedRow + ViewOffset : ViewOffset;
        public int SelectedColumn => table.CurrentCell != null ? table.CurrentCell.ColumnIndex : ViewOffset;
        public int SelectedRow => table.CurrentCell != null ? table.CurrentCell.RowIndex : ViewOffset;

        private int rowsToShow;
        private int LastOffset = 0;
        private int LastColumn = 0;
        private bool moveWithStep => moveWithStepToolStripMenuItem.Checked;
        private bool findReferencesWithStep => findReferencesWithStepToolStripMenuItem.Checked;

        public void InvalidateTable() => table.Invalidate();
        public string ColumnName(int i) => table.Columns[i].Name.Replace("Column", "").ToLower();

        private void ScrollTableBy(int delta)
        {
            if (Project?.Data == null || Project.Data.GetRomSize() <= 0)
                return;

            var amount = delta / 0x18;
            ViewOffset -= amount;
            UpdateDataGridView();

            if (LastOffset >= ViewOffset && LastOffset < ViewOffset + rowsToShow)
                table.CurrentCell = table.Rows[LastOffset - ViewOffset].Cells[LastColumn];
            else table.CurrentCell = null;
            InvalidateTable();
        }

        private void vScrollBar1_ValueChanged(object sender, EventArgs e)
        {
            ViewOffset = vScrollBar1.Value;
            UpdateDataGridView();

            if (LastOffset >= ViewOffset && LastOffset < ViewOffset + rowsToShow)
                table.CurrentCell = table.Rows[LastOffset - ViewOffset].Cells[LastColumn];
            else table.CurrentCell = null;

            InvalidateTable();
        }

        private void table_MouseUp(object sender, MouseEventArgs e)
        {
            LastOffset = SelectedOffset;
            LastColumn = SelectedColumn;

            ShowReferences(SelectedOffset);

            InvalidateTable();
        }

        private void table_KeyDown(object sender, KeyEventArgs e)
        {
            if (Project?.Data == null || Project.Data.GetRomSize() <= 0) return;

            var offset = LastOffset;
            var newOffset = offset;
            var amount = 1;
            TreeNode tn;

            e.Handled = true;
            SelectOffset(offset, -1, false);
            switch (e.KeyCode)
            {
                case Keys.Home:
                case Keys.End:
                    SelectOffset(e.KeyCode == Keys.End ? Project.Data.GetRomSize() - 1 : 0, -1, false);
                    break;
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Up:
                case Keys.Down:
                    amount = (e.KeyCode == Keys.PageUp || e.KeyCode == Keys.PageDown) ? (e.Alt ? 0x1000 : e.Shift ? 0x8000 : rowsToShow) : 1;
                    if (e.KeyCode == Keys.Up || e.KeyCode == Keys.PageUp) amount *= -1;
                    SelectOffset(offset + amount, -1, false);
                   break;
                case Keys.Left:
                case Keys.Right:
                    amount = LastColumn + (e.KeyCode == Keys.Right ? 1 : -1);
                    if(amount >= 0 && amount < table.ColumnCount)
                        table.CurrentCell = table.Rows[SelectedRow].Cells[amount];
                    break;
                case Keys.A:
                    AutoStepSafe(offset);
                    break;
                case Keys.K:
                    for(int i = 0; i < table.SelectedCells.Count; i++)
                        Mark(table.SelectedCells[i].RowIndex + ViewOffset, i + 1 == table.SelectedCells.Count);
                    break;
                case Keys.L:
                    table.CurrentCell = table.Rows[SelectedRow].Cells[0];
                    table.BeginEdit(true);
                    break;
                case Keys.B:
                    table.CurrentCell = table.Rows[SelectedRow].Cells[8];
                    table.BeginEdit(true);
                    break;
                case Keys.D:
                    table.CurrentCell = table.Rows[SelectedRow].Cells[9];
                    table.BeginEdit(true);
                    break;
                case Keys.M:
                    Project.Data.SetMFlag(offset, !Project.Data.GetMFlag(offset));
                    break;
                case Keys.X:
                    Project.Data.SetXFlag(offset, !Project.Data.GetXFlag(offset));
                    break;
                case Keys.C:
                    table.CurrentCell = table.Rows[SelectedRow].Cells[12];
                    table.BeginEdit(true);
                    break;
                case Keys.R:
                    ShowReferences(SelectedOffset);
                    break;
                case Keys.OemMinus:
                case Keys.Subtract:
                case Keys.Oemplus:
                case Keys.Add:
                    if (historyView.SelectedNode == null) break;
                    tn = e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract ? historyView.SelectedNode.PrevNode : historyView.SelectedNode.NextNode;
                    if (e.Alt) tn = e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract ? historyView.SelectedNode.FirstNode : historyView.SelectedNode.LastNode;
                    if (tn != null) {
                        SelectOffset((int) tn.Tag, -1, false);
                        historyView.SelectedNode = tn;
                    }
                    break;
                case Keys.F2:
                    SelectOffset(SearchOffset(-1));
                    break;
                case Keys.F3:
                    SelectOffset(SearchOffset(1));
                    break;
                default:
                    e.Handled = false;
                    break;
            }

            InvalidateTable();
        }

        private void table_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            var row = e.RowIndex + ViewOffset;
            if (row >= Project.Data.GetRomSize()) return;
            switch (ColumnName(e.ColumnIndex))
            {
                case "label":
                    e.Value = Project.Data.GetLabelName(Project.Data.ConvertPCtoSnes(row));
                    break;
                case "pc":
                    int baddr = Project.Data.GetBaseAddr(row) + Project.Data.CalculateBaseAddr(row), snes = Project.Data.ConvertPCtoSnes(row);
                    
                    e.Value = Util.NumberToBaseString(baddr >= 0 ? baddr : snes, Util.NumberBase.Hexadecimal, 6);
                    break;
                case "base":
                    e.Value = Util.NumberToBaseString(Project.Data.GetBaseAddr(row), Util.NumberBase.Hexadecimal, 6);
                    break;
                case "char":
                    e.Value = (char)Project.Data.GetRomByte(row);
                    break;
                case "hex":
                    e.Value = Util.NumberToBaseString(Project.Data.GetRomByte(row), displayBase);
                    break;
                case "points":
                    e.Value = RomUtil.PointToString(Project.Data.GetInOutPoint(row));
                    break;
                case "instruction":
                    var len = Project.Data.GetInstructionLength(row);
                    e.Value = row + len <= Project.Data.GetRomSize() ? Project.Data.GetInstruction(row, true) : "";
                    break;
                case "ia":
                    var ia = Project.Data.GetIntermediateAddressOrPointer(row);
                    e.Value = ia >= 0 ? Util.NumberToBaseString(ia, Util.NumberBase.Hexadecimal, 6) : "";
                    break;
                case "flag":
                    e.Value = Util.GetEnumDescription(Project.Data.GetFlag(row));
                    break;
                case "db":
                    e.Value = Util.NumberToBaseString(Project.Data.GetDataBank(row), Util.NumberBase.Hexadecimal, 2);
                    break;
                case "dp":
                    e.Value = Util.NumberToBaseString(Project.Data.GetDirectPage(row), Util.NumberBase.Hexadecimal, 4);
                    break;
                case "m":
                    e.Value = RomUtil.BoolToSize(Project.Data.GetMFlag(row));
                    break;
                case "x":
                    e.Value = RomUtil.BoolToSize(Project.Data.GetXFlag(row));
                    break;
                case "comment":
                    e.Value = Project.Data.GetComment(Project.Data.ConvertPCtoSnes(row));
                    break;
            }
        }

        private void table_CellValuePushed(object sender, DataGridViewCellValueEventArgs e)
        {
            string value = e.Value as string;
            int result;
            int row = e.RowIndex + ViewOffset;
            if (row >= Project.Data.GetRomSize()) return;
            switch (ColumnName(e.ColumnIndex))
            {
                case "label":
                    Project.Data.AddLabel(Project.Data.ConvertPCtoSnes(row), new Diz.Core.model.Label() { Name = value }, true);
                    break; // todo (validate for valid label characters)
                case "db":
                    if (int.TryParse(value, NumberStyles.HexNumber, null, out result)) Project.Data.SetDataBank(row, result);
                    break;
                case "dp":
                    if (int.TryParse(value, NumberStyles.HexNumber, null, out result)) Project.Data.SetDirectPage(row, result);
                    break;
                case "base":
                    if (int.TryParse(value, NumberStyles.HexNumber, null, out result)) Project.Data.SetBaseAddr(row, result);
                    break;
                case "m":
                    Project.Data.SetMFlag(row, (value == "8" || value == "M"));
                    break;
                case "x":
                    Project.Data.SetXFlag(row, (value == "8" || value == "X"));
                    break;
                case "comment":
                    Project.Data.AddComment(Project.Data.ConvertPCtoSnes(row), value, true);
                    break;
            }

            table.InvalidateRow(e.RowIndex);
        }

        public void PaintCell(int offset, DataGridViewCellStyle style, int column_index, int selOffset)
        {
            // editable cells show up green
            string column = ColumnName(column_index);
            if (column == "label" || column == "db" || column == "dp" || column == "comment") style.SelectionBackColor = Color.Chartreuse;

            bool diff = Project.Data.GetLabelName(Project.Data.ConvertPCtoSnes(offset)) == "" && (offset > 0 && Project.Data.GetFlag(offset-1) == Project.Data.GetFlag(offset));
            switch (Project.Data.GetFlag(offset))
            {
                case Data.FlagType.Unreached:
                    style.BackColor = Color.LightGray;
                    style.ForeColor = Color.DarkSlateGray;
                    break;
                case Data.FlagType.Opcode:
                    int opcode = Project.Data.GetRomByte(offset);
                    switch (column)
                    {
                        case "points":
                            Data.InOutPoint point = Project.Data.GetInOutPoint(offset);
                            int r = 255, g = 255, b = 255;
                            if ((point & (Data.InOutPoint.EndPoint | Data.InOutPoint.OutPoint)) != 0) g -= 50;
                            if ((point & (Data.InOutPoint.InPoint)) != 0) r -= 50;
                            if ((point & (Data.InOutPoint.ReadPoint)) != 0) b -= 50;
                            style.BackColor = Color.FromArgb(r, g, b);
                            break;
                        case "instruction":
                            if (opcode == 0x40 || opcode == 0xCB || opcode == 0xDB || opcode == 0xF8 // RTI WAI STP SED
                                || opcode == 0xFB || opcode == 0x00 || opcode == 0x02 || opcode == 0x42 // XCE BRK COP WDM
                            ) style.BackColor = Color.Yellow;
                            break;
                        case "db":
                            if (opcode == 0xAB || opcode == 0x44 || opcode == 0x54) // PLB MVP MVN
                                style.BackColor = Color.OrangeRed;
                            else if (opcode == 0x8B) // PHB
                                style.BackColor = Color.Yellow;
                            break;
                        case "dp":
                            if (opcode == 0x2B || opcode == 0x5B) // PLD TCD
                                style.BackColor = Color.OrangeRed;
                            if (opcode == 0x0B || opcode == 0x7B) // PHD TDC
                                style.BackColor = Color.Yellow;
                            break;
                        case "m":
                        case "x":
                            int mask = column == "m" ? 0x20 : 0x10;
                            if (opcode == 0x28 || ((opcode == 0xC2 || opcode == 0xE2) // PLP SEP REP
                                && (Project.Data.GetRomByte(offset + 1) & mask) != 0)) // relevant bit set
                                style.BackColor = Color.OrangeRed;
                            if (opcode == 0x08) // PHP
                                style.BackColor = Color.Yellow;
                            break;
                    }
                    if (opcode == 0x60 || opcode == 0x6B) // RTS RTL
                        style.BackColor = Color.LightGreen;
                    break;
                case Data.FlagType.Operand:
                    style.ForeColor = Color.LightGray;
                    break;
                case Data.FlagType.Graphics:
                    style.BackColor = Color.LightPink;
                    break;
                case Data.FlagType.Music:
                    style.BackColor = Color.PowderBlue;
                    break;
                case Data.FlagType.Data8Bit:
                case Data.FlagType.Data16Bit:
                case Data.FlagType.Data24Bit:
                case Data.FlagType.Data32Bit:
                    style.BackColor = Color.NavajoWhite;
                    if (diff) style.ForeColor = Color.DarkGray;
                    break;
                case Data.FlagType.Pointer16Bit:
                case Data.FlagType.Pointer24Bit:
                case Data.FlagType.Pointer32Bit:
                    style.BackColor = Color.Orchid;
                    //if (diff) style.ForeColor = Color.LightGray;
                    break;
                case Data.FlagType.Text:
                    style.BackColor = Color.Aquamarine;
                    if (diff) style.ForeColor = Color.DarkGray;
                    break;
                case Data.FlagType.Empty:
                    style.BackColor = Color.DarkSlateGray;
                    style.ForeColor = Color.LightGray;
                    break;
                case Data.FlagType.Binary:
                    style.BackColor = Color.Aqua;
                    break;
            }

            if (selOffset >= 0 && selOffset < Project.Data.GetRomSize())
            {
                int ia = Project.Data.ConvertSnesToPc(Project.Data.GetIntermediateAddressOrPointer(offset));
                int sia = Project.Data.ConvertSnesToPc(Project.Data.GetIntermediateAddressOrPointer(selOffset));
                if ((column == "pc" && sia >= 0 && sia == offset) || (column == "ia" && ia >= 0 && ia == selOffset))
                    style.BackColor = Color.DeepPink;
            }
        }

        private void table_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            int row = e.RowIndex + ViewOffset;
            if (row < 0 || row >= Project.Data.GetRomSize()) return;
            PaintCell(row, e.CellStyle, e.ColumnIndex, SelectedOffset);
        }

        public void ShowReferences(int offset, int level = 0)
        {
            if (offset < 0) return;

            int snes = Project.Data.ConvertPCtoSnes(offset), ia = -1;
            referenceView.BeginUpdate();
            if(level == 0) referenceView.Nodes.Clear();

            TreeNode tn = referenceView.Nodes.Add(Util.NumberToBaseString(snes, Util.NumberBase.Hexadecimal, 6) + "  " + Project.Data.GetLabelName(snes));
            tn.Tag = offset;
            level = tn.Index;
            if (offset == SelectedOffset) referenceView.SelectedNode = tn;

            for (int x = 0; x < Project.Data.GetRomSize(); x++)
            {
                //if (Project.Data.GetFlag(x) != Data.FlagType.Opcode) continue;
                ia = Project.Data.GetIntermediateAddressOrPointer(x);
                if (ia == snes)
                {
                    tn = referenceView.Nodes[level].Nodes.Add(Util.NumberToBaseString(Project.Data.ConvertPCtoSnes(x), Util.NumberBase.Hexadecimal, 6) + ": " + Project.Data.GetInstruction(x,true));
                    tn.Tag = x;
                }
                if (referenceView.SelectedNode == null && x == SelectedOffset) referenceView.SelectedNode = tn;
            }
            if (referenceView.Nodes[level].GetNodeCount(false) == 0) referenceView.Nodes[level].Remove();

            ia = Project.Data.GetIntermediateAddressOrPointer(offset);
            if (ia >= 0 && level == 0)
            {
                ShowReferences(Project.Data.ConvertSnesToPc(ia), 1);
            }
            referenceView.ExpandAll();
            referenceView.EndUpdate();
            referenceView.Update();
        }

        public void SelectOffset(int offset, int column = -1, bool record = true)
        {
            if (offset < 0) return;

            if (record)
            {
                if (historyView.Nodes.Count > 100) historyView.Nodes.RemoveAt(0);
                if (historyView.SelectedNode != null)
                    for (int x = historyView.Nodes.Count-1; x > historyView.SelectedNode.Index; x--) historyView.Nodes[x].Remove();
                TreeNode tn = historyView.Nodes.Add(Util.NumberToBaseString(Project.Data.ConvertPCtoSnes(offset), Util.NumberBase.Hexadecimal, 6) + ": " + Project.Data.GetInstruction(offset, true));
                tn.Tag = offset;
                historyView.SelectedNode = tn;
                if(findReferencesWithStep) ShowReferences(offset);
            }

            var col = column == -1 ? LastColumn : column;
            if (offset < ViewOffset)
            {
                ViewOffset = offset;
                UpdateDataGridView();
                table.CurrentCell = table.Rows[0].Cells[col];
            }
            else if (offset >= ViewOffset + rowsToShow)
            {
                ViewOffset = offset - rowsToShow + 1;
                UpdateDataGridView();
                table.CurrentCell = table.Rows[rowsToShow - 1].Cells[col];
            }
            else
            {
                table.CurrentCell = table.Rows[offset - ViewOffset].Cells[col];
            }
            LastOffset = SelectedOffset;
            LastColumn = SelectedColumn;
        }

        private void InitMainTable()
        {
            table.CellValueNeeded += new DataGridViewCellValueEventHandler(table_CellValueNeeded);
            table.CellValuePushed += new DataGridViewCellValueEventHandler(table_CellValuePushed);
            table.CellPainting += new DataGridViewCellPaintingEventHandler(table_CellPainting);

            rowsToShow = ((table.Height - table.ColumnHeadersHeight) / table.RowTemplate.Height);

            // https://stackoverflow.com/a/1506066
            typeof(DataGridView).InvokeMember(
                "DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.SetProperty,
                null,
                table,
                new object[] {true});
        }

        private void BeginEditingComment()
        {
            table.CurrentCell = table.Rows[SelectedRow].Cells[12];
            table.BeginEdit(true);
        }

        private void BeginAddingLabel()
        {
            table.CurrentCell = table.Rows[SelectedRow].Cells[0];
            table.BeginEdit(true);
        }
    }
}