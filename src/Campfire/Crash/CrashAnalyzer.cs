using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using Campfire.Mods;
using Campfire.Worlds;

namespace Campfire.Crash
{
    internal enum FindingLevel
    {
        /// <summary>A known reason for the game to stop.</summary>
        Cause,

        /// <summary>Something worth checking that does not stop the game by itself.</summary>
        Hint,
    }

    /// <summary>One thing the files show, with the lines that show it.</summary>
    internal sealed class CrashFinding
    {
        public CrashFinding(string key, FindingLevel level, object[] args, IReadOnlyList<string> evidence, string source)
        {
            Key = key;
            Level = level;
            Args = args ?? new object[0];
            Evidence = evidence ?? Array.Empty<string>();
            Source = source;
        }

        /// <summary>The text keys are "Crash.&lt;Key&gt;.Title" and "Crash.&lt;Key&gt;.Advice".</summary>
        public string Key { get; }

        public FindingLevel Level { get; }

        /// <summary>Values for the placeholders of the title and the advice.</summary>
        public object[] Args { get; }

        /// <summary>The lines of the file this finding comes from, as written there.</summary>
        public IReadOnlyList<string> Evidence { get; }

        /// <summary>The file name the lines come from.</summary>
        public string Source { get; }

        /// <summary>Jar files in the mods folder this finding names.</summary>
        public List<string> Mods { get; } = new List<string>();
    }

    /// <summary>What the newest crash report says about the game, where it tells.</summary>
    internal sealed class CrashSummary
    {
        public string Description { get; set; }

        public string GameVersion { get; set; }

        public string JavaVersion { get; set; }

        /// <summary>The -Xmx value the game was started with.</summary>
        public string MaxMemory { get; set; }

        /// <summary>The error the crash report starts with.</summary>
        public string Headline { get; set; }

        /// <summary>The last "Caused by:" line of that error, or <c>null</c>.</summary>
        public string RootCause { get; set; }
    }

    internal sealed class CrashAnalysis
    {
        public CrashAnalysis(IReadOnlyList<CrashFile> files, IReadOnlyList<CrashFinding> findings, CrashSummary summary,
            IReadOnlyList<string> stackMods, bool lastRunClean)
        {
            Files = files;
            Findings = findings;
            Summary = summary;
            StackMods = stackMods;
            LastRunClean = lastRunClean;
        }

        public IReadOnlyList<CrashFile> Files { get; }

        /// <summary>Causes first, then hints.</summary>
        public IReadOnlyList<CrashFinding> Findings { get; }

        public CrashSummary Summary { get; }

        /// <summary>Jar files in the mods folder whose code appears in the crash report's stack traces, first seen first.</summary>
        public IReadOnlyList<string> StackMods { get; }

        /// <summary>The last game log ends with a normal exit and reports no crash.</summary>
        public bool LastRunClean { get; }
    }

    /// <summary>
    /// Reads crash reports, game logs and Java error reports and matches them against messages that Java, the game
    /// and the mod loaders print for known problems. Every finding keeps the lines it was found in.
    /// </summary>
    internal static class CrashAnalyzer
    {
        private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Multiline;
        private const int MaxEvidence = 6;
        private const int MaxLine = 240;

        // Java memory and versions.
        private static readonly Regex OutOfMemory = new Regex(@"java\.lang\.OutOfMemoryError(?::[ \t]*(?<detail>[^\r\n]+))?", Options);
        private static readonly Regex NativeMemory = new Regex(
            @"There is insufficient memory for the Java Runtime Environment to continue|Native memory allocation \((?:malloc|mmap)\) failed", Options);
        private static readonly Regex HeapReserve = new Regex(@"Could not reserve enough space for (?:\d+KB )?object heap|Invalid maximum heap size", Options);
        private static readonly Regex ClassVersion = new Regex(
            @"class file version (?<need>\d+)(?:\.\d+)?\), this version of the Java Runtime only recognizes class file versions up to (?<have>\d+)", Options);
        private static readonly Regex OldClassVersion = new Regex(@"Unsupported major\.minor version (?<need>\d+)", Options);
        private static readonly Regex LaunchWrapperJava = new Regex(
            @"jdk\.internal\.loader\.ClassLoaders\$AppClassLoader cannot be cast to (?:class )?java\.net\.URLClassLoader", Options);
        private static readonly Regex ModuleAccess = new Regex(@"does not ""opens [\w.]+"" to unnamed module", Options);
        private static readonly Regex MainClass = new Regex(@"Could not find or load main class (?<class>\S+)", Options);

