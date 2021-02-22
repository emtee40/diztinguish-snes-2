using Diz.Core.model;
using Diz.Core.util;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;

namespace Diz.Core.arch
{
    public class Cpu65C816
    {
        private Stack<int> stack;
        private readonly Data data;
        public Cpu65C816(Data data)
        {
            this.data = data;
            this.stack = new Stack<int>();
        }
        public int Step(int offset, bool branch, bool force, int prevOffset)
        {
            var opcode = data.GetRomByte(offset);
            var prevDirectPage = data.GetDirectPage(offset);
            var prevDataBank = data.GetDataBank(offset);
            var prevBaseAddr = data.GetBaseAddr(offset);
            bool prevX = data.GetXFlag(offset), prevM = data.GetMFlag(offset);

            while (prevOffset >= 0 && data.GetFlag(prevOffset) == Data.FlagType.Operand) prevOffset--;
            if (prevOffset >= 0 && data.GetFlag(prevOffset) == Data.FlagType.Opcode)
            {
                prevDirectPage = data.GetDirectPage(prevOffset);
                prevDataBank = data.GetDataBank(prevOffset);
                prevBaseAddr = data.GetBaseAddr(prevOffset);
                prevX = data.GetXFlag(prevOffset);
                prevM = data.GetMFlag(prevOffset);
            }

            if ((opcode == 0x6B || opcode == 0x60) && stack.Count > 0) // RTS RTL
            {
                int lastOffset = stack.Pop();
                return Step(lastOffset, false, false, lastOffset - 1);
            }

            if (opcode == 0xC2 || opcode == 0xE2) // REP SEP
            {
                prevX = (data.GetRomByte(offset + 1) & 0x10) != 0 ? opcode == 0xE2 : prevX;
                prevM = (data.GetRomByte(offset + 1) & 0x20) != 0 ? opcode == 0xE2 : prevM;
            }

            if (opcode == 0x5B) // TCD
            {
                prevDirectPage = data.GetAddressMode(prevOffset) == AddressMode.Immediate16 ? data.GetRomWord(prevOffset + 1) : prevDirectPage;
            }

            // set first byte first, so the instruction length is correct
            data.SetFlag(offset, Data.FlagType.Opcode);
            data.SetDataBank(offset, prevDataBank);
            data.SetDirectPage(offset, prevDirectPage);
            data.SetXFlag(offset, prevX);
            data.SetMFlag(offset, prevM);
            data.SetBaseAddr(offset, prevBaseAddr);

            var length = GetInstructionLength(offset);

            // TODO: I don't think this is handling execution bank boundary wrapping correctly? -Dom
            // If we run over the edge of a bank, we need to go back to the beginning of that bank, not go into
            // the next one.  While this should be really rare, it's technically valid.
            //
            // docs: http://www.6502.org/tutorials/65c816opcodes.html#5.1.2
            // [Note that although the 65C816 has a 24-bit address space, the Program Counter is only a 16-bit register and
            // the Program Bank Register is a separate (8-bit) register. This means that instruction execution wraps at bank
            // boundaries. This is true even if the bank boundary occurs in the middle of the instruction.]
            //
            // TODO: check other areas, anywhere we're accessing a Rom address plus some offset, might need to wrap
            // in most situations.
            for (var i = 1; i < length; i++)
            {
                data.SetFlag(offset + i, Data.FlagType.Operand);
                data.SetDataBank(offset + i, prevDataBank);
                data.SetDirectPage(offset + i, prevDirectPage);
                data.SetBaseAddr(offset + i, prevBaseAddr);
                data.SetXFlag(offset + i, prevX);
                data.SetMFlag(offset + i, prevM);
            }

            MarkInOutPoints(offset);

            var nextOffset = offset + length;

            if (force || (opcode != 0x4C && opcode != 0x5C && opcode != 0x80 && opcode != 0x82 && (!branch ||
                (opcode != 0x10 && opcode != 0x30 && opcode != 0x50 && opcode != 0x70 && opcode != 0x90 &&
                 opcode != 0xB0 && opcode != 0xD0 && opcode != 0xF0 && opcode != 0x20 &&
                 opcode != 0x22 && opcode != 0xFC)))) 
                return nextOffset;

            var iaNextOffsetPc = data.ConvertSnesToPc(GetIntermediateAddress(offset, true));
            if (force || (branch && iaNextOffsetPc >= 0 && iaNextOffsetPc < data.GetRomSize()))
            {
                if (opcode == 0x20 || opcode == 0x22 || opcode == 0xFC)
                    stack.Push(offset);
                if (opcode == 0xFC)
                {
                    data.MarkTypeFlag(iaNextOffsetPc, Data.FlagType.Pointer16Bit, RomUtil.GetByteLengthForFlag(Data.FlagType.Pointer16Bit));
                    iaNextOffsetPc = data.ConvertSnesToPc(data.GetIntermediateAddressOrPointer(iaNextOffsetPc));
                }

                nextOffset = iaNextOffsetPc;
            }

            return nextOffset;
        }

        // input: ROM offset
        // return: a SNES address   
        public int GetIntermediateAddress(int offset, bool resolve, bool original = false)
        {
            int bank, dp, operand = 0, pc = -1, addr = -1;
            var opcode = data.GetRomByte(offset);
            int iaddr = data.GetIndirectAddr(offset);
            int baddr = data.GetBaseAddr(offset);

            var mode = GetAddressMode(offset);
            switch (mode)
            {
                case AddressMode.DirectPage:
                case AddressMode.DirectPageXIndex:
                case AddressMode.DirectPageYIndex:
                case AddressMode.DirectPageIndirect:
                case AddressMode.DirectPageXIndexIndirect:
                case AddressMode.DirectPageIndirectYIndex:
                case AddressMode.DirectPageLongIndirect:
                case AddressMode.DirectPageLongIndirectYIndex:
                    if (resolve)
                    {
                        dp = data.GetDirectPage(offset);
                        operand = data.GetRomByte(offset + 1);
                        addr = (dp + operand) & 0xFFFF;
                        break;
                    }
                    else
                    {
                        goto case AddressMode.DirectPageSIndex;
                    }
                case AddressMode.DirectPageSIndex:
                case AddressMode.DirectPageSIndexIndirectYIndex:
                    addr = data.GetRomByte(offset + 1);
                    break;
                case AddressMode.Address:
                case AddressMode.AddressXIndex:
                case AddressMode.AddressYIndex:
                case AddressMode.AddressXIndexIndirect:
                    bank = (opcode == 0x20 || opcode == 0x4C || opcode == 0x7C || opcode == 0xFC) ?
                        data.ConvertPCtoSnes(offset) >> 16 :
                        data.GetDataBank(offset);
                    operand = data.GetRomWord(offset + 1);
                    addr = (bank << 16) | operand;
                    break;
                case AddressMode.AddressIndirect:
                case AddressMode.AddressLongIndirect:
                    addr = data.GetRomWord(offset + 1);
                    break;
                case AddressMode.Long:
                case AddressMode.LongXIndex:
                    addr = data.GetRomLong(offset + 1);
                    break;
                case AddressMode.Relative8:
                    pc = data.ConvertPCtoSnes(offset + 2);
                    bank = pc >> 16;
                    addr = (sbyte)data.GetRomByte(offset + 1);
                    addr = (bank << 16) | ((pc + addr) & 0xFFFF);
                    break;
                case AddressMode.Relative16:
                    pc = data.ConvertPCtoSnes(offset + 3);
                    bank = pc >> 16;
                    addr = (short)data.GetRomWord(offset + 1);
                    addr = (bank << 16) | ((pc + addr) & 0xFFFF);
                    break;
                case AddressMode.Immediate16:
                case AddressMode.Immediate8:
                case AddressMode.Constant8:
                    if(iaddr > 0)
                        addr = mode == AddressMode.Immediate16 ? (short)data.GetRomWord(offset + 1) : (sbyte)data.GetRomByte(offset + 1);
                    break;
            }

            if (original) return addr;

            if (iaddr > 0)
            {
                pc = data.ConvertSnesToPc(iaddr);
                int pc_end = -1;
                if (data.GetFlag(offset) != Data.FlagType.Operand)
                if (baddr == 0 && pc >= 0 && pc < data.GetRomSize() && (baddr = data.GetBaseAddr(pc)) > 0 && addr >= baddr && (pc_end = pc + (addr - baddr)) < data.GetRomSize() && baddr == data.GetBaseAddr(pc_end)){
                    return data.ConvertPCtoSnes(pc_end);
                }
                return iaddr;
            }

            int last_baddr = offset > 0 ? data.GetBaseAddr(offset - 1) : -1, next_baddr = offset + 1 < data.GetRomSize() ? data.GetBaseAddr(offset + 1) : -1;
            if(baddr > 0 && (last_baddr == baddr || next_baddr == baddr))
            {
                pc = data.CalculateBaseAddr(offset) - baddr;
                pc = (offset - pc) + (addr - baddr);
                if (pc >= 0 && pc < data.GetRomSize() && data.GetBaseAddr(pc) == baddr)
                    return data.ConvertPCtoSnes(pc);
            }

            return addr;
        }

