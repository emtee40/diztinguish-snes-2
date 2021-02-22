using System;
using System.Collections.Generic;
using Microsoft.VisualBasic;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Diz.Core.model;
using Diz.Core.util;
using Label = Diz.Core.model.Label;

namespace DiztinGUIsh.window
{
    // Everything in here should probably go in its own usercontrol for JUST the table.
    // It's a complicated little beast.
    public partial class MainWindow
    {
        // Data offset of the selected row
        public int SelectedOffset => table.CurrentCell != null ? SelectedRow + ViewOffset : ViewOffset;
        public int SelectedColumn => table.CurrentCell != null ? table.CurrentCell.ColumnIndex : -1;
        public int SelectedRow => table.CurrentCell != null ? table.CurrentCell.RowIndex : 0;

        private int rowsToShow;
        private int LastOffset = 0;
        private int LastColumn = 0;
        private bool moveWithStep => moveWithStepToolStripMenuItem.Checked;
        private bool findReferencesWithStep => findReferencesWithStepToolStripMenuItem.Checked;

        public void InvalidateTable() => table.Invalidate();
        public string ColumnName(int i) => table.Columns[i].Name.Replace("Column", "").ToLower();
        public int ColumnIndex(string name) {
            for (int i = 0; i < table.Columns.Count; i++)
                if (table.Columns[i].Name.Replace("Column", "").ToLower() == name)
                    return i;
            return 0;
        }

        private void ScrollTableBy(int delta)
        {
            if (Project?.Data == null || Project.Data.GetRomSize() <= 0)
                return;

            if (table.IsCurrentCellInEditMode) table.EndEdit();

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

        private void table_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right && LastOffset != SelectedOffset)
                ShowReferences(SelectedOffset);
            SelectOffset(SelectedOffset, SelectedColumn, false);

            InvalidateTable();
        }

        private void table_KeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            if (Project?.Data == null || Project.Data.GetRomSize() <= 0 || table.IsCurrentCellInEditMode) return;

            var offset = LastOffset;
            var newOffset = offset;
            var amount = 1;
            TreeNode tn;

            //SelectOffset(offset, -1, false);
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
                    if (e.Control)
                    {
                        ScrollTableBy(amount * -0x18);
                    }
                    else
                    {
                        SelectOffset(offset + amount, -1, false);
                    }
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
                    int ia = Project.Data.GetIntermediateAddressOrPointer(offset, e.Shift);