        // Fabric and Quilt mod resolution.
        private static readonly Regex FabricHeader = new Regex(
            @"Mod resolution encountered an incompatible mod set!|Some of your mods are incompatible with the game or each other!", Options);
        private static readonly Regex FabricMissing = new Regex(
            @"Mod '(?<name>[^'\r\n]*)' \((?<id>[^)\s]+)\) (?<version>\S+) requires (?<range>[^\r\n]+?) of (?<dep>[^\r\n]+?), which is missing!", Options);
        private static readonly Regex FabricWrong = new Regex(
            @"Mod '(?<name>[^'\r\n]*)' \((?<id>[^)\s]+)\) (?<version>\S+) requires (?<range>[^\r\n]+?) of (?<dep>[^\r\n]+?), but only the wrong version is present: (?<present>[^\r\n]+?)!", Options);
        private static readonly Regex FabricBreaks = new Regex(
            @"Mod '(?<name>[^'\r\n]*)' \((?<id>[^)\s]+)\) (?<version>\S+) is incompatible with (?<range>[^\r\n]+?) of (?<dep>[^\r\n]+?), but a matching version is present: (?<present>[^\r\n]+?)!", Options);
        private static readonly Regex FabricOldMissing = new Regex(
            @"Could not find required mod: (?<id>\S+) requires \{(?<dep>[^\s@}]+)\s*@\s*\[(?<range>[^\]]*)\]\}", Options);
        private static readonly Regex FabricDependencyName = new Regex(@"^(?:mod )?'(?<name>[^']*)' \((?<id>[^)]+)\)$", Options);
        private static readonly Regex FabricSolution = new Regex(@"^\s*-?\s*(?:Install|Remove|Replace) [^\r\n]+$", Options);

        // Forge and NeoForge dependencies and loading.
        private static readonly Regex ForgeDependency = new Regex(
            @"Mod ID: '(?<id>[^']+)', Requested by: '(?<by>[^']+)', Expected range: '(?<range>[^']*)', Actual version: '(?<actual>[^']*)'", Options);
        private static readonly Regex ModLoadingDescription = new Regex(
            @"^Description: (?:Mod loading (?:error has|failures have) occurred|There was a severe problem during mod loading)", Options);
        private static readonly Regex ModSection = new Regex(@"^-- MOD (?<id>[^\s-][^\r\n]*?) --\s*$", Options);
        private static readonly Regex ModFile = new Regex(@"^\s*Mod File: (?<file>[^\r\n]+)$", Options);
        private static readonly Regex FailureMessage = new Regex(@"^\s*Failure message: (?<message>[^\r\n]+)$", Options);

        // Mixin.
        private static readonly Regex MixinApplyMod = new Regex(
            @"Mixin apply for mod (?<mod>[\w\-.]+) failed (?<config>[\w\-./]+\.json):(?<mixin>[\w.$]+) from mod [\w\-.]+ -> (?<target>[\w.$/]+)", Options);
        private static readonly Regex MixinApply = new Regex(@"Mixin apply failed (?<config>[\w\-./]+\.json):(?<mixin>[\w.$]+) -> (?<target>[\w.$/]+)", Options);
        private static readonly Regex MixinPhase = new Regex(
            @"Mixin \[(?<config>[\w\-./]+\.json):(?<mixin>[\w.$]+)\] from phase \[\w+\] in config \[[\w\-./]+\.json\] FAILED during \w+", Options);
        private static readonly Regex MixinInjection = new Regex(
            @"Critical injection failure: [^\r\n]*? in (?<config>[\w\-./]+\.json):(?<mixin>[\w.$]+) from mod (?:(?<mod>[\w\-.]+)|\(unknown\)) failed injection check", Options);
        private static readonly Regex MixinDetail = new Regex(@"(?:Critical injection failure|InvalidInjectionException|InvalidMixinException): [^\r\n]+", Options);

