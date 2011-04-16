﻿using System.Reflection.Emit;
using ZDebug.Compiler.CodeGeneration;
using ZDebug.Compiler.Generate;
using ZDebug.Core.Collections;
using ZDebug.Core.Instructions;
using ZDebug.Core.Utilities;

namespace ZDebug.Compiler
{
    internal partial class ZCompiler : ICompiler
    {
        /// <summary>
        /// Unpacks the byte address on the evaluation stack as a routine address.
        /// </summary>
        private void UnpackRoutineAddress()
        {
            byte version = machine.Version;
            if (version < 4)
            {
                il.Math.Multiply(2);
            }
            else if (version < 8)
            {
                il.Math.Multiply(4);
            }
            else // 8
            {
                il.Math.Multiply(8);
            }

            if (version >= 6 && version <= 7)
            {
                il.Math.Add(machine.RoutinesOffset * 8);
            }
        }

        private void LoadUnpackedRoutineAddress(Operand op)
        {
            switch (op.Kind)
            {
                case OperandKind.LargeConstant:
                case OperandKind.SmallConstant:
                    il.Load(machine.UnpackRoutineAddress(op.Value));
                    break;

                default: // OperandKind.Variable
                    EmitLoadVariable((byte)op.Value);
                    UnpackRoutineAddress();
                    break;
            }
        }

        private void LoadUnpackedStringAddress(Operand op)
        {
            switch (op.Kind)
            {
                case OperandKind.LargeConstant:
                case OperandKind.SmallConstant:
                    il.Load(machine.UnpackStringAddress(op.Value));
                    break;

                default: // OperandKind.Variable
                    EmitLoadVariable((byte)op.Value);

                    byte version = machine.Version;
                    if (version < 4)
                    {
                        il.Math.Multiply(2);
                    }
                    else if (version < 8)
                    {
                        il.Math.Multiply(4);
                    }
                    else // 8
                    {
                        il.Math.Multiply(8);
                    }

                    if (version >= 6 && version <= 7)
                    {
                        il.Math.Add(machine.StringsOffset * 8);
                    }
                    break;
            }
        }

        public ILabel GetLabel(int address)
        {
            ILabel result;
            if (addressToLabelMap.TryGetValue(address, out result))
            {
                return result;
            }

            throw new ZCompilerException(string.Format("Could not find label for address, {0:x4}", address));
        }

        public void EmitLoadOperand(Operand operand)
        {
            switch (operand.Kind)
            {
                case OperandKind.LargeConstant:
                case OperandKind.SmallConstant:
                    il.Load(operand.Value);
                    break;

                default: // OperandKind.Variable
                    EmitLoadVariable((byte)operand.Value);
                    break;
            }
        }

        public void EmitBranch(Branch branch)
        {
            // It is expected that the value on the top of the evaluation stack
            // is the boolean value to compare branch.Condition with.

            var noJump = il.NewLabel();

            il.Load(branch.Condition);
            noJump.BranchIf(Condition.NotEqual, @short: true);

            switch (branch.Kind)
            {
                case BranchKind.RFalse:
                    il.DebugWrite("  > branching rfalse...");
                    il.Load(0);
                    EmitReturn();
                    break;

                case BranchKind.RTrue:
                    il.DebugWrite("  > branching rtrue...");
                    il.Load(1);
                    EmitReturn();
                    break;

                default: // BranchKind.Address
                    var address = branch.TargetAddress;
                    var jump = addressToLabelMap[address];
                    il.DebugWrite(string.Format("  > branching to {0:x4}...", address));
                    jump.Branch();
                    break;
            }

            noJump.Mark();
        }

        public void EmitReturn()
        {
            Profiler_ExitRoutine();

            il.Return();
        }

        private void EmitDirectCall(Operand addressOp, ReadOnlyArray<Operand> args)
        {
            if (machine.Profiling)
            {
                il.Arguments.LoadMachine();
                if (addressOp.Value == 0)
                {
                    il.Load(0);
                }
                else
                {
                    il.Load(machine.UnpackRoutineAddress(addressOp.Value));
                }

                il.Load(false);
                il.Call(Reflection<CompiledZMachine>.GetMethod("Profiler_Call", Types.Array<int, bool>(), @public: false));
            }

            if (addressOp.Value == 0)
            {
                il.Load(0);
                EmitReturn();
            }
            else
            {
                var address = machine.UnpackRoutineAddress(addressOp.Value);
                var routineCall = machine.GetRoutineCall(address);
                var index = calls.Count;
                calls.Add(routineCall);

                // load routine call
                il.Arguments.LoadCalls();
                il.Load(index);
                il.Emit(OpCodes.Ldelem_Ref);

                foreach (var arg in args)
                {
                    EmitLoadOperand(arg);
                }

                // The memory, stack and stack pointer are the last arguments passed in
                // case any operands manipulate them.
                il.Arguments.LoadMemory();
                il.Arguments.LoadStack();
                il.Arguments.LoadSP();

                il.Call(ZRoutineCall.GetInvokeMethod(args.Length));
            }
        }

