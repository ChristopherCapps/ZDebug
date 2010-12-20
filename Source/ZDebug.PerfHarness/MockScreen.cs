﻿using System;
using System.IO;
using System.Text;
using ZDebug.Core.Execution;

namespace ZDebug.PerfHarness
{
    internal sealed class MockScreen : IScreen
    {
        private readonly Action readAction;
        private readonly string[] commands;
        private int commandIndex;
        private StringBuilder output;

        public MockScreen(Action readAction)
        {
            this.readAction = readAction;
        }

        public MockScreen(string scriptPath)
        {
            this.commands = File.ReadAllLines(scriptPath);
            this.commandIndex = 0;
            this.output = new StringBuilder();
        }

        public void Clear(int window)
        {
        }

        public void ClearAll(bool unsplit = false)
        {
        }

        public void Split(int height)
        {
        }

        public void Unsplit()
        {
        }

        public void SetWindow(int window)
        {
        }

        public int GetCursorLine()
        {
            return 0;
        }

        public int GetCursorColumn()
        {
            return 0;
        }

        public void SetCursor(int line, int column)
        {
        }

        public void SetTextStyle(ZTextStyle style)
        {
        }

        public void SetForegroundColor(ZColor color)
        {
        }

        public void SetBackgroundColor(ZColor color)
        {
        }

        public ZFont SetFont(ZFont font)
        {
            return 0;
        }

        public void ShowStatus()
        {
        }

        public byte ScreenHeightInLines
        {
            get { return 0; }
        }

        public byte ScreenWidthInColumns
        {
            get { return 0; }
        }

        public ushort ScreenHeightInUnits
        {
            get { return 0; }
        }

        public ushort ScreenWidthInUnits
        {
            get { return 0; }
        }

        public byte FontHeightInUnits
        {
            get { return 0; }
        }

        public byte FontWidthInUnits
        {
            get { return 0; }
        }

        public event EventHandler DimensionsChanged;

        public bool SupportsColors
        {
            get { return false; }
        }

        public bool SupportsBold
        {
            get { return false; }
        }

        public bool SupportsItalic
        {
            get { return false; }
        }

        public bool SupportsFixedFont
        {
            get { return false; }
        }

        public ZColor DefaultBackgroundColor
        {
            get { return ZColor.Default; }
        }

        public ZColor DefaultForegroundColor
        {
            get { return ZColor.Default; }
        }

        public void Print(string text)
        {
            if (output != null)
            {
                output.Append(text);
            }
        }

        public void Print(char ch)
        {
            if (output != null)
            {
                output.Append(ch);
            }
        }

        public void ReadChar(Action<char> callback)
        {
            readAction();
        }

        public void ReadCommand(int maxChars, Action<string> callback)
        {
            if (readAction != null)
            {
                readAction();
            }
            else
            {
                var command = commands[commandIndex++];
                output.AppendLine(command);
                callback(command);
            }
        }

        public string Output
        {
            get { return output.ToString(); }
        }
    }
}