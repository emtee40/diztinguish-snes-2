﻿using Diz.Core.model;
using Diz.Core.util;
using static Diz.Core.import.BsnesImportStreamProcessor;

namespace Diz.Core.import
{
    public partial class BsnesTraceLogImporter
    {
        private int ConvertSnesToPc(int modDataSnesAddress)
        {
            // PERF: could use Data.ConvertSnesToPc(), but, by caching the two variables here,
            // we can save some locking and maybe speed things up.
            return RomUtil.ConvertSnesToPc(modDataSnesAddress, romMapModeCached, romSizeCached);
        }

        private static int GetNextSNESAddress(int modDataSnesAddress)
        {
            return RomUtil.CalculateSnesOffsetWithWrap(modDataSnesAddress, 1);
        }

        private static Data.FlagType GetFlagForInstructionPosition(int currentIndex)
        {
            return currentIndex == 0 ? Data.FlagType.Opcode : Data.FlagType.Operand;
        }

        private void UpdatePCAddress(ModificationData modData)
        {
            modData.Pc = ConvertSnesToPc(modData.SnesAddress);
            modData.IAPc = ConvertSnesToPc(modData.IndirectAddress);
        }
    }
}