        public string GetInstruction(int offset, bool lowercase)
        {
            AddressMode mode = GetAddressMode(offset);
            string format = GetInstructionFormatString(offset);
            string mnemonic = GetMnemonic(offset);
            Data.FlagType flag = data.GetFlag(offset);
            if (lowercase) mnemonic = mnemonic.ToLower();
            string op1 = "", op2 = "";

            if (mode == AddressMode.Immediate16 || mode == AddressMode.Constant8 || mode == AddressMode.Immediate8)
            {
                if (data.GetBaseAddr(offset) > 0 || data.GetIndirectAddr(offset) > 0)
                    op1 = FormatOperandAddress(offset, mode);
                else
                {
                    int operand = data.GetRomWord(offset + 1);//mode == AddressMode.Immediate16 ? data.GetRomWord(offset + 1) : data.GetRomByte(offset + 1);
                    int iaddr = data.GetIndirectAddr(offset + 1);
                    if (iaddr > 0)
                    {// && Regex.IsMatch(mnemonic, "ld[yx]", RegexOptions.IgnoreCase))
                        int pc = data.ConvertSnesToPc(iaddr), baddr = pc >= 0 ? data.GetBaseAddr(pc) : -1;
                        bool outside_baddr = data.GetBaseAddr(offset) == 0 && baddr > 0;

                        string label1 = data.GetLabelName(iaddr, true), label2 = "";
                        if (outside_baddr) label1 += "_start";

                        op1 = label1;

                        if (data.GetIndirectAddr(offset + 2) > 0)
                            label2 = data.GetLabelName(data.GetIndirectAddr(offset + 2), true);
                        else if (operand < data.GetBankSize() && (iaddr + operand) / data.GetBankSize() == iaddr / data.GetBankSize())
                        {
                            if (outside_baddr && data.GetBaseAddr(pc + operand) == baddr) label2 = data.GetLabelName(iaddr, true) + "_end";
                            else label2 = data.GetLabelName(iaddr + operand, true);
                        }
                        else
                            operand = -1;

                        if (operand >= 0)
                            op1 = label2 + "-1-" + label1;
                    }
                }
            }

            if (op1 == "")
            if (mode == AddressMode.BlockMove)
            {
                if (data.GetIndirectAddr(offset + 1) > 0)
                    op1 = data.GetLabelName(data.GetIndirectAddr(offset + 1));
                if (data.GetIndirectAddr(offset + 2) > 0)
                    op2 = data.GetLabelName(data.GetIndirectAddr(offset + 2));

                op1 = op1 == "" ? Util.NumberToBaseString(data.GetRomByte(offset + 1), Util.NumberBase.Hexadecimal, 2, true) : $"<:{op1}";
                op2 = op2 == "" ? Util.NumberToBaseString(data.GetRomByte(offset + 2), Util.NumberBase.Hexadecimal, 2, true) : $"<:{op2}";
            }
            else if (mode == AddressMode.Constant8 || mode == AddressMode.Immediate8)
            {
                int operand = data.GetRomByte(offset + 1);
                op1 = data.GetConstantType(offset) switch
                {
                    Data.ConstantType.Text => $"'{(char)operand}'",
                    Data.ConstantType.Decimal => Util.NumberToBaseString(operand, Util.NumberBase.Decimal, 1, true),
                    Data.ConstantType.Binary => Util.NumberToBaseString(operand, Util.NumberBase.Binary, 8, true),
                    _ => Util.NumberToBaseString(operand, Util.NumberBase.Hexadecimal, 2, true)
                };

            }
            else if (mode == AddressMode.Immediate16)
            {
                int operand = data.GetRomWord(offset + 1); Color color = Util.ColorRGB555(operand);
                op1 = data.GetConstantType(offset) switch
                {
                    Data.ConstantType.Color => $"rgb555({color.R},{color.G},{color.B})",
                    Data.ConstantType.Text => $"'{(char)data.GetRomByte(offset + 1)}{(char)data.GetRomByte(offset + 2)}'",
                    Data.ConstantType.Decimal => Util.NumberToBaseString(operand, Util.NumberBase.Decimal, 1, true),
                    Data.ConstantType.Binary => Util.NumberToBaseString(operand, Util.NumberBase.Binary, 16, true),
                    _ => Util.NumberToBaseString(operand, Util.NumberBase.Hexadecimal, 4, true)
                };
            }
            else
            {
                // dom note: this is where we could inject expressions if needed. it gives stuff like "$F001".
                // we could substitute our expression of "$#F000 + $#01" or "some_struct.member" like "player.hp"
                // the expression must be verified to always match the bytes in the file [unless we allow overriding]
                op1 = FormatOperandAddress(offset, mode);
            }


            if (offset == 0 || data.GetFlag(offset - 1) != flag || data.GetLabelName(data.ConvertPCtoSnes(offset)) != "" ||
                (flag != Data.FlagType.Opcode && (data.GetInOutPoint(offset) & Data.InOutPoint.ReadPoint) == Data.InOutPoint.ReadPoint) ||
                flag == Data.FlagType.Graphics || flag == Data.FlagType.Music || flag == Data.FlagType.Binary || flag == Data.FlagType.Empty
            )
            {
                switch (flag)
                {
                    case Data.FlagType.Data8Bit:
                        return data.GetFormattedBytes(offset, 1, 1);
                    case Data.FlagType.Data16Bit:
                        return data.GetFormattedBytes(offset, 2, 1);
                    case Data.FlagType.Data24Bit:
                        return data.GetFormattedBytes(offset, 3, 1);
                    case Data.FlagType.Data32Bit:
                        return data.GetFormattedBytes(offset, 4, 1);
                    case Data.FlagType.Pointer16Bit:
                        return data.GetPointer(offset, 2);
                    case Data.FlagType.Pointer24Bit:
                        return data.GetPointer(offset, 3);
                    case Data.FlagType.Pointer32Bit:
                        return data.GetPointer(offset, 4);
                    case Data.FlagType.Text:
                        return data.GetFormattedText(offset, 0);
                    case Data.FlagType.Graphics:
                    case Data.FlagType.Music:
                    case Data.FlagType.Binary:
                    case Data.FlagType.Empty:
                        return data.GetFormattedBytes(offset, 1, 1);
                }
            }
            return string.Format(format, mnemonic, op1, op2);
        }

        public int GetInstructionLength(int offset)
        {
            var mode = GetAddressMode(offset);
            return InstructionLength(mode);
        }

