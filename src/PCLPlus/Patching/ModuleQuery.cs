using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace PCLPlus.Patching
{
    /// <summary>
    /// Lookup helpers that locate types, methods and member references inside a module.
    /// </summary>
    /// <remarks>
    /// Patch points are located by name and IL shape instead of by token or offset,
    /// so minor launcher rebuilds keep working.
    /// </remarks>
    internal static class ModuleQuery
    {
        public static TypeDef FindType(ModuleDef module, string fullName)
        {
            return module.Find(fullName, isReflectionName: false);
        }

        public static TypeDef RequireType(ModuleDef module, string fullName)
        {
            return FindType(module, fullName)
                ?? throw new PatchException($"找不到类型 {fullName}");
        }

        public static MethodDef FindMethod(TypeDef type, string name, int parameterCount)
        {
            return type?.Methods.FirstOrDefault(m =>
                m.HasBody && m.Name == name && m.MethodSig.Params.Count == parameterCount);
        }

        public static MethodDef FindMethod(ModuleDef module, string typeFullName, string name, int parameterCount)
        {
            return FindMethod(FindType(module, typeFullName), name, parameterCount);
        }

        public static MethodDef RequireMethod(ModuleDef module, string typeFullName, string name, int parameterCount)
        {
            return FindMethod(module, typeFullName, name, parameterCount)
                ?? throw new PatchException($"找不到方法 {typeFullName}.{name}（{parameterCount} 个参数）");
        }

        /// <summary>Enumerates every method in the module that has an IL body.</summary>
        public static IEnumerable<MethodDef> MethodsWithBody(ModuleDef module)
        {
            return module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody);
        }

        /// <summary>
        /// Returns the first operand of type <typeparamref name="T"/> used anywhere in the module that matches <paramref name="match"/>.
        /// </summary>
        /// <remarks>Reusing an existing reference keeps the patched assembly bound to exactly the same dependency versions.</remarks>
        public static T RequireOperand<T>(ModuleDef module, Func<T, bool> match, string description) where T : class
        {
            foreach (var method in MethodsWithBody(module))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is T operand && match(operand))
                        return operand;
                }
            }
            throw new PatchException($"找不到对 {description} 的引用");
        }

        public static IMethod RequireMethodRef(ModuleDef module, string declaringTypeFullName, string name, int parameterCount)
        {
            return RequireOperand<IMethod>(module,
                m => IsMethod(m, declaringTypeFullName, name, parameterCount),
                $"{declaringTypeFullName}.{name}");
        }

        public static bool IsMethod(IMethod method, string declaringTypeFullName, string name, int parameterCount)
        {
            return method != null
                && method.Name == name
                && method.DeclaringType?.FullName == declaringTypeFullName
                && method.MethodSig?.Params.Count == parameterCount;
        }

        public static bool IsCallTo(Instruction instruction, string declaringTypeFullName, string name)
        {
            return (instruction.OpCode.Code == Code.Call || instruction.OpCode.Code == Code.Callvirt)
                && instruction.Operand is IMethod method
                && method.Name == name
                && method.DeclaringType?.FullName == declaringTypeFullName;
        }

        public static bool IsLdstr(Instruction instruction, Func<string, bool> match)
        {
            return instruction.OpCode.Code == Code.Ldstr
                && instruction.Operand is string text
                && match(text);
        }

        public static bool IsLdstr(Instruction instruction, string value)
        {
            return IsLdstr(instruction, text => text == value);
        }

        public static bool ContainsBlockMessage(MethodDef method)
        {
            return method?.Body?.Instructions.Any(i => IsLdstr(i, LauncherSignature.IsBlockMessage)) == true;
        }

        /// <summary>Returns whether any method body in the module loads a "download disabled" message.</summary>
        public static bool ContainsBlockMessage(ModuleDef module)
        {
            return MethodsWithBody(module).Any(ContainsBlockMessage);
        }

        public static int IndexOf(IList<Instruction> instructions, Func<Instruction, bool> match, int startIndex = 0)
        {
            for (int i = startIndex; i < instructions.Count; i++)
            {
                if (match(instructions[i]))
                    return i;
            }
            return -1;
        }

        public static int RequireIndex(IList<Instruction> instructions, Func<Instruction, bool> match, string description, int startIndex = 0)
        {
            int index = IndexOf(instructions, match, startIndex);
            if (index < 0)
                throw new PatchException($"找不到 {description}");
            return index;
        }
    }
}