        private void EmitCalculatedCall(Operand addressOp, ReadOnlyArray<Operand> args)
        {
            using (var address = il.NewLocal<int>())
            {
                EmitLoadVariable((byte)addressOp.Value);
                address.Store();

                // is this address 0?
                var nonZeroCall = il.NewLabel();
                var done = il.NewLabel();
                address.Load();
                nonZeroCall.BranchIf(Condition.True);

                if (machine.Profiling)
                {
                    il.Arguments.LoadMachine();
                    il.Load(0);
                    il.Load(true);

                    var profilerCall = Reflection<CompiledZMachine>.GetMethod("Profiler_Call", Types.Array<int, bool>(), @public: false);
                    il.Call(profilerCall);
                }

                // discard any SP operands...
                int spOperands = 0;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i].IsStackVariable)
                    {
                        spOperands++;
                    }
                }

                if (spOperands > 0)
                {
                    il.Arguments.LoadSP();
                    il.Math.Subtract(spOperands);
                    il.Arguments.StoreSP();
                }

                il.Load(0);

                done.Branch();

                nonZeroCall.Mark();

                if (machine.Profiling)
                {
                    il.Arguments.LoadMachine();
                    address.Load();
                    UnpackRoutineAddress();
                    il.Load(true);

                    il.Call(Reflection<CompiledZMachine>.GetMethod("Profiler_Call", Types.Array<int, bool>(), @public: false));
                }

                il.Arguments.LoadMachine();
                address.Load();
                UnpackRoutineAddress();

                il.Call(Reflection<CompiledZMachine>.GetMethod("GetRoutineCall", Types.Array<int>(), @public: false));

                foreach (var arg in args)
                {
                    EmitLoadOperand(arg);
                }

                // The stack and stack pointer are the last arguments passed in
                // case any operands manipulate them.
                il.Arguments.LoadMemory();
                il.Arguments.LoadStack();
                il.Arguments.LoadSP();

                il.Call(ZRoutineCall.GetInvokeMethod(args.Length));

