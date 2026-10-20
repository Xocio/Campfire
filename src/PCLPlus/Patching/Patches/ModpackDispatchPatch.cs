using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace PCLPlus.Patching.Patches
{
    /// <summary>
    /// Restores modpack installation after the modpack format has been detected.
    /// </summary>
    /// <remarks>
    /// <c>ModModpack.ModpackInstall(file, instanceName, logo)</c> still opens the archive and detects its format,
    /// but then throws instead of dispatching to the format-specific installer. The patch replaces that throw with:
    /// <code>
    /// switch (kind)
    /// {
    ///     case 0: result = InstallPackCurseForge(file, archive, baseFolder, instanceName, logo); break;
    ///     case 1: result = InstallPackHMCL(file, archive, baseFolder); break;
    ///     case 2: result = InstallPackMMC(file, archive, baseFolder); break;
    ///     case 3: result = InstallPackMCBBS(file, archive, baseFolder, instanceName); break;
    ///     case 4: result = InstallPackModrinth(file, archive, baseFolder, instanceName, logo); break;
    ///     case 9: result = InstallPackLauncherPack(file, archive, baseFolder); break;
    ///     default: result = InstallPackCompress(file, archive); break;
    /// }
    /// // after the enclosing try/finally:
    /// return result;
    /// </code>
    /// </remarks>
    internal sealed class ModpackDispatchPatch : Patch
    {
        private const string ModpackType = "PCL.ModModpack";
        private const string MethodName = "ModpackInstall";
        private const string LoggerType = "MeloongCore.Logger";
        private const string LogSource = "PCL++";

        /// <summary>Format codes produced by the detection code, mapped to their installer.</summary>
        private static readonly InstallerCase[] Installers =
        {
            new InstallerCase(0, "InstallPackCurseForge", "CurseForge"),
            new InstallerCase(1, "InstallPackHMCL", "HMCL"),
            new InstallerCase(2, "InstallPackMMC", "MMC"),
            new InstallerCase(3, "InstallPackMCBBS", "MCBBS"),
            new InstallerCase(4, "InstallPackModrinth", "Modrinth"),
            new InstallerCase(9, "InstallPackLauncherPack", "带启动器的压缩包"),
        };

        private static readonly InstallerCase FallbackInstaller =
            new InstallerCase(-1, "InstallPackCompress", "未能识别，假定为压缩包");

        protected override string TextKey => "ModpackDispatch";

        protected override MethodDef FindTarget(ModuleDef module)
        {
            return ModuleQuery.FindMethod(module, ModpackType, MethodName, 3);
        }

        protected override PatchState GetState(ModuleDef module, MethodDef target)
        {
            if (ModuleQuery.ContainsBlockMessage(target))
                return PatchState.Pending;
            if (target.Body.Instructions.Any(i => ModuleQuery.IsCallTo(i, ModpackType, FallbackInstaller.MethodName)))
                return PatchState.Applied;
            return PatchState.NotFound;
        }

        protected override void Apply(ModuleDef module, MethodDef target)
        {
            var body = target.Body;
            var instructions = body.Instructions;
            var modpackType = ModuleQuery.RequireType(module, ModpackType);

            // Locate "throw new Exception(<block message>)".
            int start = ModuleQuery.RequireIndex(instructions,
                i => ModuleQuery.IsLdstr(i, LauncherSignature.IsBlockMessage), "disabled-feature message");
            int end = start + 2;
            if (end >= instructions.Count
                || instructions[start + 1].OpCode.Code != Code.Newobj
                || instructions[end].OpCode.Code != Code.Throw)
            {
                throw new PatchException("unexpected layout around the disabled modpack dispatch");
            }

            var locals = FindLocals(target);
            var logInfo = instructions
                .Where(i => ModuleQuery.IsCallTo(i, LoggerType, "Info"))
                .Select(i => (IMethod)i.Operand)
                .FirstOrDefault(m => m.MethodSig.Params.Count == 3)
                ?? throw new PatchException("logger method not found");

            // The throw sits inside a try/finally that disposes the archive; the result is returned after it.
            var finallyHandler = body.ExceptionHandlers.FirstOrDefault(h =>
                    h.HandlerType == ExceptionHandlerType.Finally
                    && instructions.IndexOf(h.TryStart) <= start
                    && instructions.IndexOf(h.TryEnd) > end)
                ?? throw new PatchException("finally block that disposes the archive not found");
            if (finallyHandler.HandlerEnd != null)
                throw new PatchException("unexpected code after the finally block");

            var returnResult = Instruction.Create(OpCodes.Ldloc, locals.Result);
            var fallbackLabel = Instruction.Create(OpCodes.Nop);

            var cases = new Dictionary<int, Instruction>();
            var caseBodies = new List<Instruction>();
            foreach (var installer in Installers)
            {
                var label = Instruction.Create(OpCodes.Nop);
                cases[installer.Kind] = label;
                caseBodies.Add(label);
                caseBodies.AddRange(EmitInstall(modpackType, installer, locals, logInfo, returnResult));
            }
            caseBodies.Add(fallbackLabel);
            caseBodies.AddRange(EmitInstall(modpackType, FallbackInstaller, locals, logInfo, returnResult));

            int maxKind = cases.Keys.Max();
            var jumpTable = Enumerable.Range(0, maxKind + 1)
                .Select(kind => cases.TryGetValue(kind, out var label) ? label : fallbackLabel)
                .ToArray();

            var replacement = new List<Instruction>
            {
                Instruction.Create(OpCodes.Ldloc, locals.Kind),
                Instruction.Create(OpCodes.Switch, jumpTable),
                Instruction.Create(OpCodes.Br, fallbackLabel),
            };
            replacement.AddRange(caseBodies);
            IlEditor.ReplaceRange(body, start, end, replacement);

            instructions.Add(returnResult);
            instructions.Add(Instruction.Create(OpCodes.Ret));
            finallyHandler.HandlerEnd = returnResult;
        }

        private static IEnumerable<Instruction> EmitInstall(TypeDef modpackType, InstallerCase installer,
            MethodLocals locals, IMethod logInfo, Instruction exit)
        {
            var method = modpackType.Methods.FirstOrDefault(m => m.Name == installer.MethodName)
                ?? throw new PatchException($"installer {installer.MethodName} not found");

            // Installers share a common leading parameter list: (file, archive, baseFolder, instanceName, logo).
            var arguments = new Instruction[]
            {
                Instruction.Create(OpCodes.Ldarg, locals.FileParameter),
                Instruction.Create(OpCodes.Ldloc, locals.Archive),
                Instruction.Create(OpCodes.Ldloc, locals.BaseFolder),
                Instruction.Create(OpCodes.Ldarg, locals.InstanceNameParameter),
                Instruction.Create(OpCodes.Ldarg, locals.LogoParameter),
            };
            int parameterCount = method.MethodSig.Params.Count;
            if (parameterCount < 2 || parameterCount > arguments.Length)
                throw new PatchException($"unexpected parameters of installer {installer.MethodName}");

            yield return Instruction.Create(OpCodes.Ldstr, "[ModPack] 整合包种类：" + installer.DisplayName);
            yield return Instruction.Create(OpCodes.Ldc_I4_0);
            yield return Instruction.Create(OpCodes.Ldstr, LogSource);
            yield return Instruction.Create(OpCodes.Call, logInfo);

            for (int i = 0; i < parameterCount; i++)
                yield return arguments[i];
            yield return Instruction.Create(OpCodes.Call, method);
            yield return Instruction.Create(OpCodes.Stloc, locals.Result);
            yield return Instruction.Create(OpCodes.Leave, exit);
        }

        private static MethodLocals FindLocals(MethodDef method)
        {
            var body = method.Body;
            var instructions = body.Instructions;
            var variables = body.Variables;

            var result = variables.FirstOrDefault(v =>
                v.Type.FullName.StartsWith("PCL.ModLoader/LoaderCombo`1"));
            var archive = variables.FirstOrDefault(v =>
                v.Type.FullName == "System.IO.Compression.ZipArchive");

            // kind = -1 is the first assignment of the format code.
            Local kind = null;
            // baseFolder = "" is the first assignment of the archive base folder.
            Local baseFolder = null;
            for (int i = 0; i + 1 < instructions.Count; i++)
            {
                var next = instructions[i + 1];
                if (!next.IsStloc())
                    continue;
                if (kind == null && instructions[i].IsLdcI4() && instructions[i].GetLdcI4Value() == -1)
                    kind = next.GetLocal(variables);
                if (baseFolder == null && ModuleQuery.IsLdstr(instructions[i], string.Empty))
                    baseFolder = next.GetLocal(variables);
            }

            if (result == null || archive == null || kind == null || baseFolder == null)
                throw new PatchException("unexpected local variables in the modpack installer");

            var parameters = method.Parameters;
            return new MethodLocals(result, archive, kind, baseFolder,
                file: parameters[0], instanceName: parameters[1], logo: parameters[2]);
        }

        private sealed class InstallerCase
        {
            public InstallerCase(int kind, string methodName, string displayName)
            {
                Kind = kind;
                MethodName = methodName;
                DisplayName = displayName;
            }

            public int Kind { get; }

            public string MethodName { get; }

            public string DisplayName { get; }
        }

        private sealed class MethodLocals
        {
            public MethodLocals(Local result, Local archive, Local kind, Local baseFolder,
                Parameter file, Parameter instanceName, Parameter logo)
            {
                Result = result;
                Archive = archive;
                Kind = kind;
                BaseFolder = baseFolder;
                FileParameter = file;
                InstanceNameParameter = instanceName;
                LogoParameter = logo;
            }

            public Local Result { get; }

            public Local Archive { get; }

            public Local Kind { get; }

            public Local BaseFolder { get; }

            public Parameter FileParameter { get; }

            public Parameter InstanceNameParameter { get; }

            public Parameter LogoParameter { get; }
        }
    }
}
