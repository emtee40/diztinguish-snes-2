using System;
using System.Globalization;
using System.Windows.Forms;
using Diz.Core.model;
using Diz.Core.util;

namespace DiztinGUIsh.window.dialog
{
    public partial class MarkManyDialog : Form
    {
        public int Start { get; private set; }
        public int End { get; private set; }
        public int Count { get; private set; }
        public bool UnreachedOnly { get; private set; }

        private int value;

        private readonly Data data;

        public MarkManyDialog(int offset, string column, Data data)
        {
            InitializeComponent();
            this.data = data;

            property.SelectedIndex = column switch
            {
                "db" => 1,
                "dp" => 2,
                "m" => 3,
                "x" => 4,
                "base" => 5,
                _ => 0
            };
            
            Start = offset;
            var rest = this.data.GetRomSize() - Start;
            Count = rest < 0x10 ? rest : 0x10;
            End = Start + Count;
            UnreachedOnly = false;

            flagCombo.SelectedIndex = 3;
            archCombo.SelectedIndex = 0;
            mxCombo.SelectedIndex = 0;

            UpdateGroup();
            UpdateText(null);
        }
        
        public int Property => property.SelectedIndex;

        public object Value {
            get
            {
                switch (property.SelectedIndex)
                {
                    case 0:
                        return flagCombo.SelectedIndex switch
                        {
                            1 => Data.FlagType.Opcode,
                            2 => Data.FlagType.Operand,
                            3 => Data.FlagType.Data8Bit,
                            4 => Data.FlagType.Graphics,
                            5 => Data.FlagType.Music,
                            6 => Data.FlagType.Empty,
                            7 => Data.FlagType.Data16Bit,
                            8 => Data.FlagType.Pointer16Bit,
                            9 => Data.FlagType.Data24Bit,
                            10 => Data.FlagType.Pointer24Bit,
                            11 => Data.FlagType.Data32Bit,
                            12 => Data.FlagType.Pointer32Bit,
                            13 => Data.FlagType.Text,
                            14 => Data.FlagType.Binary,
                            _ => Data.FlagType.Unreached
                        };
                    case 3:
                    case 4:
                        return mxCombo.SelectedIndex != 0;
                    case 6:
                        return archCombo.SelectedIndex switch
                        {
                            1 => Data.Architecture.Apuspc700,
                            2 => Data.Architecture.GpuSuperFx,
                            _ => Data.Architecture.Cpu65C816
                        };
                    default:
                        return value;
                }
            }
        }

        private void UpdateGroup()
        {
            flagCombo.Visible = (property.SelectedIndex == 0);
            regValue.Visible = (property.SelectedIndex == 1 || property.SelectedIndex == 2 || property.SelectedIndex == 5);
            mxCombo.Visible = (property.SelectedIndex == 3 || property.SelectedIndex == 4);
            archCombo.Visible = (property.SelectedIndex == 6);
            regValue.MaxLength = (property.SelectedIndex == 1 ? 3 : property.SelectedIndex == 5 ? 7 : 5);
            value = property.SelectedIndex == 1 ? data.GetDataBank(Start) : property.SelectedIndex == 5 ? data.GetBaseAddr(Start) : data.GetDirectPage(Start);
        }

        private bool updatingText;

        private void UpdateText(TextBox selected)
        {
            Util.NumberBase noBase = radioDec.Checked ? Util.NumberBase.Decimal : Util.NumberBase.Hexadecimal;
            int digits = noBase == Util.NumberBase.Hexadecimal && radioROM.Checked ? 6 : 0;
            int size = data.GetRomSize();
            int maxValue = property.SelectedIndex == 1 ? 0x100 : property.SelectedIndex == 5 ? 0x1000000 : 0x10000;

            if (Start < 0) Start = 0;
            if (End >= size) End = size - 1;
            Count = End - Start;
            if (Count < 0) Count = 0;
            if (value < 0) value = 0;
            if (value >= maxValue) value = maxValue - 1;

            updatingText = true;
            if (selected != textStart) textStart.Text = Util.NumberToBaseString(radioROM.Checked ? data.ConvertPCtoSnes(Start) : Start, noBase, digits);
            if (selected != textEnd) textEnd.Text = Util.NumberToBaseString(radioROM.Checked ? data.ConvertPCtoSnes(End) : End, noBase, digits);
            if (selected != textCount) textCount.Text = Util.NumberToBaseString(Count, noBase, 0);
            if (selected != regValue) regValue.Text = Util.NumberToBaseString(value, noBase, 0);
            updatingText = false;
        }
        
        private void property_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateGroup();
        }
        
        private void regValue_TextChanged(object sender, EventArgs e)
        {
            var style = radioDec.Checked ? NumberStyles.Number : NumberStyles.HexNumber;

            if (int.TryParse(regValue.Text, style, null, out var result))
            {
                value = result;
            }
        }

        private void okay_Click(object sender, EventArgs e)
        {
            this.UnreachedOnly = unreachedOnly.Checked;
            this.DialogResult = DialogResult.OK;
        }

        private void cancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void textCount_TextChanged(object sender, EventArgs e)
        {
            OnTextChanged(textCount, value =>
            {
                Count = value;
                End = Start + Count;
            });
        }

        private void textEnd_TextChanged(object sender, EventArgs e)
        {
            OnTextChanged(textEnd, value =>
            {
                if (radioROM.Checked)
                    value = data.ConvertSnesToPc(value);

                End = value;
                Count = End - Start;
            });
        }

        private void textStart_TextChanged(object sender, EventArgs e)
        {
            OnTextChanged(textStart, value =>
            {
                if (radioROM.Checked)
                    value = data.ConvertSnesToPc(value);

                Start = value;
                Count = End - Start;
            });
        }
        
        private void OnTextChanged(TextBox textBox, Action<int> OnResult)
        {        
            if (updatingText)
                return;

            updatingText = true;
            var style = radioDec.Checked ? NumberStyles.Number : NumberStyles.HexNumber;

            if (int.TryParse(textBox.Text, style, null, out var result))
                OnResult(result);

            UpdateText(textBox);
        }

        private void radioHex_CheckedChanged(object sender, EventArgs e)
        {
            UpdateText(null);
        }

        private void radioROM_CheckedChanged(object sender, EventArgs e)
        {
            UpdateText(null);
        }
    }
}
