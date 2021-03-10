﻿using Diz.Core.model;

namespace DiztinGUIsh.window
{
    public partial class MainWindow
    {
        private bool FindUnreached(int offset, bool end, bool direction, out int unreached)
        {
            var size = Project.Data.GetRomSize();
            unreached = end ? (direction ? 0 : size - 1) : offset;

            if (direction)
            {
                if (!end)
                    while (unreached < size - 1 && IsUnreached(unreached))
                        unreached++;
                
                while (unreached < size - 1 && IsReached(unreached)) 
                    unreached++;
            }
            else
            {
                if (unreached > 0) 
                    unreached--;
                
                while (unreached > 0 && IsReached(unreached)) 
                    unreached--;
            }

            while (unreached > 0 && IsUnreached(unreached - 1)) 
                unreached--;

            return IsUnreached(unreached);
        }

        private bool IsReached(int offset)
        {
            return Project.Data.GetFlag(offset) != Data.FlagType.Unreached;
        }

        private bool IsUnreached(int offset)
        {
            return Project.Data.GetFlag(offset) == Data.FlagType.Unreached;
        }

        private bool RomDataPresent()
        {
            return Project?.Data?.GetRomSize() > 0;
        }

        private bool IsOffsetInRange(int offset)
        {
            return offset >= 0 && offset < Project.Data.GetRomSize();
        }
    }
}