        public static int InstructionLength(AddressMode mode)
        {
            switch (mode)
            {
                case AddressMode.Implied:
                case AddressMode.Accumulator:
                    return 1;
                case AddressMode.Constant8:
                case AddressMode.Immediate8:
                case AddressMode.DirectPage:
                case AddressMode.DirectPageXIndex:
                case AddressMode.DirectPageYIndex:
                case AddressMode.DirectPageSIndex:
                case AddressMode.DirectPageIndirect:
                case AddressMode.DirectPageXIndexIndirect:
                case AddressMode.DirectPageIndirectYIndex:
                case AddressMode.DirectPageSIndexIndirectYIndex:
                case AddressMode.DirectPageLongIndirect:
                case AddressMode.DirectPageLongIndirectYIndex:
                case AddressMode.Relative8:
                    return 2;
                case AddressMode.Immediate16:
                case AddressMode.Address:
                case AddressMode.AddressXIndex:
                case AddressMode.AddressYIndex:
                case AddressMode.AddressIndirect:
                case AddressMode.AddressXIndexIndirect:
                case AddressMode.AddressLongIndirect:
                case AddressMode.BlockMove:
                case AddressMode.Relative16:
                    return 3;
                case AddressMode.Long:
                case AddressMode.LongXIndex:
                    return 4;
            }

            return 1;
        }

        public void MarkInOutPoints(int offset)
        {
            Data.FlagType flag = data.GetFlag(offset);
            int opcode = flag == Data.FlagType.Opcode ? data.GetRomByte(offset) : 0x00;
            int iaOffsetPc = data.ConvertSnesToPc(data.GetIntermediateAddressOrPointer(offset));

            if (flag == Data.FlagType.Pointer16Bit)
            {
                data.SetInOutPoint(offset, iaOffsetPc < 0 ? Data.InOutPoint.EndPoint : Data.InOutPoint.ReadPoint);
                if(iaOffsetPc >= 0)
                    data.SetInOutPoint(iaOffsetPc, data.GetFlag(iaOffsetPc) == Data.FlagType.Opcode ? Data.InOutPoint.InPoint : Data.InOutPoint.ReadPoint);
                return;
            }

            // set read point on EA
            if (iaOffsetPc >= 0 && ( // these are all read/write/math instructions
                ((opcode & 0x04) != 0) || ((opcode & 0x0F) == 0x01) || ((opcode & 0x0F) == 0x03) ||
                ((opcode & 0x1F) == 0x12) || ((opcode & 0x1F) == 0x19) || opcode == 0xA9 || opcode == 0xA2 || opcode == 0xA0) &&
                (opcode != 0x45) && (opcode != 0x55) && (opcode != 0xF5) && (opcode != 0x4C) &&
                (opcode != 0x5C) && (opcode != 0x6C) && (opcode != 0x7C) && (opcode != 0xDC) && (opcode != 0xFC)
            ) data.SetInOutPoint(iaOffsetPc, Data.InOutPoint.ReadPoint);

            // set end point on offset
            if (opcode == 0x40 || opcode == 0x4C || opcode == 0x5C || opcode == 0x60 // RTI JMP JML RTS
                || opcode == 0x6B || opcode == 0x6C || opcode == 0x7C || opcode == 0x80 // RTL JMP JMP BRA
                || opcode == 0x82 || opcode == 0xDB || opcode == 0xDC // BRL STP JML
            ) data.SetInOutPoint(offset, Data.InOutPoint.EndPoint);

            // set out point on offset
            // set in point on EA
            if (iaOffsetPc >= 0 && (
                opcode == 0x4C || opcode == 0x5C || opcode == 0x80 || opcode == 0x82 // JMP JML BRA BRL
                || opcode == 0x10 || opcode == 0x30 || opcode == 0x50 || opcode == 0x70  // BPL BMI BVC BVS
                || opcode == 0x90 || opcode == 0xB0 || opcode == 0xD0 || opcode == 0xF0  // BCC BCS BNE BEQ
                || opcode == 0x20 || opcode == 0x22)) // JSR JSL
            {
                data.SetInOutPoint(offset, Data.InOutPoint.OutPoint);
                data.SetInOutPoint(iaOffsetPc, Data.InOutPoint.InPoint);
            }
        }

        private string FormatOperandAddress(int offset, AddressMode mode)
        {
            int address = data.GetIntermediateAddress(offset, true);
            int operand = data.GetRomLong(offset + 1), count = BytesToShow(mode); operand &= ~(-1 << (8 * count));

            if (address < 0) 
                return "";

            //int ram_address = (address & 0x00ffff);
            //address = data.GetIndirectAddr(offset) == 0 && ram_address < 0x8000 && mode != AddressMode.Long && mode != AddressMode.LongXIndex ? ram_address : address;

            var label = data.GetParentLabel(offset, address);
            int ram_address = (address & 0xffff);
            if (label == "" && ram_address < 0x8000 && mode != AddressMode.Long && mode != AddressMode.LongXIndex)
            {
                address = ram_address;
                label = data.GetLabelName(address);
            }


            int pcaddress = data.ConvertSnesToPc(address);
            if ((mode == AddressMode.AddressXIndex || mode == AddressMode.AddressYIndex) && pcaddress >= 0 && pcaddress < data.GetRomSize() && data.GetBaseAddr(pcaddress) > 0)
                return data.GetLabelName(address, true) + "_start";

            if (label != "")
            {
                count = data.GetIntermediateAddress(offset, true, true) - address;
                if (data.GetIndirectAddr(offset) > 0 && count > 0)// && count < 0xFF)// && data.GetLabelName(operand) == "")
                {
                    label += "+";
                    label += data.GetConstantType(offset) switch
                    {
                        Data.ConstantType.Decimal => Util.NumberToBaseString(count, Util.NumberBase.Decimal, 1, false),
                        _ => Util.NumberToBaseString(count, Util.NumberBase.Hexadecimal, 2, true)
                    };
                }
                return (mode == AddressMode.Constant8 || mode == AddressMode.Immediate8 ? "<:" : "") + label;
            }


            return Util.NumberToBaseString(operand, Util.NumberBase.Hexadecimal, 2 * count, true);
        }

        public string GetMnemonic(int offset, bool showHint = true)
        {
            var mn = Mnemonics[data.GetRomByte(offset)];
            if (!showHint) 
                return mn;

            var mode = GetAddressMode(offset);
            var count = BytesToShow(mode);

            if (mode == AddressMode.Constant8 || mode == AddressMode.Relative16 || mode == AddressMode.Relative8) return mn;

            return count switch
            {
                1 => mn += ".B",
                2 => mn += ".W",
                3 => mn += ".L",
                _ => mn
            };
        }

        private static int BytesToShow(AddressMode mode)
        {
            switch (mode)
            {
                case AddressMode.Constant8:
                case AddressMode.Immediate8:
                case AddressMode.DirectPage:
                case AddressMode.DirectPageXIndex:
                case AddressMode.DirectPageYIndex:
                case AddressMode.DirectPageSIndex:
                case AddressMode.DirectPageIndirect:
                case AddressMode.DirectPageXIndexIndirect:
                case AddressMode.DirectPageIndirectYIndex:
                case AddressMode.DirectPageSIndexIndirectYIndex:
                case AddressMode.DirectPageLongIndirect:
                case AddressMode.DirectPageLongIndirectYIndex:
                case AddressMode.Relative8:
                    return 1;
                case AddressMode.Immediate16:
                case AddressMode.Address:
                case AddressMode.AddressXIndex:
                case AddressMode.AddressYIndex:
                case AddressMode.AddressIndirect:
                case AddressMode.AddressXIndexIndirect:
                case AddressMode.AddressLongIndirect:
                case AddressMode.Relative16:
                    return 2;
                case AddressMode.Long:
                case AddressMode.LongXIndex:
                    return 3;
            }
            return 0;
        }

