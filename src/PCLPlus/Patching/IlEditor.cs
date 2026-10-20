using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet.Emit;

namespace PCLPlus.Patching
{
    /// <summary>
    /// Safe editing primitives for method bodies.
    /// </summary>
    internal static class IlEditor
    {
        /// <summary>
        /// Replaces the inclusive range [<paramref name="start"/>, <paramref name="end"/>] with <paramref name="replacement"/>.
        /// </summary>
        /// <remarks>
        /// The first instruction object of the range is reused, so branches and exception handlers
        /// that point at the start of the range stay valid. Any other reference into the range is rejected.
        /// </remarks>
        public static void ReplaceRange(CilBody body, int start, int end, IList<Instruction> replacement)
        {
            var instructions = body.Instructions;
            if (start < 0 || end < start || end >= instructions.Count || replacement.Count == 0)
                throw new PatchException("invalid replacement range");

            var removed = new HashSet<Instruction>();
            for (int i = start + 1; i <= end; i++)
                removed.Add(instructions[i]);
            EnsureNotReferencedFromOutside(body, start, end, removed);

            var head = instructions[start];
            head.OpCode = replacement[0].OpCode;
            head.Operand = replacement[0].Operand;

            for (int i = end; i > start; i--)
                instructions.RemoveAt(i);
            for (int i = 1; i < replacement.Count; i++)
                instructions.Insert(start + i, replacement[i]);

            // Branch targets inside the replacement may still point at the original first instruction object.
            RetargetTo(replacement, replacement[0], head);
        }

        /// <summary>Replaces the whole body, dropping all existing exception handlers.</summary>
        public static void ReplaceBody(CilBody body, IEnumerable<Instruction> instructions)
        {
            body.ExceptionHandlers.Clear();
            body.Instructions.Clear();
            foreach (var instruction in instructions)
                body.Instructions.Add(instruction);
        }

        /// <summary>Inserts <paramref name="instructions"/> at the very beginning of the body.</summary>
        public static void Prepend(CilBody body, IList<Instruction> instructions)
        {
            for (int i = 0; i < instructions.Count; i++)
                body.Instructions.Insert(i, instructions[i]);
        }

        private static void RetargetTo(IEnumerable<Instruction> instructions, Instruction from, Instruction to)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.Operand == from)
                    instruction.Operand = to;
                else if (instruction.Operand is Instruction[] targets)
                    instruction.Operand = targets.Select(t => t == from ? to : t).ToArray();
            }
        }

        private static void EnsureNotReferencedFromOutside(CilBody body, int start, int end, HashSet<Instruction> removed)
        {
            var instructions = body.Instructions;
            for (int i = 0; i < instructions.Count; i++)
            {
                if (i >= start && i <= end)
                    continue;

                var operand = instructions[i].Operand;
                if (operand is Instruction target && removed.Contains(target))
                    throw new PatchException("target code is referenced by a branch from outside the range");
                if (operand is Instruction[] targets && targets.Any(removed.Contains))
                    throw new PatchException("target code is referenced by a branch from outside the range");
            }

            foreach (var handler in body.ExceptionHandlers)
            {
                if (removed.Contains(handler.TryStart) || removed.Contains(handler.TryEnd)
                    || removed.Contains(handler.HandlerStart) || removed.Contains(handler.HandlerEnd)
                    || removed.Contains(handler.FilterStart))
                {
                    throw new PatchException("target code lies on an exception handler boundary");
                }
            }
        }
    }
}