                done.Mark();
            }
        }

        public void EmitCall(Operand address, ReadOnlyArray<Operand> args)
        {
            if (address.Kind != OperandKind.Variable)
            {
                EmitDirectCall(address, args);
            }
            else
            {
                EmitCalculatedCall(address, args);
            }
        }

        private void EmitLoadMemoryByte(CodeBuilder loadAddress)
        {
            il.Arguments.LoadMemory();
            loadAddress();
            il.Emit(OpCodes.Ldelem_U1);
        }

        public void EmitLoadMemoryByte(int address)
        {
            EmitLoadMemoryByte(
                loadAddress: () => il.Load(address));
        }

        public void EmitLoadMemoryByte(ILocal address)
        {
            EmitLoadMemoryByte(
                loadAddress: () => address.Load());
        }

        private void EmitLoadMemoryWord(CodeBuilder loadAddress)
        {
            // shift memory[address] left 8 bits
            EmitLoadMemoryByte(loadAddress);
            il.Math.Shl(8);

            // read memory[address + 1]
            EmitLoadMemoryByte(() =>
            {
                loadAddress();
                il.Math.Add(1);
            });

            // or bytes together
            il.Math.Or();
            il.Convert.ToUInt16();
        }

        public void EmitLoadMemoryWord(int address)
        {
            EmitLoadMemoryWord(
                loadAddress: () => il.Load(address));
        }

        public void EmitLoadMemoryWord(ILocal address)
        {
            EmitLoadMemoryWord(
                loadAddress: () => address.Load());
        }

        private void EmitStoreMemoryByte(CodeBuilder loadAddress, CodeBuilder loadValue)
        {
            il.Arguments.LoadMemory();
            loadAddress();
            loadValue();
            il.Emit(OpCodes.Stelem_I1);
        }

        public void EmitStoreMemoryByte(int address, ILocal value)
        {
            EmitStoreMemoryByte(
                loadAddress: () => il.Load(address),
                loadValue: () => value.Load());
        }

        public void EmitStoreMemoryByte(ILocal address, ILocal value)
        {
            EmitStoreMemoryByte(
                loadAddress: () => address.Load(),
                loadValue: () => value.Load());
        }

        private void EmitStoreMemoryWord(CodeBuilder loadAddress, CodeBuilder loadValue)
        {
            // memory[address] = (byte)(value >> 8);
            EmitStoreMemoryByte(
                loadAddress,
                () =>
                {
                    loadValue();
                    il.Math.Shr(8);
                    il.Convert.ToUInt8();
                });

            // memory[address + 1] = (byte)(value & 0xff);
            EmitStoreMemoryByte(
                () =>
                {
                    loadAddress();
                    il.Math.Add(1);
                },
                () =>
                {
                    loadValue();
                    il.Math.And(0xff);
                    il.Convert.ToUInt8();
                });
        }

        public void EmitStoreMemoryWord(int address, ILocal value)
        {
            EmitStoreMemoryWord(
                loadAddress: () => il.Load(address),
                loadValue: () => value.Load());
        }

        public void EmitStoreMemoryWord(ILocal address, ILocal value)
        {
            EmitStoreMemoryWord(
                loadAddress: () => address.Load(),
                loadValue: () => value.Load());
        }

        public void EmitPopStack(bool indirect = false)
        {
            il.Arguments.LoadStack();
            il.Arguments.LoadSP();
            il.Emit(OpCodes.Ldelem_U2);

            if (!indirect)
            {
                // decrement sp
                il.Arguments.LoadSP();
                il.Math.Subtract(1);
                il.Arguments.StoreSP();
            }
        }

        public void EmitPushStack(ILocal value, bool indirect = false)
        {
            if (!indirect)
            {
                // increment sp
                il.Arguments.LoadSP();
                il.Math.Add(1);
                il.Arguments.StoreSP();
            }

            il.Arguments.LoadStack();
            il.Arguments.LoadSP();
            value.Load();
            il.Emit(OpCodes.Stelem_I2);
        }

        private void EmitLoadLocalVariable(byte variableIndex)
        {
            il.Arguments.LoadLocals();
            il.Load(variableIndex);
            il.Emit(OpCodes.Ldelem_U2);
        }

        private void EmitLoadLocalVariable(ILocal variableIndex)
        {
            il.Arguments.LoadLocals();
            variableIndex.Load();
            il.Emit(OpCodes.Ldelem_U2);
        }

        private void EmitStoreLocalVariable(byte variableIndex, ILocal value)
        {
            il.Arguments.LoadLocals();
            il.Load(variableIndex);
            value.Load();
            il.Emit(OpCodes.Stelem_I2);
        }

        private void EmitStoreLocalVariable(ILocal variableIndex, ILocal value)
        {
            il.Arguments.LoadLocals();
            variableIndex.Load();
            value.Load();
            il.Emit(OpCodes.Stelem_I2);
        }

        private void EmitLoadGlobalVariable(byte variableIndex)
        {
            var address = (variableIndex * 2) + machine.GlobalVariableTableAddress;
            EmitLoadMemoryWord(address);
        }

        private void EmitLoadGlobalVariable(ILocal variableIndex)
        {
            using (var address = il.NewLocal<int>())
            {
                variableIndex.Load();
                il.Math.Multiply(2);
                il.Math.Add(machine.GlobalVariableTableAddress);
                address.Store();

                EmitLoadMemoryWord(address);
            }
        }

        private void EmitStoreGlobalVariable(byte variableIndex, ILocal value)
        {
            var address = (variableIndex * 2) + machine.GlobalVariableTableAddress;
            EmitStoreMemoryWord(address, value);
        }

        private void EmitStoreGlobalVariable(ILocal variableIndex, ILocal value)
        {
            using (var address = il.NewLocal<int>())
            {
                variableIndex.Load();
                il.Math.Multiply(2);
                il.Math.Add(machine.GlobalVariableTableAddress);
                address.Store();

                EmitStoreMemoryWord(address, value);
            }
        }

        public void EmitLoadVariable(byte variableIndex, bool indirect = false)
        {
            if (variableIndex == 0)
            {
                EmitPopStack(indirect);
            }
            else if (variableIndex < 16)
            {
                EmitLoadLocalVariable((byte)(variableIndex - 1));
            }
            else
            {
                EmitLoadGlobalVariable((byte)(variableIndex - 16));
            }
        }

        public void EmitLoadVariable(ILocal variableIndex, bool indirect = false)
        {
            il.DebugIndent();

            var tryLocal = il.NewLabel();
            var tryGlobal = il.NewLabel();
            var done = il.NewLabel();

            // branch if this is not the stack (variableIndex > 0)
            variableIndex.Load();
            tryLocal.BranchIf(Condition.True, @short: true);

            // stack
            if (usesStack)
            {
                EmitPopStack(indirect);
                done.Branch(@short: true);
            }
            else
            {
                il.RuntimeError("Unexpected stack access.");
            }

            // local
            tryLocal.Mark();

            // branch if this is not a local (variableIndex >= 16)
            variableIndex.Load();
            il.Load(16);
            tryGlobal.BranchIf(Condition.AtLeast, @short: true);

            if (routine.Locals.Length > 0)
            {
                variableIndex.Load();
                il.Math.Subtract(1);

                using (var localVariableIndex = il.NewLocal<byte>())
                {
                    localVariableIndex.Store();
                    EmitLoadLocalVariable(localVariableIndex);
                }

                done.Branch(@short: true);
            }
            else
            {
                il.RuntimeError("Unexpected read from local variable {0}.", variableIndex);
            }

            // global
            tryGlobal.Mark();

            if (usesMemory)
            {
                variableIndex.Load();
                il.Math.Subtract(16);

                using (var globalVariableIndex = il.NewLocal<byte>())
                {
                    globalVariableIndex.Store();
                    EmitLoadGlobalVariable(globalVariableIndex);
                }
            }
            else
            {
                il.RuntimeError("Unexpected global variable access.");
            }

            done.Mark();

            il.DebugUnindent();

            calculatedLoadVariableCount++;
        }

        public void EmitStoreVariable(byte variableIndex, ILocal value, bool indirect = false)
        {
            if (variableIndex == 0)
            {
                EmitPushStack(value, indirect);
            }
            else if (variableIndex < 16)
            {
                EmitStoreLocalVariable((byte)(variableIndex - 1), value);
            }
            else
            {
                EmitStoreGlobalVariable((byte)(variableIndex - 16), value);
            }
        }

        public void EmitStoreVariable(Variable variable, ILocal value, bool indirect = false)
        {
            switch (variable.Kind)
            {
                case VariableKind.Stack:
                    EmitPushStack(value, indirect);
                    break;

                case VariableKind.Local:
                    EmitStoreLocalVariable(variable.Index, value);
                    break;

                case VariableKind.Global:
                    EmitStoreGlobalVariable(variable.Index, value);
                    break;
            }
        }

        public void EmitStoreVariable(ILocal variableIndex, ILocal value, bool indirect = false)
        {
            il.DebugIndent();

            var tryLocal = il.NewLabel();
            var tryGlobal = il.NewLabel();
            var done = il.NewLabel();

            // branch if this is not the stack (variableIndex > 0)
            variableIndex.Load();
            tryLocal.BranchIf(Condition.True, @short: true);

            // stack
            if (usesStack)
            {
                EmitPushStack(value, indirect);
                done.Branch(@short: true);
            }
            else
            {
                il.RuntimeError("Unexpected stack access.");
            }

            // local
            tryLocal.Mark();

            // branch if this is not a local (variableIndex >= 16)
            variableIndex.Load();
            il.Load(16);
            tryGlobal.BranchIf(Condition.AtLeast, @short: true);

            if (routine.Locals.Length > 0)
            {
                variableIndex.Load();
                il.Math.Subtract(1);

                using (var localVariableIndex = il.NewLocal<byte>())
                {
                    localVariableIndex.Store();
                    EmitStoreLocalVariable(localVariableIndex, value);
                }

                done.Branch(@short: true);
            }
            else
            {
                il.RuntimeError("Unexpected write to local variable {0}.", variableIndex);
            }

            // global
            tryGlobal.Mark();

            if (usesMemory)
            {
                variableIndex.Load();
                il.Math.Subtract(16);

                using (var globalVariableIndex = il.NewLocal<byte>())
                {
                    globalVariableIndex.Store();
                    EmitStoreGlobalVariable(globalVariableIndex, value);
                }
            }
            else
            {
                il.RuntimeError("Unexpected global variable access.");
            }

            done.Mark();

            il.DebugUnindent();

            calculatedStoreVariableCount++;
        }
        private void EmitLoadObjectParent(int objNum)
        {
            var address = (ushort)(CalculateObjectAddress(objNum) + machine.ObjectParentOffset);

            ReadObjectNumber(address);
        }

        public void EmitLoadObjectParent(Operand operand)
        {
            switch (operand.Kind)
            {
                case OperandKind.LargeConstant:
                case OperandKind.SmallConstant:
                    ReadObjectParent(operand.Value);
                    break;

                case OperandKind.Variable:
                    EmitLoadOperand(operand);
                    ReadObjectParent();
                    break;
            }
        }
    }
}