        // {0} = mnemonic
        // {1} = intermediate address / label OR operand 1 for block move
        // {2} = operand 2 for block move
        private string GetInstructionFormatString(int offset)
        {
            var mode = GetAddressMode(offset);
            switch (mode)
            {
                case AddressMode.Implied:
                    return "{0}";
                case AddressMode.Accumulator:
                    return "{0} A";
                case AddressMode.Constant8:
                case AddressMode.Immediate8:
                case AddressMode.Immediate16:
                    return "{0} #{1}";
                case AddressMode.DirectPage:
                case AddressMode.Address:
                case AddressMode.Long:
                case AddressMode.Relative8:
                case AddressMode.Relative16:
                    return "{0} {1}";
                case AddressMode.DirectPageXIndex:
                case AddressMode.AddressXIndex:
                case AddressMode.LongXIndex:
                    return "{0} {1},X";
                case AddressMode.DirectPageYIndex:
                case AddressMode.AddressYIndex:
                    return "{0} {1},Y";
                case AddressMode.DirectPageSIndex:
                    return "{0} {1},S";
                case AddressMode.DirectPageIndirect:
                case AddressMode.AddressIndirect:
                    return "{0} ({1})";
                case AddressMode.DirectPageXIndexIndirect:
                case AddressMode.AddressXIndexIndirect:
                    return "{0} ({1},X)";
                case AddressMode.DirectPageIndirectYIndex:
                    return "{0} ({1}),Y";
                case AddressMode.DirectPageSIndexIndirectYIndex:
                    return "{0} ({1},S),Y";
                case AddressMode.DirectPageLongIndirect:
                case AddressMode.AddressLongIndirect:
                    return "{0} [{1}]";
                case AddressMode.DirectPageLongIndirectYIndex:
                    return "{0} [{1}],Y";
                case AddressMode.BlockMove:
                    return "{0} {1},{2}";
            }
            return "";
        }

        public string GetRegisterLabel(int snes)
        {
            if (Registers.TryGetValue(snes, out var val))
                return val?.Name ?? "";
            return "";
        }
        public string GetRegisterComment(int snes)
        {
            if (Registers.TryGetValue(snes, out var val))
                return val?.Comment ?? "";
            return "";
        }

        public AddressMode GetAddressMode(int offset)
        {
            var mode = AddressingModes[data.GetRomByte(offset)];
            return mode switch
            {
                AddressMode.ImmediateMFlagDependent => data.GetMFlag(offset)
                    ? AddressMode.Immediate8
                    : AddressMode.Immediate16,
                AddressMode.ImmediateXFlagDependent => data.GetXFlag(offset)
                    ? AddressMode.Immediate8
                    : AddressMode.Immediate16,
                _ => mode
            };
        }

        public enum AddressMode : byte
        {
            Implied, Accumulator, Constant8, Immediate8, Immediate16,
            ImmediateXFlagDependent, ImmediateMFlagDependent,
            DirectPage, DirectPageXIndex, DirectPageYIndex,
            DirectPageSIndex, DirectPageIndirect, DirectPageXIndexIndirect,
            DirectPageIndirectYIndex, DirectPageSIndexIndirectYIndex,
            DirectPageLongIndirect, DirectPageLongIndirectYIndex,
            Address, AddressXIndex, AddressYIndex, AddressIndirect,
            AddressXIndexIndirect, AddressLongIndirect,
            Long, LongXIndex, BlockMove, Relative8, Relative16
        }

        private static readonly string[] Mnemonics =
        {
            "BRK", "ORA", "COP", "ORA", "TSB", "ORA", "ASL", "ORA", "PHP", "ORA", "ASL", "PHD", "TSB", "ORA", "ASL", "ORA",
            "BPL", "ORA", "ORA", "ORA", "TRB", "ORA", "ASL", "ORA", "CLC", "ORA", "INC", "TCS", "TRB", "ORA", "ASL", "ORA",
            "JSR", "AND", "JSL", "AND", "BIT", "AND", "ROL", "AND", "PLP", "AND", "ROL", "PLD", "BIT", "AND", "ROL", "AND",
            "BMI", "AND", "AND", "AND", "BIT", "AND", "ROL", "AND", "SEC", "AND", "DEC", "TSC", "BIT", "AND", "ROL", "AND",
            "RTI", "EOR", "WDM", "EOR", "MVP", "EOR", "LSR", "EOR", "PHA", "EOR", "LSR", "PHK", "JMP", "EOR", "LSR", "EOR",
            "BVC", "EOR", "EOR", "EOR", "MVN", "EOR", "LSR", "EOR", "CLI", "EOR", "PHY", "TCD", "JML", "EOR", "LSR", "EOR",
            "RTS", "ADC", "PER", "ADC", "STZ", "ADC", "ROR", "ADC", "PLA", "ADC", "ROR", "RTL", "JMP", "ADC", "ROR", "ADC",
            "BVS", "ADC", "ADC", "ADC", "STZ", "ADC", "ROR", "ADC", "SEI", "ADC", "PLY", "TDC", "JMP", "ADC", "ROR", "ADC",
            "BRA", "STA", "BRL", "STA", "STY", "STA", "STX", "STA", "DEY", "BIT", "TXA", "PHB", "STY", "STA", "STX", "STA",
            "BCC", "STA", "STA", "STA", "STY", "STA", "STX", "STA", "TYA", "STA", "TXS", "TXY", "STZ", "STA", "STZ", "STA",
            "LDY", "LDA", "LDX", "LDA", "LDY", "LDA", "LDX", "LDA", "TAY", "LDA", "TAX", "PLB", "LDY", "LDA", "LDX", "LDA",
            "BCS", "LDA", "LDA", "LDA", "LDY", "LDA", "LDX", "LDA", "CLV", "LDA", "TSX", "TYX", "LDY", "LDA", "LDX", "LDA",
            "CPY", "CMP", "REP", "CMP", "CPY", "CMP", "DEC", "CMP", "INY", "CMP", "DEX", "WAI", "CPY", "CMP", "DEC", "CMP",
            "BNE", "CMP", "CMP", "CMP", "PEI", "CMP", "DEC", "CMP", "CLD", "CMP", "PHX", "STP", "JML", "CMP", "DEC", "CMP",
            "CPX", "SBC", "SEP", "SBC", "CPX", "SBC", "INC", "SBC", "INX", "SBC", "NOP", "XBA", "CPX", "SBC", "INC", "SBC",
            "BEQ", "SBC", "SBC", "SBC", "PEA", "SBC", "INC", "SBC", "SED", "SBC", "PLX", "XCE", "JSR", "SBC", "INC", "SBC"
        };

