using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace PCLPlus.Patching.Patches
{
    /// <summary>
    /// Restores the "install modpack from file" entry point.
    /// </summary>
    /// <remarks>
    /// <c>ModModpack.ModpackInstall()</c> was reduced to a single hint. Its compiler-generated closure, which runs
    /// <c>ModpackInstall(file)</c> and reports failures, is still present; the patch rebuilds the method as:
    /// <code>
    /// var closure = new Closure();
    /// closure.File = Dialogs.SelectFile("选择整合包压缩文件", false, null, Filter).FirstOrDefault();
    /// if (string.IsNullOrEmpty(closure.File)) return;
    /// ModBase.RunInNewThread(closure.Lambda, "Modpack Install", ThreadPriority.Normal);
    /// </code>
    /// </remarks>
    internal sealed class ModpackPickerPatch : Patch
    {
        private const string ModpackType = "PCL.ModModpack";
        private const string MethodName = "ModpackInstall";
        private const string DialogsType = "MeloongCore.Wpf.Dialogs";
        private const string SelectFileMethod = "SelectFile";
        private const string ClosureFileField = "$VB$Local_File";

        private const string DialogTitle = "选择整合包压缩文件";
        private const string DialogFilter = "整合包文件(*.rar;*.zip;*.mrpack)|*.rar;*.zip;*.mrpack";
        private const string ThreadName = "Modpack Install";
        private const int ThreadPriorityNormal = 2;

        public override string Title => "手动安装整合包";

        public override string Description => "恢复“安装整合包”按钮的文件选择功能";

        protected override MethodDef FindTarget(ModuleDef module)
        {
            return ModuleQuery.FindMethod(module, ModpackType, MethodName, 0);
        }

        protected override PatchState GetState(ModuleDef module, MethodDef target)
        {
            if (ModuleQuery.ContainsBlockMessage(target))
                return PatchState.Pending;
            if (target.Body.Instructions.Any(i => ModuleQuery.IsCallTo(i, DialogsType, SelectFileMethod)))
                return PatchState.Applied;
            return PatchState.NotFound;
        }

        protected override void Apply(ModuleDef module, MethodDef target)
        {
            var modpackType = ModuleQuery.RequireType(module, ModpackType);
            var installFromFile = ModuleQuery.FindMethod(modpackType, MethodName, 3)
                ?? throw new PatchException("找不到整合包安装方法");

            var closure = FindClosure(modpackType, installFromFile);
            var closureCtor = closure.FindDefaultConstructor()
                ?? throw new PatchException("整合包闭包缺少构造函数");
            var fileField = closure.FindField(ClosureFileField);
            var lambda = closure.Methods.First(m => CallsMethod(m, installFromFile));

            var dialog = FindSelectFileCallSite(module);
            var firstOrDefault = ModuleQuery.RequireOperand<MethodSpec>(module,
                m => m.Name == "FirstOrDefault"
                    && m.GenericInstMethodSig?.GenericArguments.Count == 1
                    && m.GenericInstMethodSig.GenericArguments[0].ElementType == ElementType.String,
                "Enumerable.FirstOrDefault<string>");
            var isNullOrEmpty = ModuleQuery.RequireMethodRef(module, "System.String", "IsNullOrEmpty", 1);
            var actionCtor = ModuleQuery.RequireMethodRef(module, "System.Action", ".ctor", 2);
            var runInNewThread = ModuleQuery.RequireMethod(module, "PCL.ModBase", "RunInNewThread", 3);

            var body = target.Body;
            var closureLocal = new Local(closure.ToTypeSig());
            body.Variables.Clear();
            body.Variables.Add(closureLocal);
            body.InitLocals = true;

            var ret = Instruction.Create(OpCodes.Ret);
            IlEditor.ReplaceBody(body, new[]
            {
                Instruction.Create(OpCodes.Newobj, closureCtor),
                Instruction.Create(OpCodes.Stloc, closureLocal),

                Instruction.Create(OpCodes.Ldloc, closureLocal),
                Instruction.Create(OpCodes.Ldstr, DialogTitle),
                Instruction.Create(OpCodes.Ldc_I4_0),
                Instruction.Create(OpCodes.Ldnull),
                Instruction.Create(OpCodes.Ldstr, DialogFilter),
                Instruction.Create(OpCodes.Call, dialog.FilterConversion),
                Instruction.Create(OpCodes.Newobj, dialog.NullableConstructor),
                Instruction.Create(OpCodes.Call, dialog.SelectFile),
                Instruction.Create(OpCodes.Call, firstOrDefault),
                Instruction.Create(OpCodes.Stfld, fileField),

                Instruction.Create(OpCodes.Ldloc, closureLocal),
                Instruction.Create(OpCodes.Ldfld, fileField),
                Instruction.Create(OpCodes.Call, isNullOrEmpty),
                Instruction.Create(OpCodes.Brtrue, ret),

                Instruction.Create(OpCodes.Ldloc, closureLocal),
                Instruction.Create(OpCodes.Ldftn, lambda),
                Instruction.Create(OpCodes.Newobj, actionCtor),
                Instruction.Create(OpCodes.Ldstr, ThreadName),
                Instruction.Create(OpCodes.Ldc_I4, ThreadPriorityNormal),
                Instruction.Create(OpCodes.Call, runInNewThread),
                Instruction.Create(OpCodes.Pop),

                ret,
            });
        }

        /// <summary>Finds the nested closure type whose lambda installs the selected file.</summary>
        private static TypeDef FindClosure(TypeDef modpackType, MethodDef installFromFile)
        {
            return modpackType.NestedTypes.FirstOrDefault(t =>
                       t.FindField(ClosureFileField)?.FieldType.ElementType == ElementType.String
                       && t.Methods.Any(m => CallsMethod(m, installFromFile)))
                ?? throw new PatchException("找不到整合包安装的闭包");
        }

        private static bool CallsMethod(MethodDef method, MethodDef callee)
        {
            return method.HasBody && method.Body.Instructions.Any(i =>
                i.OpCode.Code == Code.Call && i.Operand is IMethod m
                && new SigComparer().Equals(m, callee));
        }

        /// <summary>
        /// Finds an existing <c>SelectFile(title, multiselect, folder, "filter")</c> call and returns the members it uses
        /// to turn a filter string into the dialog's filter argument.
        /// </summary>
        private static SelectFileCall FindSelectFileCallSite(ModuleDef module)
        {
            foreach (var method in ModuleQuery.MethodsWithBody(module))
            {
                var instructions = method.Body.Instructions;
                for (int i = 3; i < instructions.Count; i++)
                {
                    if (!ModuleQuery.IsCallTo(instructions[i], DialogsType, SelectFileMethod))
                        continue;

                    var nullableCtor = instructions[i - 1];
                    var conversion = instructions[i - 2];
                    var filter = instructions[i - 3];
                    if (nullableCtor.OpCode.Code == Code.Newobj
                        && conversion.OpCode.Code == Code.Call
                        && conversion.Operand is IMethod conversionMethod && conversionMethod.Name == "op_Implicit"
                        && filter.OpCode.Code == Code.Ldstr)
                    {
                        return new SelectFileCall(
                            (IMethod)instructions[i].Operand,
                            conversionMethod,
                            (IMethod)nullableCtor.Operand);
                    }
                }
            }
            throw new PatchException("找不到文件选择对话框的调用方式");
        }

        private sealed class SelectFileCall
        {
            public SelectFileCall(IMethod selectFile, IMethod filterConversion, IMethod nullableConstructor)
            {
                SelectFile = selectFile;
                FilterConversion = filterConversion;
                NullableConstructor = nullableConstructor;
            }

            public IMethod SelectFile { get; }

            public IMethod FilterConversion { get; }

            public IMethod NullableConstructor { get; }
        }
    }
}
