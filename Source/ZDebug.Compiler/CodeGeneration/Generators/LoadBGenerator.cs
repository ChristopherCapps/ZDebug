﻿using ZDebug.Compiler.Generate;
using ZDebug.Core.Instructions;

namespace ZDebug.Compiler.CodeGeneration.Generators
{
    internal class LoadBGenerator : LoadMemoryGenerator
    {
        public LoadBGenerator(Instruction instruction)
            : base(instruction)
        {
        }

        protected override int CalculateAddress(int address, int offset)
        {
            return address + offset;
        }

        protected override void EmitCalculateAddress(Operand addressOp, Operand offsetOp, ILBuilder il, ICompiler compiler)
        {
            compiler.EmitLoadOperand(addressOp);
            compiler.EmitLoadOperand(offsetOp);
            il.Math.Add();
        }

        protected override void LoadMemory(int address, ICompiler compiler)
        {
            compiler.EmitLoadMemoryByte(address);
        }

        protected override void LoadMemory(ILocal address, ICompiler compiler)
        {
            compiler.EmitLoadMemoryByte(address);
        }
    }
}