        private static readonly AddressMode[] AddressingModes =
        {
            AddressMode.Constant8, AddressMode.DirectPageXIndexIndirect, AddressMode.Constant8, AddressMode.DirectPageSIndex,
            AddressMode.DirectPage, AddressMode.DirectPage, AddressMode.DirectPage, AddressMode.DirectPageLongIndirect,
            AddressMode.Implied, AddressMode.ImmediateMFlagDependent, AddressMode.Accumulator, AddressMode.Implied,
            AddressMode.Address, AddressMode.Address, AddressMode.Address, AddressMode.Long,
            AddressMode.Relative8, AddressMode.DirectPageIndirectYIndex, AddressMode.DirectPageIndirect, AddressMode.DirectPageSIndexIndirectYIndex,
            AddressMode.DirectPage, AddressMode.DirectPageXIndex, AddressMode.DirectPageXIndex, AddressMode.DirectPageLongIndirectYIndex,
            AddressMode.Implied, AddressMode.AddressYIndex, AddressMode.Accumulator, AddressMode.Implied,
            AddressMode.Address, AddressMode.AddressXIndex, AddressMode.AddressXIndex, AddressMode.LongXIndex,

            AddressMode.Address, AddressMode.DirectPageXIndexIndirect, AddressMode.Long, AddressMode.DirectPageSIndex,
            AddressMode.DirectPage, AddressMode.DirectPage, AddressMode.DirectPage, AddressMode.DirectPageLongIndirect,
            AddressMode.Implied, AddressMode.ImmediateMFlagDependent, AddressMode.Accumulator, AddressMode.Implied,
            AddressMode.Address, AddressMode.Address, AddressMode.Address, AddressMode.Long,
            AddressMode.Relative8, AddressMode.DirectPageIndirectYIndex, AddressMode.DirectPageIndirect, AddressMode.DirectPageSIndexIndirectYIndex,
            AddressMode.DirectPageXIndex, AddressMode.DirectPageXIndex, AddressMode.DirectPageXIndex, AddressMode.DirectPageLongIndirectYIndex,
            AddressMode.Implied, AddressMode.AddressYIndex, AddressMode.Accumulator, AddressMode.Implied,
            AddressMode.AddressXIndex, AddressMode.AddressXIndex, AddressMode.AddressXIndex, AddressMode.LongXIndex,

            AddressMode.Implied, AddressMode.DirectPageXIndexIndirect, AddressMode.Constant8, AddressMode.DirectPageSIndex,
            AddressMode.BlockMove, AddressMode.DirectPage, AddressMode.DirectPage, AddressMode.DirectPageLongIndirect,
            AddressMode.Implied, AddressMode.ImmediateMFlagDependent, AddressMode.Accumulator, AddressMode.Implied,
            AddressMode.Address, AddressMode.Address, AddressMode.Address, AddressMode.Long,
            AddressMode.Relative8, AddressMode.DirectPageIndirectYIndex, AddressMode.DirectPageIndirect, AddressMode.DirectPageSIndexIndirectYIndex,
            AddressMode.BlockMove, AddressMode.DirectPageXIndex, AddressMode.DirectPageXIndex, AddressMode.DirectPageLongIndirectYIndex,
            AddressMode.Implied, AddressMode.AddressYIndex, AddressMode.Implied, AddressMode.Implied,
            AddressMode.Long, AddressMode.AddressXIndex, AddressMode.AddressXIndex, AddressMode.LongXIndex,

            AddressMode.Implied, AddressMode.DirectPageXIndexIndirect, AddressMode.Relative16, AddressMode.DirectPageSIndex,
            AddressMode.DirectPage, AddressMode.DirectPage, AddressMode.DirectPage, AddressMode.DirectPageLongIndirect,
            AddressMode.Implied, AddressMode.ImmediateMFlagDependent, AddressMode.Accumulator, AddressMode.Implied,
            AddressMode.AddressIndirect, AddressMode.Address, AddressMode.Address, AddressMode.Long,
            AddressMode.Relative8, AddressMode.DirectPageIndirectYIndex, AddressMode.DirectPageIndirect, AddressMode.DirectPageSIndexIndirectYIndex,
            AddressMode.DirectPageXIndex, AddressMode.DirectPageXIndex, AddressMode.DirectPageXIndex, AddressMode.DirectPageLongIndirectYIndex,
            AddressMode.Implied, AddressMode.AddressYIndex, AddressMode.Implied, AddressMode.Implied,
            AddressMode.AddressXIndexIndirect, AddressMode.AddressXIndex, AddressMode.AddressXIndex, AddressMode.LongXIndex,

            AddressMode.Relative8, AddressMode.DirectPageXIndexIndirect, AddressMode.Relative16, AddressMode.DirectPageSIndex,
            AddressMode.DirectPage, AddressMode.DirectPage, AddressMode.DirectPage, AddressMode.DirectPageLongIndirect,
            AddressMode.Implied, AddressMode.ImmediateMFlagDependent, AddressMode.Implied, AddressMode.Implied,
            AddressMode.Address, AddressMode.Address, AddressMode.Address, AddressMode.Long,
            AddressMode.Relative8, AddressMode.DirectPageIndirectYIndex, AddressMode.DirectPageIndirect, AddressMode.DirectPageSIndexIndirectYIndex,
            AddressMode.DirectPageXIndex, AddressMode.DirectPageXIndex, AddressMode.DirectPageYIndex, AddressMode.DirectPageLongIndirectYIndex,
            AddressMode.Implied, AddressMode.AddressYIndex, AddressMode.Implied, AddressMode.Implied,
            AddressMode.Address, AddressMode.AddressXIndex, AddressMode.AddressXIndex, AddressMode.LongXIndex,

            AddressMode.ImmediateXFlagDependent, AddressMode.DirectPageXIndexIndirect, AddressMode.ImmediateXFlagDependent, AddressMode.DirectPageSIndex,
            AddressMode.DirectPage, AddressMode.DirectPage, AddressMode.DirectPage, AddressMode.DirectPageLongIndirect,
            AddressMode.Implied, AddressMode.ImmediateMFlagDependent, AddressMode.Implied, AddressMode.Implied,
            AddressMode.Address, AddressMode.Address, AddressMode.Address, AddressMode.Long,
            AddressMode.Relative8, AddressMode.DirectPageIndirectYIndex, AddressMode.DirectPageIndirect, AddressMode.DirectPageSIndexIndirectYIndex,
            AddressMode.DirectPageXIndex, AddressMode.DirectPageXIndex, AddressMode.DirectPageYIndex, AddressMode.DirectPageLongIndirectYIndex,
            AddressMode.Implied, AddressMode.AddressYIndex, AddressMode.Implied, AddressMode.Implied,
            AddressMode.AddressXIndex, AddressMode.AddressXIndex, AddressMode.AddressYIndex, AddressMode.LongXIndex,

            AddressMode.ImmediateXFlagDependent, AddressMode.DirectPageXIndexIndirect, AddressMode.Constant8, AddressMode.DirectPageSIndex,
            AddressMode.DirectPage, AddressMode.DirectPage, AddressMode.DirectPage, AddressMode.DirectPageLongIndirect,
            AddressMode.Implied, AddressMode.ImmediateMFlagDependent, AddressMode.Implied, AddressMode.Implied,
            AddressMode.Address, AddressMode.Address, AddressMode.Address, AddressMode.Long,
            AddressMode.Relative8, AddressMode.DirectPageIndirectYIndex, AddressMode.DirectPageIndirect, AddressMode.DirectPageSIndexIndirectYIndex,
            AddressMode.DirectPageIndirect, AddressMode.DirectPageXIndex, AddressMode.DirectPageXIndex, AddressMode.DirectPageLongIndirectYIndex,
            AddressMode.Implied, AddressMode.AddressYIndex, AddressMode.Implied, AddressMode.Implied,
            AddressMode.AddressLongIndirect, AddressMode.AddressXIndex, AddressMode.AddressXIndex, AddressMode.LongXIndex,

            AddressMode.ImmediateXFlagDependent, AddressMode.DirectPageXIndexIndirect, AddressMode.Constant8, AddressMode.DirectPageSIndex,
            AddressMode.DirectPage, AddressMode.DirectPage, AddressMode.DirectPage, AddressMode.DirectPageLongIndirect,
            AddressMode.Implied, AddressMode.ImmediateMFlagDependent, AddressMode.Implied, AddressMode.Implied,
            AddressMode.Address, AddressMode.Address, AddressMode.Address, AddressMode.Long,
            AddressMode.Relative8, AddressMode.DirectPageIndirectYIndex, AddressMode.DirectPageIndirect, AddressMode.DirectPageSIndexIndirectYIndex,
            AddressMode.Address, AddressMode.DirectPageXIndex, AddressMode.DirectPageXIndex, AddressMode.DirectPageLongIndirectYIndex,
            AddressMode.Implied, AddressMode.AddressYIndex, AddressMode.Implied, AddressMode.Implied,
            AddressMode.AddressXIndexIndirect, AddressMode.AddressXIndex, AddressMode.AddressXIndex, AddressMode.LongXIndex,
        };