                    if (e.Control && ia >= 0)
                    {
                        string label = Interaction.InputBox("Type the label name you want for the address " + Util.NumberToBaseString(ia, Util.NumberBase.Hexadecimal, 6, true), "Add/Update Label", Project.Data.GetLabelName(ia));
                        Project.Data.AddLabel(ia, new Label() { Name = Regex.IsMatch(label, ".?[a-zA-Z0-9_]+|[\\+\\-]+") ? label : "" }, true);
                    }
                    table.CurrentCell = table.Rows[SelectedRow].Cells[ColumnIndex("label")];
                    table.BeginEdit(true);
                    break;
                case Keys.B:
                    table.CurrentCell = table.Rows[SelectedRow].Cells[ColumnIndex("db")];
                    table.BeginEdit(true);
                    break;
                case Keys.D:
                    table.CurrentCell = table.Rows[SelectedRow].Cells[ColumnIndex("dp")];
                    table.BeginEdit(true);
                    break;
                case Keys.M:
                    for (int i = 0; i < table.SelectedCells.Count; i++)
                        Project.Data.ToggleMFlag(table.SelectedCells[i].RowIndex + ViewOffset);
                    break;
                case Keys.X:
                    for (int i = 0; i < table.SelectedCells.Count; i++)
                        Project.Data.ToggleXFlag(table.SelectedCells[i].RowIndex + ViewOffset);
                    break;
                case Keys.C:
                    table.CurrentCell = table.Rows[SelectedRow].Cells[ColumnIndex("comment")];
                    table.BeginEdit(true);
                    break;
                case Keys.R:
                    ShowReferences(SelectedOffset);
                    break;
                case Keys.OemMinus:
                case Keys.Subtract:
                case Keys.Oemplus:
                case Keys.Add:
                    if (e.Shift) { historyView.Nodes.Clear(); break; }
                    if (historyView.SelectedNode == null) break;
                    tn = e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract ? historyView.SelectedNode.PrevNode : historyView.SelectedNode.NextNode;
                    if (e.Alt) tn = e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract ? historyView.Nodes[0] : historyView.Nodes[historyView.Nodes.Count-1];
                    if (tn != null) {
                        SelectOffset((int) tn.Tag, -1, false);
                        historyView.SelectedNode = tn;
                    }
                    break;
                case Keys.F3:
                    SelectOffset(SearchOffset(e.Shift ? -1 : 1));
                    break;
                case Keys.Enter:
                    LastOffset = SelectedOffset+1;
                    e.Handled = false;
                    break;
                case Keys.F9:
                    GoToIntermediateAddress(LastOffset, e.Control);
                    break;
                default:
                    e.Handled = false;
                    break;
            }

            InvalidateTable();
        }

        private void table_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            int row = e.RowIndex + ViewOffset, snes = Project.Data.ConvertPCtoSnes(row);
            if (row >= Project.Data.GetRomSize()) return;
            bool editing = table.IsCurrentCellInEditMode && e.ColumnIndex == SelectedColumn, samerow = row == SelectedOffset;
            int ia = Project.Data.GetIntermediateAddressOrPointer(row, true);
            switch (ColumnName(e.ColumnIndex))
            {
                case "label":
                    var label = Project.Data.GetLabelName(snes);
                    if (label == "" && (Project.Data.GetInOutPoint(row) & Data.InOutPoint.InPoint) == Data.InOutPoint.InPoint)
                        label = Project.Data.GetLabelName(snes, true);

                    e.Value = label;
                    break;
                case "pc":                    
                    e.Value = Util.NumberToBaseString(Project.Data.ConvertPCtoSnes(row), Util.NumberBase.Hexadecimal, 6);
                    break;
                case "base":
                    int baddr = editing ? Project.Data.GetBaseAddr(row) : Project.Data.CalculateBaseAddr(row);
                    e.Value = baddr > 0 ? Util.NumberToBaseString(baddr, Util.NumberBase.Hexadecimal, 6) : "";
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
                    e.Value = row + len <= Project.Data.GetRomSize() ? Project.Data.GetInstruction(row, true).Truncate(200) : "";
                    break;
                case "ia":
                    if (Project.Data.GetIndirectAddr(row) > 0 && (ia < 0 || (editing && samerow) || samerow)) ia = Project.Data.GetIndirectAddr(row);
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
                case "constant":
                    e.Value = Project.Data.GetFlag(row) != Data.FlagType.Operand ? Util.GetEnumDescription(Project.Data.GetConstantType(row)).Truncate(1) : "-";
                    break;
                case "comment":
                    e.Value = Project.Data.GetComment(Project.Data.ConvertPCtoSnes(row));
                    if (e.Value == "" && ia >= 0 && Project.Data.GetComment(ia) != "" && ((!editing && !samerow) || !samerow)) e.Value = Project.Data.GetComment(ia);
                    break;
            }
        }

        private void table_CellValuePushed(object sender, DataGridViewCellValueEventArgs e)
        {
            string value = e.Value as string;
            int result, row = e.RowIndex + ViewOffset;
            if (row >= Project.Data.GetRomSize()) return;
            switch (ColumnName(e.ColumnIndex))
            {
                case "label":
                    if (value != null && !Regex.IsMatch(value, ".?[a-zA-Z0-9_]+|[\\+\\-]+")) value = null;
                    Project.Data.AddLabel(Project.Data.ConvertPCtoSnes(row), new Diz.Core.model.Label() { Name = value }, true);
                    break;
                case "db":
                    Project.Data.SetDataBank(row, int.TryParse(value, NumberStyles.HexNumber, null, out result) ? result : 0);
                    break;
                case "dp":
                    Project.Data.SetDirectPage(row, int.TryParse(value, NumberStyles.HexNumber, null, out result) ? result : 0);
                    break;
                case "base":
                    Project.Data.SetBaseAddr(row, int.TryParse(value, NumberStyles.HexNumber, null, out result) ? result : 0);
                    break;
                case "ia":
                    Project.Data.SetIndirectAddr(row, int.TryParse(value, NumberStyles.HexNumber, null, out result) && result != Project.Data.GetIntermediateAddressOrPointer(row, true) ? result : 0);
                    break;
                case "m":
                    Project.Data.SetMFlag(row, (value == "8" || value.ToUpper() == "M"));
                    break;
                case "x":
                    Project.Data.SetXFlag(row, (value == "8" || value.ToUpper() == "X"));
                    break;
                case "constant":
                    Data.ConstantType ctype = value.ToUpper() switch
                    {
                        "D" => Data.ConstantType.Decimal,
                        "B" => Data.ConstantType.Binary,
                        "T" => Data.ConstantType.Text,
                        _ => Data.ConstantType.Hexadecimal
                    };
                    Project.Data.SetConstantType(row, ctype);
                    break;
                case "comment":
                    Project.Data.AddComment(Project.Data.ConvertPCtoSnes(row), value, true);
                    break;
            }

            InvalidateTable();
        }

        public void PaintCell(int offset, DataGridViewCellStyle style, int column_index, int selOffset, DataGridViewCellPaintingEventArgs cell)
        {
            // editable cells show up green
            string column = ColumnName(column_index);
            if (column == "label" || column == "base" || column == "db" || column == "dp" || column == "ia" || column == "comment") style.SelectionBackColor = Color.Chartreuse;

            bool diff = Project.Data.GetLabelName(Project.Data.ConvertPCtoSnes(offset)) == "" && (offset > 0 && Project.Data.GetFlag(offset - 1) == Project.Data.GetFlag(offset));
            Data.InOutPoint point = Project.Data.GetInOutPoint(offset);
            Data.FlagType flag = Project.Data.GetFlag(offset);
            Data.ConstantType constant = Project.Data.GetConstantType(offset);
            switch (flag)
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
                            if (constant != Data.ConstantType.Hexadecimal)
                            {
                                style.BackColor = Color.FromArgb(50 - (int)constant, 100 - (int)constant, 255 - ((int)constant * 20));
                                style.ForeColor = Color.White;
                            }
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
                    switch (opcode)
                    {
                        case 0x4C: case 0x5C: case 0x6C: case 0x7C: case 0xDC:
                            style.BackColor = Color.WhiteSmoke;
                            break;
                        case 0x60: case 0x6B:
                            style.BackColor = Color.LightGreen;
                            break;
                    }
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
                    if (column == "ia" && Project.Data.GetConstantType(offset) == Data.ConstantType.Color && (point & Data.InOutPoint.ReadPoint) > 0)
                    {
                        if (flag == Data.FlagType.Data16Bit)
                            style.BackColor = Util.ColorRGB555(Project.Data.GetRomWord(offset));
                        else if (flag == Data.FlagType.Data24Bit)
                            style.BackColor = Color.FromArgb(Project.Data.GetRomByte(offset+2), Project.Data.GetRomByte(offset+1), Project.Data.GetRomByte(offset));
                    }
                    if (RomUtil.GetByteLengthForFlag(flag) > 1 && (point & Data.InOutPoint.ReadPoint) == 0) style.ForeColor = Color.DarkGray;
                    break;
                case Data.FlagType.Pointer16Bit:
                case Data.FlagType.Pointer24Bit:
                case Data.FlagType.Pointer32Bit:
                    style.BackColor = Color.Orchid;
                    if ((point & (Data.InOutPoint.ReadPoint | Data.InOutPoint.EndPoint)) == 0) style.ForeColor = Color.DarkMagenta;
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

            int ia = Project.Data.ConvertSnesToPc(Project.Data.GetIntermediateAddressOrPointer(offset));
            int sia = Project.Data.ConvertSnesToPc(Project.Data.GetIntermediateAddressOrPointer(selOffset));
            if (selOffset >= 0 && selOffset < Project.Data.GetRomSize())
            {
                if ((column == "pc" && sia >= 0 && sia == offset) || (column == "ia" && ia >= 0 && ia == selOffset))
                    style.BackColor = Color.DeepPink;
            }


            switch (column)
            {
                case "label":
                    style.ForeColor = Project.Data.GetLabelName(Project.Data.ConvertPCtoSnes(offset)) == "" ? Color.LightGray : style.ForeColor;
                    break;
                case "base":
                    style.ForeColor = Project.Data.GetBaseAddr(offset) > 0 /*&& Project.Data.GetFlag(offset) != Data.FlagType.Operand*/ ? Color.DarkBlue : Color.LightGray;
                    break;
                case "ia":
                    if (Project.Data.GetIndirectAddr(offset) > 0 /*&& Project.Data.GetFlag(offset) != Data.FlagType.Operand*/) style.ForeColor = Color.Red;
                    break;
                case "constant":
                    if (constant != Data.ConstantType.Hexadecimal)
                    {
                        style.BackColor = Color.FromArgb(50 - (int)constant, 100 - (int)constant, 255 - ((int)constant * 20));
                        style.ForeColor = Color.White;
                    }
                    break;
                case "comment":
                    if(Project.Data.GetComment(Project.Data.ConvertPCtoSnes(offset)) == "") style.ForeColor = Color.LightGray;
                    break;
            }

            cell.PaintBackground(cell.CellBounds, true);
            if (sia >= 0)
            {
                if (column == "pc" && ((sia > selOffset && offset < sia && offset > selOffset) || (sia < selOffset && offset > sia && offset <= selOffset)))
                    cell.Graphics.DrawLine(new Pen(Color.DeepPink, 2F), cell.CellBounds.Right - 2, cell.CellBounds.Top, cell.CellBounds.Right - 2, cell.CellBounds.Bottom);
                if (((sia >= 0 && selOffset == offset-1) || (ia >= 0 && ia == offset)) && column_index > ColumnIndex("pc") && column_index <= ColumnIndex("ia"))
                    cell.Graphics.DrawLine(Pens.DeepPink, cell.CellBounds.Left, cell.CellBounds.Top, cell.CellBounds.Right, cell.CellBounds.Top);
                
            }
            
            cell.Paint(cell.ClipBounds, DataGridViewPaintParts.ContentForeground);
            cell.Handled = true;
        }
        private void ToggleMenuShortcuts(bool enable)
        {
            for (int x = 0; x < editToolStripMenuItem.DropDownItems.Count; x++)
                editToolStripMenuItem.DropDownItems[x].Enabled = enable;
        }
        private void table_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e) => ToggleMenuShortcuts(false);
        private void table_CellEndEdit(object sender, DataGridViewCellEventArgs e) => ToggleMenuShortcuts(true);

        private void table_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            int row = e.RowIndex + ViewOffset;
            if (row < 0 || row >= Project.Data.GetRomSize()) return;
            PaintCell(row, e.CellStyle, e.ColumnIndex, LastOffset, e);
        }

        public void ShowReferences(int offset, bool pc = true, bool clear = true)
        {

            int snes = pc ? Project?.Data?.ConvertPCtoSnes(offset) ?? -1 : offset, ia = -1;
            if (snes < 0) return;
            referenceView.BeginUpdate();
            if(clear) referenceView.Nodes.Clear();

            TreeNode tn = referenceView.Nodes.Add(Util.NumberToBaseString(snes, Util.NumberBase.Hexadecimal, 6) + "  " + Project.Data.GetLabelName(snes));
            tn.Tag = Project.Data.ConvertSnesToPc(snes);
            int level = tn.Index;
            if ((int) tn.Tag == SelectedOffset) referenceView.SelectedNode = tn;

            for (int x = 0; x < Project.Data.GetRomSize(); x++)
            {
                //if (Project.Data.GetFlag(x) != Data.FlagType.Opcode) continue;
                ia = Project.Data.GetIntermediateAddressOrPointer(x);
                if (ia == snes && Project.Data.GetFlag(x) != Data.FlagType.Operand)
                {
                    tn = referenceView.Nodes[level].Nodes.Add(Util.NumberToBaseString(Project.Data.ConvertPCtoSnes(x), Util.NumberBase.Hexadecimal, 6) + ": " + Project.Data.GetInstruction(x,true).Truncate(100));
                    tn.Tag = x;
                    if (referenceView.SelectedNode == null && x == SelectedOffset)
                        referenceView.SelectedNode = tn;
                }
            }
            if (referenceView.Nodes[level].GetNodeCount(false) == 0) referenceView.Nodes[level].Remove();

            ia = pc && offset >= 0 ? Project.Data.GetIntermediateAddressOrPointer(offset) : -1;
            if (ia >= 0)
                ShowReferences(ia, false, false);

            referenceView.ExpandAll();
            referenceView.EndUpdate();
            if(referenceView.SelectedNode != null)
                referenceView.SelectedNode.EnsureVisible();
        }

        public void SelectOffset(int offset, int column = -1, bool record = true)
        {
            if (offset < 0 || offset >= Project.Data.GetRomSize()) return;
            currentOffset.Text = "Offset: " + Util.NumberToBaseString(Project.Data.ConvertPCtoSnes(offset), Util.NumberBase.Hexadecimal, 6, true) + "/0x" + Util.NumberToBaseString(offset, Util.NumberBase.Hexadecimal, 6);

            if (record)
            {
                historyView.BeginUpdate();
                TreeNode tn;
                if (historyView.Nodes.Count > 100) historyView.Nodes.RemoveAt(0);
                if (historyView.SelectedNode != null)
                    for (int x = historyView.Nodes.Count-1; x > historyView.SelectedNode.Index; x--) historyView.Nodes[x].Remove();
                if (historyView.SelectedNode == null || (int) historyView.SelectedNode.Tag != SelectedOffset)
                {
                    tn = historyView.Nodes.Add(Util.NumberToBaseString(Project.Data.ConvertPCtoSnes(SelectedOffset), Util.NumberBase.Hexadecimal, 6) + ": " + Project.Data.GetInstruction(SelectedOffset, true).Truncate(100));
                    tn.Tag = SelectedOffset;
                }
                tn = historyView.Nodes.Add(Util.NumberToBaseString(Project.Data.ConvertPCtoSnes(offset), Util.NumberBase.Hexadecimal, 6) + ": " + Project.Data.GetInstruction(offset, true).Truncate(100));
                tn.Tag = offset;
                historyView.SelectedNode = tn;
                if(findReferencesWithStep) ShowReferences(offset);
                if (historyView.SelectedNode != null) 
                    historyView.SelectedNode.EnsureVisible();
                historyView.EndUpdate();
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
            table.CellBeginEdit += new DataGridViewCellCancelEventHandler(table_CellBeginEdit);
            table.CellEndEdit += new DataGridViewCellEventHandler(table_CellEndEdit);
            table.MouseClick += new MouseEventHandler(table_MouseDown);
            table.KeyDown += new KeyEventHandler(table_KeyDown);


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
            table.CurrentCell = table.Rows[SelectedRow].Cells[ColumnIndex("comment")];
            table.BeginEdit(true);
        }

        private void BeginAddingLabel()
        {
            table.CurrentCell = table.Rows[SelectedRow].Cells[ColumnIndex("label")];
            table.BeginEdit(true);
        }
    }
}