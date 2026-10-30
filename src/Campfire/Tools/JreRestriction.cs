using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Campfire.Patching;

namespace Campfire.Tools
{
    /// <summary>
    /// The rule in PCL Community Edition that switches off every JRE, a Java without <c>javac.exe</c>.
    /// </summary>
    /// <remarks>
    /// <c>JavaManager._ShouldEnableByDefault</c> decides whether a discovered Java is switched on. It turns off
    /// every JRE newer than Java 8, and a Java 8 JRE unless <c>lib\rt.jar</c> exists. The edits keep the
    /// architecture check and remove both JRE conditions: the <c>rt.jar</c> probe becomes <c>true</c> and the
    /// version gate becomes nops. No instruction changes its size.
    /// </remarks>
    internal static class JreRestriction
    {
        /// <summary>Assembly that holds the Java management of the community edition.</summary>
        public const string CoreAssemblyName = "PCL.Core";

        private const string ManagerType = "PCL.Core.Minecraft.JavaManager";
        private const string MethodName = "_ShouldEnableByDefault";
        private const string InstallationType = "PCL.Core.Minecraft.Java.JavaInstallation";
        private const string RuntimeArchiveName = "rt.jar";
        private const int LegacyMajorVersion = 8;

        /// <summary>One instruction rewritten by the tool.</summary>
        public sealed class Edit
        {
            public Edit(Instruction instruction, bool loadsTrue)
            {
                Instruction = instruction;
                LoadsTrue = loadsTrue;
            }

            public Instruction Instruction { get; }

            /// <summary>Whether the instruction becomes <c>ldc.i4.1</c>; otherwise it becomes <c>nop</c>.</summary>
            public bool LoadsTrue { get; }

            /// <summary>Offset of the instruction from the first IL byte of the method.</summary>
            public int Offset => (int)Instruction.Offset;

            /// <summary>The bytes written over the instruction; they always have the original size.</summary>
            public byte[] Bytes
            {
                get
                {
                    var bytes = new byte[Instruction.GetSize()];
                    if (LoadsTrue)
                        bytes[0] = (byte)OpCodes.Ldc_I4_1.Value;
                    return bytes;
                }
            }
        }

        /// <summary>Whether the module has the Java manager at all; older releases do not.</summary>
        public static bool HasJavaManager(ModuleDef module)
        {
            return ModuleQuery.FindType(module, ManagerType) != null;
        }

        /// <summary>Locates the method that decides whether a discovered Java is switched on, or returns <c>null</c>.</summary>
        public static MethodDef FindTarget(ModuleDef module)
        {
            return ModuleQuery.FindMethod(module, ManagerType, MethodName, 1);
        }

        /// <summary>Whether the method still contains the JRE conditions or already lost them.</summary>
        public static RestrictionState GetState(MethodDef target)
        {
            var instructions = target.Body.Instructions;
            if (!instructions.Any(i => ModuleQuery.IsCallTo(i, InstallationType, "get_IsJre")))
                return RestrictionState.Unknown;

            if (FindEdits(target) != null)
                return RestrictionState.Present;

            bool restricted = instructions.Any(i => ModuleQuery.IsLdstr(i, RuntimeArchiveName)
                                                    || ModuleQuery.IsCallTo(i, InstallationType, "get_MajorVersion"));
            return restricted ? RestrictionState.Unknown : RestrictionState.Removed;
        }

        /// <summary>
        /// Lists the instructions to rewrite, or returns <c>null</c> when the method does not have the expected shape.
        /// </summary>
        public static IReadOnlyList<Edit> FindEdits(MethodDef target)
        {
            var instructions = target.Body.Instructions;

            // ldloc <folder>; ldstr "rt.jar"; call Path.Combine; call File.Exists
            int archive = ModuleQuery.IndexOf(instructions, i => ModuleQuery.IsLdstr(i, RuntimeArchiveName));
            if (archive < 1 || archive + 2 >= instructions.Count)
                return null;
            if (!instructions[archive - 1].IsLdloc()
                || !ModuleQuery.IsCallTo(instructions[archive + 1], "System.IO.Path", "Combine")
                || !ModuleQuery.IsCallTo(instructions[archive + 2], "System.IO.File", "Exists"))
            {
                return null;
            }

            // ldarg.0; callvirt get_MajorVersion; ldc.i4 8; bgt
            int version = ModuleQuery.IndexOf(instructions, i => ModuleQuery.IsCallTo(i, InstallationType, "get_MajorVersion"));
            if (version < 1 || version + 2 >= instructions.Count)
                return null;
            var limit = instructions[version + 1];
            var gate = instructions[version + 2];
            if (!instructions[version - 1].IsLdarg()
                || !limit.IsLdcI4() || limit.GetLdcI4Value() != LegacyMajorVersion
                || (gate.OpCode.Code != Code.Bgt && gate.OpCode.Code != Code.Bgt_S))
            {
                return null;
            }

            var edits = new List<Edit> { new Edit(instructions[archive - 1], loadsTrue: true) };
            edits.AddRange(Enumerable.Range(archive, 3).Select(i => new Edit(instructions[i], loadsTrue: false)));
            edits.AddRange(Enumerable.Range(version - 1, 4).Select(i => new Edit(instructions[i], loadsTrue: false)));
            return edits;
        }
    }

    /// <summary>What the Java default check of a community launcher looks like.</summary>
    internal enum RestrictionState
    {
        /// <summary>The method has a layout that is not recognized.</summary>
        Unknown,

        /// <summary>The method still switches off JREs.</summary>
        Present,

        /// <summary>The method no longer switches off JREs.</summary>
        Removed,
    }
}
