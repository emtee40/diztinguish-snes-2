using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Diz.Core.model;
using Diz.Core.util;
using DiztinGUIsh.controller;
using Label = Diz.Core.model.Label;

namespace DiztinGUIsh.window
{
    // Everything in here should probably go in its own usercontrol for JUST the table.
    // It's a complicated little beast.
    public partial class MainWindow
    {

        public bool Locked;
        private int currentlyEditing = -1;


        private void labelView_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (ModifierKeys != Keys.Alt) return;

            if (!int.TryParse((string) labelView.SelectedRows[0].Cells[0].Value, NumberStyles.HexNumber, null,
                out var val)) return;

            var offset = Project.Data.ConvertSnesToPc(val);
            if (offset >= 0)
                SelectOffset(offset);
        }

        private static void SplitOnFirstComma(string instr, out string firstPart, out string remainder)
        {
            if (!instr.Contains(","))
            {
                firstPart = instr;
                remainder = "";
                return;
            }

            firstPart = instr.Substring(0, instr.IndexOf(','));
            remainder = instr.Substring(instr.IndexOf(',') + 1);
        }

        private void ImportLabelsFromCsv(bool replaceAll)
        {
            OpenFileDialog open = new OpenFileDialog();
            open.Filter = "Comma Separated Value Files|*.csv|Text Files|*.txt|All Files|*.*";

            var result = open.ShowDialog();
            if (result != DialogResult.OK || open.FileName == "")
                return;

            var errLine = 0;
            try
            {
                var newValues = new Dictionary<int, Label>();
                var lines = Util.ReadLines(open.FileName).ToArray();

                var validLabelChars = new Regex(@"^([a-zA-Z0-9_\-]*)$");

                // NOTE: this is kind of a risky way to parse CSV files, won't deal with weirdness in the comments
                // section.
                for (var i = 0; i < lines.Length; i++)
                {
                    var label = new Label();

                    errLine = i + 1;

                    SplitOnFirstComma(lines[i], out var labelAddress, out var remainder);
                    SplitOnFirstComma(remainder, out label.Name, out label.Comment);

                    label.CleanUp();

                    label.Name = label.Name.Trim();
                    if (!validLabelChars.Match(label.Name).Success)
                        throw new InvalidDataException("invalid label name: " + label.Name);

                    newValues.Add(int.Parse(labelAddress, NumberStyles.HexNumber, null), label);
                }

                // everything read OK, modify the existing list now. point of no return
                if (replaceAll)
                    Project.Data.DeleteAllLabels();

                ClearAndInvalidateDataGrid();

                // this will call AddRow() to add items back to the UI datagrid.
                foreach (var pair in newValues)
                {
                    Project.Data.AddLabel(pair.Key, pair.Value, true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "An error occurred while parsing the file.\n" + ex.Message +
                    (errLine > 0 ? $" (Check line {errLine}.)" : ""),
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void exportLabelsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog save = new SaveFileDialog();
            save.Filter = "Comma Separated Value Files|*.csv|Text Files|*.txt|All Files|*.*";
            var result = save.ShowDialog();
            if (result != DialogResult.OK || save.FileName == "") return;
            
            try
            {
                using var sw = new StreamWriter(save.FileName);
                foreach (var pair in Project.Data.Labels)
                {
                    sw.WriteLine(
                        $"{Util.NumberToBaseString(pair.Key, Util.NumberBase.Hexadecimal, 6)},{pair.Value.Name},{pair.Value.Comment}");
                }
            } catch (Exception)
            {
                MessageBox.Show("An error occurred while saving the file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void labelView_UserDeletingRow(object sender, DataGridViewRowCancelEventArgs e)
        {
            if (!int.TryParse((string) labelView.Rows[e.Row.Index].Cells[0].Value, NumberStyles.HexNumber, null,
                out var val)) return;
            Locked = true;
            Project.Data.AddLabel(val, null, true);
            Locked = false;
            InvalidateTable(); // TODO: move to mainwindow, use notifychanged in mainwindow for this
        }

        private void labelView_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            currentlyEditing = e.RowIndex;

            // start by entering an address first, not the label
            if (labelView.Rows[e.RowIndex].IsNewRow && e.ColumnIndex == 1)
            {
                labelView.CurrentCell = labelView.Rows[e.RowIndex].Cells[0];
            }
        }

        private void labelView_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (labelView.Rows[e.RowIndex].IsNewRow) return;
            var val = -1;
            int.TryParse((string)labelView.Rows[e.RowIndex].Cells[0].Value, NumberStyles.HexNumber, null, out var oldAddress);

            var labelLabel = new Label
            {
                Name = (string) labelView.Rows[e.RowIndex].Cells[1].Value,
                Comment = (string)labelView.Rows[e.RowIndex].Cells[2].Value,
            };

            //toolStripStatusLabel1.Text = "";

            switch (e.ColumnIndex)
            {
                case 0:
                    {
                        if (!int.TryParse(e.FormattedValue.ToString(), NumberStyles.HexNumber, null, out val))
                        {
                            e.Cancel = true;
                            //toolStripStatusLabel1.Text = "Must enter a valid hex address.";
                        } else if (oldAddress == -1 && Project.Data.Labels.ContainsKey(val))
                        {
                            e.Cancel = true;
                            //toolStripStatusLabel1.Text = "This address already has a label.";

                            Console.WriteLine(Util.NumberToBaseString(val, Util.NumberBase.Hexadecimal));
                        } else if (labelView.EditingControl != null)
                        {
                            labelView.EditingControl.Text = Util.NumberToBaseString(val, Util.NumberBase.Hexadecimal, 6);
                        }
                        break;
                    }
                case 1:
                    {
                        val = oldAddress;
                        labelLabel.Name = e.FormattedValue.ToString();
                        // todo (validate for valid label characters)
                        break;
                    }
                case 2:
                    {
                        val = oldAddress;
                        labelLabel.Comment = e.FormattedValue.ToString();
                        // todo (validate for valid comment characters, if any)
                        break;
                    }
            }

            Locked = true;
            if (currentlyEditing >= 0)
            {
                if (val >= 0) Project.Data.AddLabel(oldAddress, null, true);
                Project.Data.AddLabel(val, labelLabel, true);
            }
            Locked = false;

            currentlyEditing = -1;
            InvalidateTable();  // TODO: move to mainwindow, use notifychanged in mainwindow for this
        }

        public void AddRow(int address, Label alias)
        {
            if (Locked) 
                return;
            RawAdd(address, alias);
            labelView.Invalidate();
        }

        private void RawAdd(int address, Label alias)
        {
            labelView.Rows.Add(Util.NumberToBaseString(address, Util.NumberBase.Hexadecimal, 6), alias.Name, alias.Comment);
        }

        public void RemoveRow(int address)
        {
            if (Locked) 
                return;

            for (var index = 0; index < labelView.Rows.Count; index++)
            {
                if ((string) labelView.Rows[index].Cells[0].Value !=
                    Util.NumberToBaseString(address, Util.NumberBase.Hexadecimal, 6)) continue;

                labelView.Rows.RemoveAt(index);
                labelView.Invalidate();
                break;
            }
        }

        public void ClearAndInvalidateDataGrid()
        {
            labelView.Rows.Clear();
            labelView.Invalidate();
        }

        private void importAppendLabelsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Info: Items in CSV will:\n" +
                            "1) CSV items will be added if their address doesn't already exist in this list\n" +
                            "2) CSV items will replace anything with the same address as items in the list\n" +
                            "3) any unmatched addresses in the list will be left alone\n" +
                            "\n" +
                            "Continue?\n", "Warning", MessageBoxButtons.OKCancel) != DialogResult.OK)
                return;

            ImportLabelsFromCsv(false);
        }

        private void importReplaceLabelsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Info: All list items will be deleted and replaced with the CSV file.\n" +
                                "\n" +
                                "Continue?\n", "Warning", MessageBoxButtons.OKCancel) != DialogResult.OK)
                return;

            ImportLabelsFromCsv(true);
        }

        public void LabelsRebindProject()
        {
            RepopulateFromData();

            labelView.CellValidating += labelView_CellValidating;
            labelView.CellBeginEdit += labelView_CellBeginEdit;
            labelView.UserDeletingRow += labelView_UserDeletingRow;
            labelView.Sort(labelView.Columns[0], ListSortDirection.Ascending);
            //labelView.UserDeletingRow += labelView_UserDeletingRow;

            // todo: eventually use databinding/datasource, probably.
            // Todo: modify observabledictionary wrapper to avoid having to do the .Dict call here.
            Project.Data.Labels.PropertyChanged += Labels_PropertyChanged;
            Project.Data.Labels.CollectionChanged += Labels_CollectionChanged;
        }

        private void RepopulateFromData()
        {
            ClearAndInvalidateDataGrid();

            if (Project.Data == null)
                return;

            // TODO: replace with winforms databinding eventually
            foreach (var item in Project.Data.Labels)
            {
                RawAdd(item.Key, item.Value);
            }
            labelView.Invalidate();
        }

        private void Labels_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (KeyValuePair<int, Label> item in e.NewItems)
                {
                    AddRow(item.Key, item.Value);
                }
            }

            if (e.OldItems != null)
            {
                foreach (KeyValuePair<int, Label> item in e.OldItems)
                {
                    RemoveRow(item.Key);
                }
            }
        }

        private void Labels_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // if needed, catch any changes to label content here
        }
    }
}