        // Graphics and native code.
        private static readonly Regex PixelFormat = new Regex(@"Pixel format not accepted", Options);
        private static readonly Regex GlfwNoDriver = new Regex(@"GLFW error 65542[^\r\n]*", Options);
        private static readonly Regex GlfwVersion = new Regex(@"GLFW error 65543[^\r\n]*", Options);
        private static readonly Regex ProblematicFrame = new Regex(@"^#\s+(?<kind>[CjJVv])\s+\[(?<library>[^\]+\r\n]+)(?:\+0x[0-9a-fA-F]+)?\][^\r\n]*$", Options);
        private static readonly Regex GraphicsDriver = new Regex(
            @"^(?:atio6axx|atioglxx|atig6pxx|nvoglv32|nvoglv64|ig\w*icd(?:32|64)|igxelpicd(?:32|64))\.dll$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex NativeCrash = new Regex(@"EXCEPTION_ACCESS_VIOLATION|SIGSEGV", Options);
        private static readonly Regex Natives = new Regex(@"Failed to locate library: (?<library>[\w.\-]+)|no (?<library>lwjgl\w*) in java\.library\.path", Options);
        private static readonly Regex JavaVm32 = new Regex(@"^\s*Java VM Version: [^\r\n]*\bClient VM\b|^# Java VM: [^\r\n]*windows-x86\s", Options);

        // Damaged files.
        private static readonly Regex ZipError = new Regex(
            @"java\.util\.zip\.ZipException: (?:zip END header not found|invalid LOC header \(bad signature\)|invalid CEN header \([^)\r\n]*\)|invalid entry compressed size[^\r\n]*|error in opening zip file|zip file is empty)", Options);
        private static readonly Regex JarPath = new Regex(@"(?<path>[A-Za-z]:[\\/][^\r\n'""\[\]<>|?*]+?\.jar)", Options);

        // Crash report sections.
        private static readonly Regex Description = new Regex(@"^Description: (?<text>[^\r\n]+)$", Options);
        private static readonly Regex TickingEntity = new Regex(@"^Description: Ticking entity\s*$", Options);
        private static readonly Regex TickingBlock = new Regex(@"^Description: Ticking (?:block entity|tile entity)\s*$", Options);
        private static readonly Regex EntityType = new Regex(@"^\s*Entity Type: (?<value>[^\r\n]+)$", Options);
        private static readonly Regex EntityLocation = new Regex(@"^\s*Entity's Exact location: (?<value>[^\r\n]+)$", Options);
        private static readonly Regex BlockName = new Regex(@"^\s*Name: (?<value>[^\r\n]+)$", Options);
        private static readonly Regex BlockLocation = new Regex(@"^\s*Block location: (?<value>[^\r\n]+)$", Options);
        private static readonly Regex DebugCrash = new Regex(@"^Description: Manually triggered debug crash", Options);
        private static readonly Regex GameVersionLine = new Regex(@"^\s*Minecraft Version: (?<value>[^\r\n]+)$", Options);
        private static readonly Regex JavaVersionLine = new Regex(@"^\s*Java Version: (?<value>[^\r\n]+)$", Options);
        private static readonly Regex MaxMemoryFlag = new Regex(@"^\s*JVM Flags: [^\r\n]*?-Xmx(?<value>\S+)", Options);
        private static readonly Regex ExceptionLine = new Regex(@"^(?<text>[\w.$]+(?:Exception|Error|Throwable)(?::[^\r\n]*)?)$", Options);
        private static readonly Regex SuspectedMods = new Regex(@"^\s*Suspected Mods?: (?<value>[^\r\n]+)$", Options);

        // Errors that only count in a crash report.
        private static readonly Regex ClassInit = new Regex(@"java\.lang\.NoClassDefFoundError: Could not initialize class (?<class>[\w/.$]+)", Options);
        private static readonly Regex MissingClass = new Regex(
            @"java\.lang\.(?:NoClassDefFoundError|ClassNotFoundException): (?!Could not initialize class )(?<class>[\w/.$]+)", Options);
        private static readonly Regex CausedBy = new Regex(@"^Caused by: (?<text>[^\r\n]+)$", Options);
        private static readonly Regex Mismatch = new Regex(
            @"java\.lang\.(?<kind>NoSuchMethodError|NoSuchFieldError|AbstractMethodError|IncompatibleClassChangeError)(?::[ \t]*(?<what>[^\r\n]+))?", Options);
        private static readonly Regex StackOverflow = new Regex(@"java\.lang\.StackOverflowError", Options);

        // Game log endings.
        private static readonly Regex CleanExit = new Regex(@"\]: Stopping!\s*$", Options);
        private static readonly Regex GameCrashed = new Regex(@"Game crashed! Crash report saved to:", Options);

        // Stack traces.
        private static readonly Regex StackFrame = new Regex(@"^\s*at (?:[^\s/(]*/)*(?<class>[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)+)\.[\w$<>]+\(", Options);
        private static readonly Regex StackJar = new Regex(@"(?<jar>[\w.+\-]+\.jar)(?:%23\d+)?!/", Options);

        /// <summary>A method a mixin added to a game class: its name carries the mod id, as in "handler$dhh000$modmenu$onInit".</summary>
        private static readonly Regex MixinMethod = new Regex(@"^\s*at [^\r\n(]*?\$[a-z]{3}\d{3}\$(?<mod>[a-z][a-z0-9_\-]*)\$", Options);

        /// <summary>Package prefixes of Java, the game, the loaders and common libraries; their frames name no mod.</summary>
        private static readonly string[] SystemPackages =
        {
            "java.", "javax.", "jdk.", "sun.", "com.sun.", "net.minecraft.", "com.mojang.", "org.lwjgl.", "org.spongepowered.",
            "net.fabricmc.", "org.quiltmc.", "net.minecraftforge.", "net.neoforged.", "cpw.mods.", "io.netty.", "com.google.",
            "org.apache.", "org.slf4j.", "it.unimi.", "oshi.", "org.objectweb.", "kotlin.", "scala.", "com.llamalad7.mixinextras.",
            "org.jetbrains.", "joptsimple.", "com.electronwill.", "net.jodah.", "org.openjdk.",
        };

        /// <param name="files">The files to read.</param>
        /// <param name="version">The version the files belong to, or <c>null</c> for a file the user picked.</param>
        public static CrashAnalysis Analyze(IReadOnlyList<CrashFile> files, InstalledVersion version)
        {
            var findings = new List<CrashFinding>();
            var mods = version == null ? null : ModIndex.Load(version.ModsDirectory);
            foreach (var file in files)
                Check(file, findings, mods);

            if (version != null)
            {
                string gameDirectory = Path.GetDirectoryName(version.SavesDirectory) ?? "";
                if (gameDirectory.Contains("!"))
                    findings.Add(new CrashFinding("PathBang", FindingLevel.Hint, new object[] { gameDirectory }, null, null));
            }

            var report = files.FirstOrDefault(f => f.Kind == CrashFileKind.CrashReport || f.Kind == CrashFileKind.Other);
            var summary = report == null ? null : Summarize(report.Text);
            var stackMods = report == null || mods == null ? (IReadOnlyList<string>)Array.Empty<string>() : mods.FindInStack(report.Text);

            var log = files.FirstOrDefault(f => f.Kind == CrashFileKind.GameLog);
            bool clean = log != null && !GameCrashed.IsMatch(log.Text) && EndsCleanly(log.Text);

            // The same finding from two files is kept once, from the first.
            var unique = new List<CrashFinding>();
            foreach (var finding in findings)
            {
                if (!unique.Any(u => u.Key == finding.Key && u.Args.SequenceEqual(finding.Args)))
                    unique.Add(finding);
            }
            return new CrashAnalysis(files, unique.OrderBy(f => f.Level).ToList(), summary, stackMods, clean);
        }

        private static void Check(CrashFile file, List<CrashFinding> into, ModIndex mods)
        {
            string text = file.Text ?? "";
            string source = file.Name;
            bool report = file.Kind == CrashFileKind.CrashReport || file.Kind == CrashFileKind.Other;
            bool jvm = file.Kind == CrashFileKind.JvmReport || file.Kind == CrashFileKind.Other;

            void Add(string key, FindingLevel level, Match match, params object[] args)
            {
                into.Add(new CrashFinding(key, level, args, new[] { LineOf(text, match.Index) }, source));
            }

            // Memory.
            var oom = OutOfMemory.Match(text);
            if (oom.Success)
            {
                string detail = oom.Groups["detail"].Success ? oom.Groups["detail"].Value.Trim() : "";
                Add(detail.IndexOf("native thread", StringComparison.OrdinalIgnoreCase) >= 0 ? "NativeThread" : "OutOfMemory",
                    FindingLevel.Cause, oom, detail);
            }
            var native = NativeMemory.Match(text);
            if (native.Success)
                Add("NativeMemory", FindingLevel.Cause, native);
            var heap = HeapReserve.Match(text);
            if (heap.Success)
                Add("HeapReserve", FindingLevel.Cause, heap);

            // Java versions.
            var version = ClassVersion.Match(text);
            if (version.Success && int.TryParse(version.Groups["need"].Value, out int need) && int.TryParse(version.Groups["have"].Value, out int have)
                && need > 44 && have > 44)
            {
                Add("JavaTooOld", FindingLevel.Cause, version, JavaName(need), JavaName(have));
            }
            else
            {
                var old = OldClassVersion.Match(text);
                if (old.Success && int.TryParse(old.Groups["need"].Value, out int oldNeed) && oldNeed > 44)
                    Add("JavaTooOldUnknown", FindingLevel.Cause, old, JavaName(oldNeed));
            }
            var wrapper = LaunchWrapperJava.Match(text);
            if (wrapper.Success)
                Add("NeedJava8", FindingLevel.Cause, wrapper);
            var module = ModuleAccess.Match(text);
            if (module.Success && !wrapper.Success)
                Add("JavaTooNew", FindingLevel.Cause, module);
            var main = MainClass.Match(text);
            if (main.Success)
                Add("MainClass", FindingLevel.Cause, main, main.Groups["class"].Value);

            CheckFabric(text, source, into, mods);
            CheckForge(text, source, into, mods);
            CheckMixin(text, source, into, mods);

            // Graphics.
            var pixel = PixelFormat.Match(text);
            if (pixel.Success)
                Add("GraphicsDriver", FindingLevel.Cause, pixel);
            var noDriver = GlfwNoDriver.Match(text);
            if (noDriver.Success)
                Add("GraphicsDriver", FindingLevel.Cause, noDriver);
            var glVersion = GlfwVersion.Match(text);
            if (glVersion.Success)
                Add("OpenGLVersion", FindingLevel.Cause, glVersion);
            if (jvm)
                CheckNativeCrash(text, source, into);
            var natives = Natives.Match(text);
            if (natives.Success)
                Add("Natives", FindingLevel.Cause, natives, natives.Groups["library"].Value);
            var bits = JavaVm32.Match(text);
            if (bits.Success)
                Add("Java32", FindingLevel.Hint, bits);

            // Damaged files.
            var zip = ZipError.Match(text);
            if (zip.Success)
            {
                string near = Window(text, zip.Index, 6);
                var path = JarPath.Match(near);
                string name = path.Success ? Path.GetFileName(path.Groups["path"].Value) : "";
                var finding = new CrashFinding(name.Length > 0 ? "CorruptFile" : "CorruptFileUnknown", FindingLevel.Cause, new object[] { name },
                    Lines(near, 3), source);
                if (name.Length > 0 && mods != null && mods.HasFile(name))
                    finding.Mods.Add(name);
                into.Add(finding);
            }

            if (!report)
                return;

            // Crash report sections.
            var debug = DebugCrash.Match(text);
            if (debug.Success)
                Add("DebugCrash", FindingLevel.Cause, debug);
            var entity = TickingEntity.Match(text);
            if (entity.Success)
            {
                var type = EntityType.Match(text, entity.Index);
                var location = EntityLocation.Match(text, entity.Index);
                var evidence = new List<string> { LineOf(text, entity.Index) };
                evidence.AddRange(new[] { type, location }.Where(m => m.Success).Select(m => LineOf(text, m.Index)));
                into.Add(new CrashFinding("TickingEntity", FindingLevel.Cause,
                    new object[] { type.Success ? type.Groups["value"].Value.Trim() : "?", location.Success ? location.Groups["value"].Value.Trim() : "?" },
                    evidence, source));
            }
            var block = TickingBlock.Match(text);
            if (block.Success)
            {
                int section = text.IndexOf("-- Block entity being ticked --", StringComparison.Ordinal);
                int from = section >= 0 ? section : block.Index;
                var name = BlockName.Match(text, from);
                var location = BlockLocation.Match(text, from);
                var evidence = new List<string> { LineOf(text, block.Index) };
                evidence.AddRange(new[] { name, location }.Where(m => m.Success).Select(m => LineOf(text, m.Index)));
                into.Add(new CrashFinding("TickingBlockEntity", FindingLevel.Cause,
                    new object[] { name.Success ? name.Groups["value"].Value.Trim() : "?", location.Success ? location.Groups["value"].Value.Trim() : "?" },
                    evidence, source));
            }
            CheckModLoading(text, source, into, mods);

            var suspected = SuspectedMods.Match(text);
            if (suspected.Success)
            {
                string value = suspected.Groups["value"].Value.Trim();
                if (!value.Equals("NONE", StringComparison.OrdinalIgnoreCase) && !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                    Add("SuspectedMods", FindingLevel.Hint, suspected, value);
            }

            // Errors that point at mods made for other versions.
            var mismatch = Mismatch.Match(text);
            if (mismatch.Success)
                Add("Mismatch", FindingLevel.Hint, mismatch, mismatch.Groups["kind"].Value,
                    mismatch.Groups["what"].Success ? mismatch.Groups["what"].Value.Trim() : "");
            var init = ClassInit.Match(text);
            if (init.Success)
            {
                // The first failure of the class is the Caused by chain of the same error, or an earlier error.
                var evidence = new List<string> { LineOf(text, init.Index) };
                var cause = RootCause(text, init.Index);
                if (cause != null)
                    evidence.Add(cause);
                into.Add(new CrashFinding("ClassInit", FindingLevel.Hint, new object[] { init.Groups["class"].Value.Replace('/', '.') }, evidence, source));
            }
            var missing = MissingClass.Match(text);
            if (missing.Success)
                Add("MissingClass", FindingLevel.Hint, missing, missing.Groups["class"].Value.Replace('/', '.'));
            var overflow = StackOverflow.Match(text);
            if (overflow.Success)
                Add("StackOverflow", FindingLevel.Hint, overflow);
        }

        // ======================== Loaders ========================

        private static void CheckFabric(string text, string source, List<CrashFinding> into, ModIndex mods)
        {
            int before = into.Count;
            var solutions = FabricSolution.Matches(text).Cast<Match>().Select(m => Clip(m.Value.Trim())).Distinct().Take(3).ToList();

            foreach (Match match in FabricMissing.Matches(text))
            {
                var dependency = FabricDependency(match.Groups["dep"].Value);
                string key = dependency.Id == "java" ? "FabricJava" : dependency.Id == "minecraft" ? "FabricGame" : "FabricMissing";
                AddFabric(into, key, match, text, source, solutions, mods, match.Groups["name"].Value, dependency.Name, "");
            }
            foreach (Match match in FabricWrong.Matches(text))
            {
                var dependency = FabricDependency(match.Groups["dep"].Value);
                string key = dependency.Id == "java" ? "FabricJava" : dependency.Id == "minecraft" ? "FabricGame" : "FabricWrongVersion";
                AddFabric(into, key, match, text, source, solutions, mods, match.Groups["name"].Value, dependency.Name, match.Groups["present"].Value);
            }
            foreach (Match match in FabricBreaks.Matches(text))
            {
                var dependency = FabricDependency(match.Groups["dep"].Value);
                AddFabric(into, "FabricBreaks", match, text, source, solutions, mods, match.Groups["name"].Value, dependency.Name, match.Groups["present"].Value);
            }
            foreach (Match match in FabricOldMissing.Matches(text))
                AddFabric(into, "FabricMissing", match, text, source, solutions, mods, match.Groups["id"].Value, match.Groups["dep"].Value, "");

            var header = FabricHeader.Match(text);
            if (header.Success && into.Count == before)
            {
                var evidence = new List<string> { LineOf(text, header.Index) };
                evidence.AddRange(Lines(Window(text, header.Index, 8), 8).Skip(1).Where(l => l.StartsWith("-", StringComparison.Ordinal)));
                into.Add(new CrashFinding("FabricResolution", FindingLevel.Cause, null, evidence, source));
            }
        }

        private static void AddFabric(List<CrashFinding> into, string key, Match match, string text, string source, List<string> solutions,
            ModIndex mods, params object[] args)
        {
            // The suggested fixes that name the same dependency go with the finding.
            var dependency = FabricDependency(match.Groups["dep"].Value);
            var evidence = new List<string> { LineOf(text, match.Index) };
            evidence.AddRange(solutions.Where(s => Mentions(s, dependency.Id) || Mentions(s, dependency.Name)));
            var finding = new CrashFinding(key, FindingLevel.Cause, args, evidence.Take(MaxEvidence).ToList(), source);
            string file = mods?.FileOfId(match.Groups["id"].Value);
            if (file != null)
                finding.Mods.Add(file);
            into.Add(finding);
        }

        /// <summary>Whether <paramref name="line"/> names <paramref name="word"/> as a whole word.</summary>
        private static bool Mentions(string line, string word)
        {
            return !string.IsNullOrEmpty(word)
                   && Regex.IsMatch(line, @"(?<![\w\-])" + Regex.Escape(word) + @"(?![\w\-])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }

        /// <summary>"mod 'Sodium' (sodium)" as its name and id; a bare id as both.</summary>
        private static (string Name, string Id) FabricDependency(string text)
        {
            text = text.Trim();
            var named = FabricDependencyName.Match(text);
            if (named.Success)
                return (named.Groups["name"].Value.Length > 0 ? named.Groups["name"].Value : named.Groups["id"].Value, named.Groups["id"].Value);
            if (text.StartsWith("mod ", StringComparison.Ordinal))
                text = text.Substring(4);
            text = text.Trim('\'', ' ');
            return (text, text);
        }

        private static void CheckForge(string text, string source, List<CrashFinding> into, ModIndex mods)
        {
            foreach (Match match in ForgeDependency.Matches(text))
            {
                string id = match.Groups["id"].Value;
                string by = match.Groups["by"].Value;
                string range = match.Groups["range"].Value;
                string actual = match.Groups["actual"].Value;
                string key;
                object[] args;
                if (id == "minecraft")
                {
                    key = "ForgeGame";
                    args = new object[] { by, range, actual };
                }
                else if (id == "forge" || id == "neoforge")
                {
                    key = actual == "[MISSING]" ? "ForgeLoaderMissing" : "ForgeLoader";
                    args = new object[] { by, id == "forge" ? "Forge" : "NeoForge", range, actual };
                }
                else if (actual == "[MISSING]")
                {
                    key = "ForgeMissing";
                    args = new object[] { by, id, range };
                }
                else
                {
                    key = "ForgeWrongVersion";
                    args = new object[] { by, id, range, actual };
                }
                var finding = new CrashFinding(key, FindingLevel.Cause, args, new[] { LineOf(text, match.Index) }, source);
                string file = mods?.FileOfId(by);
                if (file != null)
                    finding.Mods.Add(file);
                into.Add(finding);
            }
        }

        /// <summary>A crash report that lists the mods that failed to load, each in its own "-- MOD id --" section.</summary>
        private static void CheckModLoading(string text, string source, List<CrashFinding> into, ModIndex mods)
        {
            var description = ModLoadingDescription.Match(text);
            if (!description.Success)
                return;
            int found = 0;
            foreach (Match section in ModSection.Matches(text))
            {
                string body = Window(text, section.Index, 14);
                var file = ModFile.Match(body);
                var failure = FailureMessage.Match(body);
                if (!failure.Success)
                    continue;
                string id = section.Groups["id"].Value.Trim();
                var evidence = new[] { section, file, failure }.Where(m => m.Success).Select(m => Clip(m.Value.Trim())).ToList();
                var finding = new CrashFinding("ModLoadingFailed", FindingLevel.Cause, new object[] { id, Clip(failure.Groups["message"].Value.Trim()) },
                    evidence, source);
                string jar = file.Success ? Path.GetFileName(file.Groups["file"].Value.Trim()) : mods?.FileOfId(id);
                if (jar != null && (mods == null || mods.HasFile(jar)))
                    finding.Mods.Add(jar);
                into.Add(finding);
                if (++found >= 5)
                    break;
            }
            if (found == 0)
            {
                var evidence = new List<string> { LineOf(text, description.Index) };
                var headline = ExceptionLine.Match(text, description.Index + description.Length);
                if (headline.Success && headline.Index - description.Index < 2000)
                    evidence.Add(Clip(headline.Groups["text"].Value.Trim()));
                string cause = RootCause(text, description.Index);
                if (cause != null && !evidence.Contains(cause))
                    evidence.Add(cause);
                into.Add(new CrashFinding("ModLoading", FindingLevel.Cause, null, evidence, source));
            }
        }

        private static void CheckMixin(string text, string source, List<CrashFinding> into, ModIndex mods)
        {
            var details = MixinDetail.Matches(text).Cast<Match>().Select(m => Clip(m.Value)).Distinct().Take(2).ToList();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matches = MixinApplyMod.Matches(text).Cast<Match>()
                .Concat(MixinApply.Matches(text).Cast<Match>())
                .Concat(MixinPhase.Matches(text).Cast<Match>())
                .Concat(MixinInjection.Matches(text).Cast<Match>());
            foreach (var match in matches)
            {
                string config = match.Groups["config"].Value;
                if (seen.Count >= 3 || !seen.Add(config))
                    continue;
                string modId = match.Groups["mod"].Success ? match.Groups["mod"].Value : null;
                string file = (modId != null ? mods?.FileOfId(modId) : null) ?? mods?.FileOfMixin(config);
                string shown = modId ?? (file != null ? Path.GetFileNameWithoutExtension(file) : null);
                var evidence = new List<string> { LineOf(text, match.Index) };
                if (!match.Value.StartsWith("Critical injection failure", StringComparison.Ordinal))
                    evidence.AddRange(details);
                var finding = new CrashFinding(shown != null ? "Mixin" : "MixinConfig", FindingLevel.Cause, new object[] { shown ?? "", config },
                    evidence, source);
                if (file != null)
                    finding.Mods.Add(file);
                into.Add(finding);
            }
        }

        /// <summary>A Java error report: the library the virtual machine failed in.</summary>
        private static void CheckNativeCrash(string text, string source, List<CrashFinding> into)
        {
            var crash = NativeCrash.Match(text);
            if (!crash.Success)
                return;
            int problem = text.IndexOf("Problematic frame:", StringComparison.Ordinal);
            var frame = problem >= 0 ? ProblematicFrame.Match(text, problem) : Match.Empty;
            var evidence = new List<string> { LineOf(text, crash.Index) };
            if (frame.Success)
                evidence.Add(LineOf(text, frame.Index));

            string library = frame.Success ? frame.Groups["library"].Value.Trim() : "";
            if (library.Length > 0 && GraphicsDriver.IsMatch(library))
                into.Add(new CrashFinding("DriverCrash", FindingLevel.Cause, new object[] { library }, evidence, source));
            else
                into.Add(new CrashFinding("NativeCrash", FindingLevel.Cause, new object[] { library.Length > 0 ? library : "?" }, evidence, source));
        }

        // ======================== Summary ========================

        private static CrashSummary Summarize(string text)
        {
            var summary = new CrashSummary();
            var description = Description.Match(text);
            if (description.Success)
            {
                summary.Description = description.Groups["text"].Value.Trim();
                var headline = ExceptionLine.Match(text, description.Index + description.Length);
                if (headline.Success && headline.Index - description.Index < 2000)
                    summary.Headline = Clip(headline.Groups["text"].Value.Trim());
                summary.RootCause = RootCause(text, description.Index);
            }
            summary.GameVersion = Value(GameVersionLine, text);
            summary.JavaVersion = Value(JavaVersionLine, text);
            summary.MaxMemory = Value(MaxMemoryFlag, text);
            return summary.Description == null && summary.GameVersion == null && summary.JavaVersion == null ? null : summary;
        }

        /// <summary>
        /// The last "Caused by:" line of the error that starts at <paramref name="from"/>, up to the detailed part of a
        /// crash report; <c>null</c> when there is none.
        /// </summary>
        private static string RootCause(string text, int from)
        {
            int end = text.IndexOf("A detailed walkthrough of the error", from, StringComparison.Ordinal);
            if (end < 0)
                end = Math.Min(text.Length, from + 60000);
            Match last = null;
            for (var match = CausedBy.Match(text, from); match.Success && match.Index < end; match = match.NextMatch())
                last = match;
            return last == null ? null : Clip(last.Value.Trim());
        }

        private static string Value(Regex regex, string text)
        {
            var match = regex.Match(text);
            return match.Success ? Clip(match.Groups["value"].Value.Trim()) : null;
        }

        /// <summary>Whether one of the last lines of the log is the game's normal "Stopping!".</summary>
        private static bool EndsCleanly(string text)
        {
            int start = text.Length;
            for (int lines = 0; lines < 60 && start > 0; lines++)
                start = text.LastIndexOf('\n', Math.Max(0, start - 2));
            return CleanExit.IsMatch(text.Substring(Math.Max(0, start)));
        }

        // ======================== Text ========================

        /// <summary>The Java release that reads class files of version <paramref name="classVersion"/>; 52 is Java 8.</summary>
        private static string JavaName(int classVersion) => (classVersion - 44).ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static string LineOf(string text, int index)
        {
            if (text.Length == 0)
                return "";
            int start = text.LastIndexOf('\n', Math.Max(0, Math.Min(index, text.Length - 1))) + 1;
            int end = text.IndexOf('\n', Math.Min(index, text.Length - 1));
            if (end < 0)
                end = text.Length;
            return Clip(text.Substring(start, Math.Max(0, end - start)).Trim());
        }

        /// <summary>The text from the line at <paramref name="index"/> on, <paramref name="lines"/> lines long.</summary>
        private static string Window(string text, int index, int lines)
        {
            if (text.Length == 0)
                return "";
            int start = text.LastIndexOf('\n', Math.Max(0, Math.Min(index, text.Length - 1))) + 1;
            int end = start;
            for (int i = 0; i < lines && end >= 0 && end < text.Length; i++)
                end = text.IndexOf('\n', end + 1);
            if (end < 0)
                end = text.Length;
            return text.Substring(start, end - start);
        }

        private static List<string> Lines(string text, int count)
        {
            return text.Split('\n').Select(l => Clip(l.Trim())).Where(l => l.Length > 0).Take(count).ToList();
        }

        private static string Clip(string line)
        {
            return line.Length > MaxLine ? line.Substring(0, MaxLine) + "…" : line;
        }

        /// <summary>Whether a class in a stack frame belongs to Java, the game, a loader or a common library.</summary>
        internal static bool IsSystemClass(string className)
        {
            return SystemPackages.Any(p => className.StartsWith(p, StringComparison.Ordinal));
        }

        // ======================== Mods folder ========================

        /// <summary>The jars of a mods folder, by mod id, mixin configuration and Java package.</summary>
        private sealed class ModIndex
        {
            private readonly string _folder;
            private readonly HashSet<string> _files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, string> _ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, string> _mixins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private Dictionary<string, string> _packages;

            private ModIndex(string folder)
            {
                _folder = folder;
            }

            public static ModIndex Load(string folder)
            {
                var index = new ModIndex(folder);
                try
                {
                    if (!Directory.Exists(folder))
                        return index;
                    foreach (string path in Directory.EnumerateFiles(folder, "*.jar", SearchOption.TopDirectoryOnly))
                    {
                        string name = Path.GetFileName(path);
                        index._files.Add(name);
                        var manifest = ModManifest.Read(path);
                        if (manifest == null)
                            continue;
                        foreach (var metadata in manifest.Formats.Values)
                        {
                            foreach (string id in metadata.Ids)
                            {
                                if (!index._ids.ContainsKey(id))
                                    index._ids[id] = name;
                            }
                            foreach (string config in metadata.MixinConfigs)
                            {
                                if (!index._mixins.ContainsKey(config))
                                    index._mixins[config] = name;
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                }
                return index;
            }

            public bool HasFile(string name) => _files.Contains(name);

            public string FileOfId(string id) => !string.IsNullOrEmpty(id) && _ids.TryGetValue(id, out string file) ? file : null;

            public string FileOfMixin(string config) => _mixins.TryGetValue(config, out string file) ? file : null;

            /// <summary>The jars named in the stack traces of <paramref name="text"/>, by file name or by package, first seen first.</summary>
            public IReadOnlyList<string> FindInStack(string text)
            {
                var found = new List<string>();
                foreach (Match match in StackJar.Matches(text))
                {
                    string jar = match.Groups["jar"].Value;
                    if (_files.Contains(jar) && !found.Contains(jar, StringComparer.OrdinalIgnoreCase))
                        found.Add(jar);
                }

                foreach (Match match in MixinMethod.Matches(text))
                {
                    string jar = FileOfId(match.Groups["mod"].Value);
                    if (jar != null && !found.Contains(jar, StringComparer.OrdinalIgnoreCase))
                        found.Add(jar);
                }

                var classes = StackFrame.Matches(text).Cast<Match>().Select(m => m.Groups["class"].Value).Where(c => !IsSystemClass(c)).ToList();
                if (classes.Count > 0)
                {
                    var packages = Packages();
                    foreach (string className in classes)
                    {
                        int dot = className.LastIndexOf('.');
                        if (dot <= 0 || !packages.TryGetValue(className.Substring(0, dot), out string jar) || jar == null)
                            continue;
                        if (!found.Contains(jar, StringComparer.OrdinalIgnoreCase))
                            found.Add(jar);
                    }
                }
                return found.Take(5).ToList();
            }

            /// <summary>Each Java package in the top-level jars and the jar it is in; <c>null</c> for a package in several jars.</summary>
            private Dictionary<string, string> Packages()
            {
                if (_packages != null)
                    return _packages;
                _packages = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string name in _files)
                {
                    try
                    {
                        using (var zip = ZipFile.OpenRead(Path.Combine(_folder, name)))
                        {
                            var own = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var entry in zip.Entries)
                            {
                                string path = entry.FullName;
                                if (!path.EndsWith(".class", StringComparison.Ordinal) || path.StartsWith("META-INF/", StringComparison.Ordinal))
                                    continue;
                                int slash = path.LastIndexOf('/');
                                if (slash > 0)
                                    own.Add(path.Substring(0, slash).Replace('/', '.'));
                            }
                            foreach (string package in own)
                                _packages[package] = _packages.ContainsKey(package) ? null : name;
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
                    {
                    }
                }
                return _packages;
            }
        }
    }
}
