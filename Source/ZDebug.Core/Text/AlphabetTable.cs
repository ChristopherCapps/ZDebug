﻿using System;
using ZDebug.Core.Basics;

namespace ZDebug.Core.Text
{
    internal sealed class AlphabetTable
    {
        private const string A0 = "??????abcdefghijklmnopqrstuvwxys";
        private const string A1 = "??????ABCDEFGHIJKLMNOPQRSTUVWXYS";
        private const string A2 = "???????\n0123456789.,!?_#'\"/\\-:()";
        private const string A3 = "???????0123456789.,!?_#'\"/\\<-:()";

        private readonly string[] alphabets;

        private int currentAlphabet;
        private int baseAlphabet;

        public AlphabetTable(Memory memory)
        {
            var version = memory.ReadVersion();
            if (version == 1)
            {
                alphabets = new string[] { A0, A1, A3 };
            }
            else if (version >= 2 || version <= 4)
            {
                alphabets = new string[] { A0, A1, A2 };
            }
            else if (version >= 5 || version <= 8)
            {
                var alphabetTableAddress = memory.ReadAlphabetTableAddress();
                if (alphabetTableAddress == 0)
                {
                    alphabets = new string[] { A0, A1, A2 };
                }
                else
                {
                    alphabets = memory.ReadCustomAlphabetTable(alphabetTableAddress);
                }
            }
            else
            {
                throw new InvalidOperationException("Invalid version number: " + version);
            }
        }

        public void Reset()
        {
            currentAlphabet = baseAlphabet;
        }

        public void Shift()
        {
            currentAlphabet = (baseAlphabet + 1) % 3;
        }

        public void DoubleShift()
        {
            currentAlphabet = (baseAlphabet + 2) % 3;
        }

        public void ShiftLock()
        {
            baseAlphabet = (baseAlphabet + 1) % 3;
            currentAlphabet = baseAlphabet;
        }

        public void DoubleShiftLock()
        {
            baseAlphabet = (baseAlphabet + 2) % 3;
            currentAlphabet = baseAlphabet;
        }

        public char ReadChar(byte zchar)
        {
            if (zchar < 6 || zchar > 31)
            {
                throw new ArgumentOutOfRangeException("zchar");
            }

            var result = alphabets[currentAlphabet][zchar];
            Reset();

            return result;
        }

        public int CurrentAlphabet
        {
            get { return currentAlphabet; }
        }
    }
}