        public static readonly SortedDictionary<int, Label> Registers = new SortedDictionary<int, Label> {
            { 0x2100, new Label() { Name = "INIDISP", Comment = "Display Control 1" } },
            { 0x2101, new Label() { Name = "OBJSEL", Comment = "Object Size & Object Base" } },
            { 0x2102, new Label() { Name = "OAMADDL", Comment = "OAM Address; lower byte" } },
            { 0x2103, new Label() { Name = "OAMADDH", Comment = "OAM Address; bit 9 & Priority Rotation" } },
            { 0x2104, new Label() { Name = "OAMDATA", Comment = "OAM Data Write" } },
            { 0x2105, new Label() { Name = "BGMODE", Comment = "BG Mode & BG Character Size" } },
            { 0x2106, new Label() { Name = "MOSAIC", Comment = "Mosaic Size & Mosaic Enable" } },
            { 0x2107, new Label() { Name = "BG1SC", Comment = "BG1 Screen Base & Screen Size" } },
            { 0x2108, new Label() { Name = "BG2SC", Comment = "BG2 Screen Base & Screen Size" } },
            { 0x2109, new Label() { Name = "BG3SC", Comment = "BG3 Screen Base & Screen Size" } },
            { 0x210A, new Label() { Name = "BG4SC", Comment = "BG4 Screen Base & Screen Size" } },
            { 0x210B, new Label() { Name = "BG12NBA", Comment = "BG1/BG2 Character Data Area Designation" } },
            { 0x210C, new Label() { Name = "BG34NBA", Comment = "BG3/BG4 Character Data Area Designation" } },
            { 0x210D, new Label() { Name = "BG1HOFS", Comment = "BG1 Horizontal Scroll (X) / M7HOFS" } },
            { 0x210E, new Label() { Name = "BG1VOFS", Comment = "BG1 Vertical   Scroll (Y) / M7VOFS" } },
            { 0x210F, new Label() { Name = "BG2HOFS", Comment = "BG2 Horizontal Scroll (X)" } },
            { 0x2110, new Label() { Name = "BG2VOFS", Comment = "BG2 Vertical   Scroll (Y)" } },
            { 0x2111, new Label() { Name = "BG3HOFS", Comment = "BG3 Horizontal Scroll (X)" } },
            { 0x2112, new Label() { Name = "BG3VOFS", Comment = "BG3 Vertical   Scroll (Y)" } },
            { 0x2113, new Label() { Name = "BG4HOFS", Comment = "BG4 Horizontal Scroll (X)" } },
            { 0x2114, new Label() { Name = "BG4VOFS", Comment = "BG4 Vertical   Scroll (Y)" } },
            { 0x2115, new Label() { Name = "VMAINC", Comment = "VRAM Address Increment Mode" } },
            { 0x2116, new Label() { Name = "VMADDL", Comment = "VRAM Address; lower byte" } },
            { 0x2117, new Label() { Name = "VMADDH", Comment = "VRAM Address; higher byte" } },
            { 0x2118, new Label() { Name = "VMDATAL", Comment = "VRAM Data Write; lower byte" } },
            { 0x2119, new Label() { Name = "VMDATAH", Comment = "VRAM Data Write; higher byte" } },
            { 0x211A, new Label() { Name = "M7SEL", Comment = "Mode7 Rot/Scale Mode Settings" } },
            { 0x211B, new Label() { Name = "M7A", Comment = "Mode7 Rot/Scale A (COSINE A) & Maths 16-Bit Operand" } },
            { 0x211C, new Label() { Name = "M7B", Comment = "Mode7 Rot/Scale B (SINE A)   & Maths  8-bit Operand" } },
            { 0x211D, new Label() { Name = "M7C", Comment = "Mode7 Rot/Scale C (SINE B)" } },
            { 0x211E, new Label() { Name = "M7D", Comment = "Mode7 Rot/Scale D (COSINE B)" } },
            { 0x211F, new Label() { Name = "M7X", Comment = "Mode7 Rot/Scale Center Coordinate X" } },
            { 0x2120, new Label() { Name = "M7Y", Comment = "Mode7 Rot/Scale Center Coordinate Y" } },
            { 0x2121, new Label() { Name = "CGADD", Comment = "Palette CGRAM Address" } },
            { 0x2122, new Label() { Name = "CGDATA", Comment = "Palette CGRAM Data Write" } },
            { 0x2123, new Label() { Name = "W12SEL", Comment = "Window BG1/BG2  Mask Settings" } },
            { 0x2124, new Label() { Name = "W34SEL", Comment = "Window BG3/BG4  Mask Settings" } },
            { 0x2125, new Label() { Name = "WOBJSEL", Comment = "Window OBJ/MATH Mask Settings" } },
            { 0x2126, new Label() { Name = "WH0", Comment = "Window 1 Left  Position (X1)" } },
            { 0x2127, new Label() { Name = "WH1", Comment = "Window 1 Right Position (X2)" } },
            { 0x2128, new Label() { Name = "WH2", Comment = "Window 2 Left  Position (X1)" } },
            { 0x2129, new Label() { Name = "WH3", Comment = "Window 2 Right Position (X2)" } },
            { 0x212A, new Label() { Name = "WBGLOG", Comment = "Window 1/2 Mask Logic (BG1..BG4)" } },
            { 0x212B, new Label() { Name = "WOBJLOG", Comment = "Window 1/2 Mask Logic (OBJ/MATH)" } },
            { 0x212C, new Label() { Name = "TM", Comment = "Main Screen Designation" } },
            { 0x212D, new Label() { Name = "TS", Comment = "Sub  Screen Designation" } },
            { 0x212E, new Label() { Name = "TMW", Comment = "Window Area Main Screen Disable" } },
            { 0x212F, new Label() { Name = "TSW", Comment = "Window Area Sub  Screen Disable" } },
            { 0x2130, new Label() { Name = "CGSWSEL", Comment = "Color Math Control Register A" } },
            { 0x2131, new Label() { Name = "CGADSUB", Comment = "Color Math Control Register B" } },
            { 0x2132, new Label() { Name = "COLDATA", Comment = "Color Math Sub Screen Backdrop Color" } },
            { 0x2133, new Label() { Name = "SETINI", Comment = "Display Control 2" } },
            { 0x2134, new Label() { Name = "MPYL", Comment = "PPU1 Signed Multiply Result (Lower  8-Bit)" } },
            { 0x2135, new Label() { Name = "MPYM", Comment = "PPU1 Signed Multiply Result (Middle 8-Bit)" } },
            { 0x2136, new Label() { Name = "MPYH", Comment = "PPU1 Signed Multiply Result (Upper  8-Bit)" } },
            { 0x2137, new Label() { Name = "SLHV", Comment = "PPU1 Latch H/V-Counter By Software (Read=Strobe)" } },
            { 0x2138, new Label() { Name = "RDOAM", Comment = "PPU1 OAM  Data Read" } },
            { 0x2139, new Label() { Name = "RDVRAML", Comment = "PPU1 VRAM  Data Read; lower byte" } },
            { 0x213A, new Label() { Name = "RDVRAMH", Comment = "PPU1 VRAM  Data Read; higher byte" } },
            { 0x213B, new Label() { Name = "RDCGRAM", Comment = "PPU2 CGRAM Data Read" } },
            { 0x213C, new Label() { Name = "OPHCT", Comment = "PPU2 Horizontal Counter Latch" } },
            { 0x213D, new Label() { Name = "OPVCT", Comment = "PPU2 Vertical   Counter Latch" } },
            { 0x213E, new Label() { Name = "STAT77", Comment = "PPU1 Status & PPU1 Version Number" } },
            { 0x213F, new Label() { Name = "STAT78", Comment = "PPU2 Status & PPU2 Version Number" } },
            { 0x2140, new Label() { Name = "APUIO0", Comment = "Main CPU To Sound CPU Communication Port 0" } },
            { 0x2141, new Label() { Name = "APUIO1", Comment = "Main CPU To Sound CPU Communication Port 1" } },
            { 0x2142, new Label() { Name = "APUIO2", Comment = "Main CPU To Sound CPU Communication Port 2" } },
            { 0x2143, new Label() { Name = "APUIO3", Comment = "Main CPU To Sound CPU Communication Port 3" } },
            { 0x2180, new Label() { Name = "WMDATA", Comment = "WRAM Data Read/Write" } },
            { 0x2181, new Label() { Name = "WMADDL", Comment = "WRAM Address (Lower  8-Bit)" } },
            { 0x2182, new Label() { Name = "WMADDM", Comment = "WRAM Address (Middle 8-Bit)" } },
            { 0x2183, new Label() { Name = "WMADDH", Comment = "WRAM Address (Upper  1-Bit)" } },
            { 0x4016, new Label() { Name = "JOYA", Comment = "Joypad Input Register A; Joypad Output" } },
            { 0x4017, new Label() { Name = "JOYB", Comment = "Joypad Input Register B" } },
            { 0x4200, new Label() { Name = "NMITIMEN", Comment = "Interrupt Enable & Joypad Request" } },
            { 0x4201, new Label() { Name = "WRIO", Comment = "Programmable I/O Port (Open-Collector Output)" } },
            { 0x4202, new Label() { Name = "WRMPYA", Comment = "Set Unsigned  8-Bit Multiplicand" } },
            { 0x4203, new Label() { Name = "WRMPYB", Comment = "Set Unsigned  8-Bit Multiplier & Start Multiplication" } },
            { 0x4204, new Label() { Name = "WRDIVL", Comment = "Set Unsigned 16-Bit Dividend; lower byte" } },
            { 0x4205, new Label() { Name = "WRDIVH", Comment = "Set Unsigned 16-Bit Dividend; higher byte" } },
            { 0x4206, new Label() { Name = "WRDIVB", Comment = "Set Unsigned  8-Bit Divisor & Start Division" } },
            { 0x4207, new Label() { Name = "HTIMEL", Comment = "H-Count Timer Setting; lower byte" } },
            { 0x4208, new Label() { Name = "HTIMEH", Comment = "H-Count Timer Setting (Upper 1bit)" } },
            { 0x4209, new Label() { Name = "VTIMEL", Comment = "V-Count Timer Setting; lower byte" } },
            { 0x420A, new Label() { Name = "VTIMEH", Comment = "V-Count Timer Setting (Upper 1-Bit)" } },
            { 0x420B, new Label() { Name = "MDMAEN", Comment = "Select General Purpose DMA Channels & Start Transfer" } },
            { 0x420C, new Label() { Name = "HDMAEN", Comment = "Select H-Blank DMA (H-DMA) Channels" } },
            { 0x420D, new Label() { Name = "MEMSEL", Comment = "Memory-2 Waitstate Control" } },
            { 0x4210, new Label() { Name = "RDNMI", Comment = "V-Blank NMI Flag and CPU Version Number (Read/Ack)" } },
            { 0x4211, new Label() { Name = "TIMEUP", Comment = "H/V-Timer IRQ Flag (Read/Ack)" } },
            { 0x4212, new Label() { Name = "HVBJOY", Comment = "H/V-Blank Flag & Joypad Busy Flag" } },
            { 0x4213, new Label() { Name = "RDIO", Comment = "Joypad Programmable I/O Port (Input)" } },
            { 0x4214, new Label() { Name = "RDDIVL", Comment = "Unsigned Div Result (Quotient); lower byte" } },
            { 0x4215, new Label() { Name = "RDDIVH", Comment = "Unsigned Div Result (Quotient); higher byte" } },
            { 0x4216, new Label() { Name = "RDMPYL", Comment = "Unsigned Div Remainder / Mul Product; lower byte" } },
            { 0x4217, new Label() { Name = "RDMPYH", Comment = "Unsigned Div Remainder / Mul Product; higher byte" } },
            { 0x4218, new Label() { Name = "JOY1L", Comment = "Joypad 1 (Gameport 1; Pin 4); lower byte" } },
            { 0x4219, new Label() { Name = "JOY1H", Comment = "Joypad 1 (Gameport 1; Pin 4); higher byte" } },
            { 0x421A, new Label() { Name = "JOY2L", Comment = "Joypad 2 (Gameport 2; Pin 4); lower byte" } },
            { 0x421B, new Label() { Name = "JOY2H", Comment = "Joypad 2 (Gameport 2; Pin 4); higher byte" } },
            { 0x421C, new Label() { Name = "JOY3L", Comment = "Joypad 3 (Gameport 1; Pin 5); lower byte" } },
            { 0x421D, new Label() { Name = "JOY3H", Comment = "Joypad 3 (Gameport 1; Pin 5); higher byte" } },
            { 0x421E, new Label() { Name = "JOY4L", Comment = "Joypad 4 (Gameport 2; Pin 5); lower byte" } },
            { 0x421F, new Label() { Name = "JOY4H", Comment = "Joypad 4 (Gameport 2; Pin 5); higher byte" } },
            { 0x4300, new Label() { Name = "DMAP0", Comment = "DMA0 DMA/HDMA Parameters" } },
            { 0x4301, new Label() { Name = "BBAD0", Comment = "DMA0 DMA/HDMA I/O-Bus Address (PPU-Bus AKA B-Bus)" } },
            { 0x4302, new Label() { Name = "A1T0L", Comment = "DMA0 DMA/HDMA Table Start Address; lower byte" } },
            { 0x4303, new Label() { Name = "A1T0H", Comment = "DMA0 DMA/HDMA Table Start Address; higher byte" } },
            { 0x4304, new Label() { Name = "A1T0B", Comment = "DMA0 DMA/HDMA Table Start Address (Bank)" } },
            { 0x4305, new Label() { Name = "DAS0L", Comment = "DMA0 DMA Count; lower byte" } },
            { 0x4306, new Label() { Name = "DAS0H", Comment = "DMA0 DMA Count; higher byte" } },
            { 0x4307, new Label() { Name = "DAS0B", Comment = "DMA0 Indirect HDMA Address (Bank)" } },
            { 0x4308, new Label() { Name = "A2A0L", Comment = "DMA0 HDMA Table Address; lower byte" } },
            { 0x4309, new Label() { Name = "A2A0H", Comment = "DMA0 HDMA Table Address; higher byte" } },
            { 0x430A, new Label() { Name = "NTRL0", Comment = "DMA0 HDMA Line-Counter" } },
            { 0x4310, new Label() { Name = "DMAP1", Comment = "DMA1 DMA/HDMA Parameters" } },
            { 0x4311, new Label() { Name = "BBAD1", Comment = "DMA1 DMA/HDMA I/O-Bus Address (PPU-Bus AKA B-Bus)" } },
            { 0x4312, new Label() { Name = "A1T1L", Comment = "DMA1 DMA/HDMA Table Start Address; lower byte" } },
            { 0x4313, new Label() { Name = "A1T1H", Comment = "DMA1 DMA/HDMA Table Start Address; higher byte" } },
            { 0x4314, new Label() { Name = "A1T1B", Comment = "DMA1 DMA/HDMA Table Start Address (Bank)" } },
            { 0x4315, new Label() { Name = "DAS1L", Comment = "DMA1 DMA Count; lower byte" } },
            { 0x4316, new Label() { Name = "DAS1H", Comment = "DMA1 DMA Count; higher byte" } },
            { 0x4317, new Label() { Name = "DAS1B", Comment = "DMA1 Indirect HDMA Address (Bank)" } },
            { 0x4318, new Label() { Name = "A2A1L", Comment = "DMA1 HDMA Table Address; lower byte" } },
            { 0x4319, new Label() { Name = "A2A1H", Comment = "DMA1 HDMA Table Address; higher byte" } },
            { 0x431A, new Label() { Name = "NTRL1", Comment = "DMA1 HDMA Line-Counter" } },
            { 0x4320, new Label() { Name = "DMAP2", Comment = "DMA2 DMA/HDMA Parameters" } },
            { 0x4321, new Label() { Name = "BBAD2", Comment = "DMA2 DMA/HDMA I/O-Bus Address (PPU-Bus AKA B-Bus)" } },
            { 0x4322, new Label() { Name = "A1T2L", Comment = "DMA2 DMA/HDMA Table Start Address; lower byte" } },
            { 0x4323, new Label() { Name = "A1T2H", Comment = "DMA2 DMA/HDMA Table Start Address; higher byte" } },
            { 0x4324, new Label() { Name = "A1T2B", Comment = "DMA2 DMA/HDMA Table Start Address (Bank)" } },
            { 0x4325, new Label() { Name = "DAS2L", Comment = "DMA2 DMA Count; lower byte" } },
            { 0x4326, new Label() { Name = "DAS2H", Comment = "DMA2 DMA Count; higher byte" } },
            { 0x4327, new Label() { Name = "DAS2B", Comment = "DMA2 Indirect HDMA Address (Bank)" } },
            { 0x4328, new Label() { Name = "A2A2L", Comment = "DMA2 HDMA Table Address; lower byte" } },
            { 0x4329, new Label() { Name = "A2A2H", Comment = "DMA2 HDMA Table Address; higher byte" } },
            { 0x432A, new Label() { Name = "NTRL2", Comment = "DMA2 HDMA Line-Counter" } },
            { 0x4330, new Label() { Name = "DMAP3", Comment = "DMA3 DMA/HDMA Parameters" } },
            { 0x4331, new Label() { Name = "BBAD3", Comment = "DMA3 DMA/HDMA I/O-Bus Address (PPU-Bus AKA B-Bus)" } },
            { 0x4332, new Label() { Name = "A1T3L", Comment = "DMA3 DMA/HDMA Table Start Address; lower byte" } },
            { 0x4333, new Label() { Name = "A1T3H", Comment = "DMA3 DMA/HDMA Table Start Address; higher byte" } },
            { 0x4334, new Label() { Name = "A1T3B", Comment = "DMA3 DMA/HDMA Table Start Address (Bank)" } },
            { 0x4335, new Label() { Name = "DAS3L", Comment = "DMA3 DMA Count; lower byte" } },
            { 0x4336, new Label() { Name = "DAS3H", Comment = "DMA3 DMA Count; higher byte" } },
            { 0x4337, new Label() { Name = "DAS3B", Comment = "DMA3 Indirect HDMA Address (Bank)" } },
            { 0x4338, new Label() { Name = "A2A3L", Comment = "DMA3 HDMA Table Address; lower byte" } },
            { 0x4339, new Label() { Name = "A2A3H", Comment = "DMA3 HDMA Table Address; higher byte" } },
            { 0x433A, new Label() { Name = "NTRL3", Comment = "DMA3 HDMA Line-Counter" } },
            { 0x4340, new Label() { Name = "DMAP4", Comment = "DMA4 DMA/HDMA Parameters" } },
            { 0x4341, new Label() { Name = "BBAD4", Comment = "DMA4 DMA/HDMA I/O-Bus Address (PPU-Bus AKA B-Bus)" } },
            { 0x4342, new Label() { Name = "A1T4L", Comment = "DMA4 DMA/HDMA Table Start Address; lower byte" } },
            { 0x4343, new Label() { Name = "A1T4H", Comment = "DMA4 DMA/HDMA Table Start Address; higher byte" } },
            { 0x4344, new Label() { Name = "A1T4B", Comment = "DMA4 DMA/HDMA Table Start Address (Bank)" } },
            { 0x4345, new Label() { Name = "DAS4L", Comment = "DMA4 DMA Count; lower byte" } },
            { 0x4346, new Label() { Name = "DAS4H", Comment = "DMA4 DMA Count; higher byte" } },
            { 0x4347, new Label() { Name = "DAS4B", Comment = "DMA4 Indirect HDMA Address (Bank)" } },
            { 0x4348, new Label() { Name = "A2A4L", Comment = "DMA4 HDMA Table Address; lower byte" } },
            { 0x4349, new Label() { Name = "A2A4H", Comment = "DMA4 HDMA Table Address; higher byte" } },
            { 0x434A, new Label() { Name = "NTRL4", Comment = "DMA4 HDMA Line-Counter" } },
            { 0x4350, new Label() { Name = "DMAP5", Comment = "DMA5 DMA/HDMA Parameters" } },
            { 0x4351, new Label() { Name = "BBAD5", Comment = "DMA5 DMA/HDMA I/O-Bus Address (PPU-Bus AKA B-Bus)" } },
            { 0x4352, new Label() { Name = "A1T5L", Comment = "DMA5 DMA/HDMA Table Start Address; lower byte" } },
            { 0x4353, new Label() { Name = "A1T5H", Comment = "DMA5 DMA/HDMA Table Start Address; higher byte" } },
            { 0x4354, new Label() { Name = "A1T5B", Comment = "DMA5 DMA/HDMA Table Start Address (Bank)" } },
            { 0x4355, new Label() { Name = "DAS5L", Comment = "DMA5 DMA Count; lower byte" } },
            { 0x4356, new Label() { Name = "DAS5H", Comment = "DMA5 DMA Count; higher byte" } },
            { 0x4357, new Label() { Name = "DAS5B", Comment = "DMA5 Indirect HDMA Address (Bank)" } },
            { 0x4358, new Label() { Name = "A2A5L", Comment = "DMA5 HDMA Table Address; lower byte" } },
            { 0x4359, new Label() { Name = "A2A5H", Comment = "DMA5 HDMA Table Address; higher byte" } },
            { 0x435A, new Label() { Name = "NTRL5", Comment = "DMA5 HDMA Line-Counter" } },
            { 0x4360, new Label() { Name = "DMAP6", Comment = "DMA6 DMA/HDMA Parameters" } },
            { 0x4361, new Label() { Name = "BBAD6", Comment = "DMA6 DMA/HDMA I/O-Bus Address (PPU-Bus AKA B-Bus)" } },
            { 0x4362, new Label() { Name = "A1T6L", Comment = "DMA6 DMA/HDMA Table Start Address; lower byte" } },
            { 0x4363, new Label() { Name = "A1T6H", Comment = "DMA6 DMA/HDMA Table Start Address; higher byte" } },
            { 0x4364, new Label() { Name = "A1T6B", Comment = "DMA6 DMA/HDMA Table Start Address (Bank)" } },
            { 0x4365, new Label() { Name = "DAS6L", Comment = "DMA6 DMA Count; lower byte" } },
            { 0x4366, new Label() { Name = "DAS6H", Comment = "DMA6 DMA Count; higher byte" } },
            { 0x4367, new Label() { Name = "DAS6B", Comment = "DMA6 Indirect HDMA Address (Bank)" } },
            { 0x4368, new Label() { Name = "A2A6L", Comment = "DMA6 HDMA Table Address; lower byte" } },
            { 0x4369, new Label() { Name = "A2A6H", Comment = "DMA6 HDMA Table Address; higher byte" } },
            { 0x436A, new Label() { Name = "NTRL6", Comment = "DMA6 HDMA Line-Counter" } },
            { 0x4370, new Label() { Name = "DMAP7", Comment = "DMA7 DMA/HDMA Parameters" } },
            { 0x4371, new Label() { Name = "BBAD7", Comment = "DMA7 DMA/HDMA I/O-Bus Address (PPU-Bus AKA B-Bus)" } },
            { 0x4372, new Label() { Name = "A1T7L", Comment = "DMA7 DMA/HDMA Table Start Address; lower byte" } },
            { 0x4373, new Label() { Name = "A1T7H", Comment = "DMA7 DMA/HDMA Table Start Address; higher byte" } },
            { 0x4374, new Label() { Name = "A1T7B", Comment = "DMA7 DMA/HDMA Table Start Address (Bank)" } },
            { 0x4375, new Label() { Name = "DAS7L", Comment = "DMA7 DMA Count; lower byte" } },
            { 0x4376, new Label() { Name = "DAS7H", Comment = "DMA7 DMA Count; higher byte" } },
            { 0x4377, new Label() { Name = "DAS7B", Comment = "DMA7 Indirect HDMA Address (Bank)" } },
            { 0x4378, new Label() { Name = "A2A7L", Comment = "DMA7 HDMA Table Address; lower byte" } },
            { 0x4379, new Label() { Name = "A2A7H", Comment = "DMA7 HDMA Table Address; higher byte" } },
            { 0x437A, new Label() { Name = "NTRL7", Comment = "DMA7 HDMA Line-Counter" } }
        };
    }
}
