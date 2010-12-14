﻿using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZDebug.Core.Execution;
using ZDebug.Core.Instructions;
using ZDebug.UI.Controls;
using ZDebug.UI.Services;
using ZDebug.UI.Utilities;

namespace ZDebug.UI.ViewModel
{
    internal sealed class DisassemblyViewModel : ViewModelWithViewBase<UserControl>
    {
        private struct AddressAndIndex
        {
            public readonly int Address;
            public readonly int Index;

            public AddressAndIndex(int address, int index)
            {
                this.Address = address;
                this.Index = index;
            }
        }

        private readonly BulkObservableCollection<DisassemblyLineViewModel> lines;
        private readonly Dictionary<int, DisassemblyLineViewModel> addressToLineMap;
        private readonly List<AddressAndIndex> routineAddressAndIndexList;

        private DisassemblyLineViewModel inputLine;

        public DisassemblyViewModel()
            : base("DisassemblyView")
        {
            lines = new BulkObservableCollection<DisassemblyLineViewModel>();
            addressToLineMap = new Dictionary<int, DisassemblyLineViewModel>();
            routineAddressAndIndexList = new List<AddressAndIndex>();
        }

        private DisassemblyLineViewModel GetLineByAddress(int address)
        {
            return addressToLineMap[address];
        }

        private void DebuggerService_StoryOpened(object sender, StoryEventArgs e)
        {
            var reader = e.Story.Memory.CreateReader(0);

            DisassemblyLineViewModel ipLine;

            lines.BeginBulkOperation();
            try
            {
                var routineTable = e.Story.RoutineTable;

                for (int rIndex = 0; rIndex < routineTable.Count; rIndex++)
                {
                    if (rIndex > 0)
                    {
                        lines.Add(DisassemblyBlankLineViewModel.Instance);
                    }

                    var routine = routineTable[rIndex];

                    var routineHeaderLine = new DisassemblyRoutineHeaderLineViewModel(routine);
                    routineAddressAndIndexList.Add(new AddressAndIndex(routineHeaderLine.Address, lines.Count));
                    lines.Add(routineHeaderLine);
                    addressToLineMap.Add(routine.Address, routineHeaderLine);

                    lines.Add(DisassemblyBlankLineViewModel.Instance);

                    foreach (var i in routine.Instructions)
                    {
                        var instructionLine = new DisassemblyInstructionLineViewModel(i);
                        if (DebuggerService.BreakpointExists(i.Address))
                        {
                            instructionLine.HasBreakpoint = true;
                        }
                        lines.Add(instructionLine);
                        addressToLineMap.Add(i.Address, instructionLine);
                    }
                }

                ipLine = GetLineByAddress(e.Story.Processor.PC);
                ipLine.HasIP = true;
            }
            finally
            {
                lines.EndBulkOperation();
            }

            BringLineIntoView(ipLine);

            e.Story.Processor.Stepped += Processor_Stepped;
            e.Story.Processor.EnterFrame += Processor_EnterFrame;
            e.Story.Processor.ExitFrame += Processor_ExitFrame;
            e.Story.RoutineTable.RoutineAdded += RoutineTable_RoutineAdded;
        }

        private void BringLineIntoView(DisassemblyLineViewModel line)
        {
            var lines = this.View.FindName<ItemsControl>("lines");
            lines.BringIntoView(line);
        }

        private void Processor_Stepped(object sender, ProcessorSteppedEventArgs e)
        {
            var oldLine = GetLineByAddress(e.OldPC);
            oldLine.HasIP = false;

            if (DebuggerService.State == DebuggerState.Running ||
                DebuggerService.State == DebuggerState.AwaitingInput ||
                DebuggerService.State == DebuggerState.Done)
            {
                return;
            }

            var newLine = GetLineByAddress(e.NewPC);
            newLine.HasIP = true;

            BringLineIntoView(newLine);
        }

        private void Processor_EnterFrame(object sender, StackFrameEventArgs e)
        {
            var returnLine = GetLineByAddress(e.NewFrame.ReturnAddress);
            returnLine.IsNextInstruction = true;
            returnLine.ToolTip = new CallToolTip(e.NewFrame);
        }

        private void Processor_ExitFrame(object sender, StackFrameEventArgs e)
        {
            var returnLine = GetLineByAddress(e.OldFrame.ReturnAddress);
            returnLine.IsNextInstruction = false;
            returnLine.ToolTip = false;
        }

        private void RoutineTable_RoutineAdded(object sender, RoutineAddedEventArgs e)
        {
            // FInd routine header line that would follow this routine
            int nextRoutineIndex = -1;
            int insertionPoint = -1;
            for (int i = 0; i < routineAddressAndIndexList.Count; i++)
            {
                var addressAndIndex = routineAddressAndIndexList[i];
                if (addressAndIndex.Address > e.Routine.Address)
                {
                    nextRoutineIndex = i;
                    insertionPoint = addressAndIndex.Index;
                    break;
                }
            }

            // If no routine header found, insert at the end of the list.
            if (nextRoutineIndex == -1)
            {
                insertionPoint = lines.Count;
            }

            var count = 0;
            var routineHeaderLine = new DisassemblyRoutineHeaderLineViewModel(e.Routine);
            lines.Insert(insertionPoint, routineHeaderLine);
            count++;
            addressToLineMap.Add(e.Routine.Address, routineHeaderLine);

            lines.Insert(insertionPoint + count, DisassemblyBlankLineViewModel.Instance);
            count++;

            foreach (var i in e.Routine.Instructions)
            {
                var instructionLine = new DisassemblyInstructionLineViewModel(i);
                if (DebuggerService.BreakpointExists(i.Address))
                {
                    instructionLine.HasBreakpoint = true;
                }
                lines.Insert(insertionPoint + count, instructionLine);
                count++;
                addressToLineMap.Add(i.Address, instructionLine);
            }

            if (nextRoutineIndex >= 0)
            {
                lines.Insert(insertionPoint + count, DisassemblyBlankLineViewModel.Instance);
                count++;

                // fix up routine indeces...
                for (int i = nextRoutineIndex; i < routineAddressAndIndexList.Count; i++)
                {
                    var addressAndIndex = routineAddressAndIndexList[i];
                    routineAddressAndIndexList[i] = new AddressAndIndex(addressAndIndex.Address, addressAndIndex.Index + count);
                }

                routineAddressAndIndexList.Insert(nextRoutineIndex, new AddressAndIndex(e.Routine.Address, insertionPoint));
            }
            else
            {
                routineAddressAndIndexList.Add(new AddressAndIndex(e.Routine.Address, insertionPoint));
            }
        }

        private void DebuggerService_StoryClosed(object sender, StoryEventArgs e)
        {
            lines.Clear();
            addressToLineMap.Clear();
            routineAddressAndIndexList.Clear();
        }

        private void DebuggerService_StateChanged(object sender, DebuggerStateChangedEventArgs e)
        {
            if (e.NewState == DebuggerState.Unavailable)
            {
                return;
            }

            if (e.NewState == DebuggerState.StoppedAtError)
            {
                var line = GetLineByAddress(DebuggerService.Story.Processor.ExecutingInstruction.Address);
                line.State = DisassemblyLineState.Blocked;
                line.ToolTip = new ExceptionToolTip(DebuggerService.CurrentException);
                BringLineIntoView(line);
            }
            else if (e.OldState == DebuggerState.Running && e.NewState == DebuggerState.Stopped)
            {
                var line = GetLineByAddress(DebuggerService.Story.Processor.PC);
                line.HasIP = true;
                BringLineIntoView(line);
            }
            else if (e.NewState == DebuggerState.Done)
            {
                var line = GetLineByAddress(DebuggerService.Story.Processor.ExecutingInstruction.Address);
                line.State = DisassemblyLineState.Stopped;
                BringLineIntoView(line);
            }
            else if (e.NewState == DebuggerState.AwaitingInput)
            {
                inputLine = GetLineByAddress(DebuggerService.Story.Processor.ExecutingInstruction.Address);
                inputLine.State = DisassemblyLineState.Paused;
                BringLineIntoView(inputLine);
            }
            else if (e.OldState == DebuggerState.AwaitingInput)
            {
                inputLine.State = DisassemblyLineState.None;
                inputLine = null;

                var ipLine = GetLineByAddress(DebuggerService.Story.Processor.PC);
                ipLine.HasIP = true;
            }
        }

        private void DebuggerService_BreakpointRemoved(object sender, BreakpointEventArgs e)
        {
            var bpLine = GetLineByAddress(e.Address) as DisassemblyInstructionLineViewModel;
            if (bpLine != null)
            {
                bpLine.HasBreakpoint = false;
            }
        }

        private void DebuggerService_BreakpointAdded(object sender, BreakpointEventArgs e)
        {
            var bpLine = GetLineByAddress(e.Address) as DisassemblyInstructionLineViewModel;
            if (bpLine != null)
            {
                bpLine.HasBreakpoint = true;
            }
        }

        protected internal override void Initialize()
        {
            DebuggerService.StoryOpened += DebuggerService_StoryOpened;
            DebuggerService.StoryClosed += DebuggerService_StoryClosed;

            DebuggerService.StateChanged += DebuggerService_StateChanged;

            DebuggerService.BreakpointAdded += DebuggerService_BreakpointAdded;
            DebuggerService.BreakpointRemoved += DebuggerService_BreakpointRemoved;

            var typeface = new Typeface(this.View.FontFamily, this.View.FontStyle, this.View.FontWeight, this.View.FontStretch);

            var addressText = new FormattedText("  000000: ", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, this.View.FontSize, this.View.Foreground);
            this.View.Resources["addressWidth"] = new GridLength(addressText.WidthIncludingTrailingWhitespace);

            var opcodeName = new FormattedText("check_arg_count  ", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, this.View.FontSize, this.View.Foreground);
            this.View.Resources["opcodeWidth"] = new GridLength(opcodeName.WidthIncludingTrailingWhitespace);

        }

        public BulkObservableCollection<DisassemblyLineViewModel> Lines
        {
            get { return lines; }
        }
    }
}
