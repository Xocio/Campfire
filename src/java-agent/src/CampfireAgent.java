package campfire;

import java.io.File;
import java.lang.instrument.Instrumentation;
import java.net.URL;
import java.util.jar.JarFile;

/**
 * Entry point loaded by the attach helper. It adds the hook to the server tick method, both in a
 * server class that is already loaded and in one the game loads later, such as when the host opens
 * a world after Campfire attached.
 *
 * <p>The hook itself is put on the bootstrap class path. Minecraft may load its server classes in a
 * child class loader (the dedicated server bundler does), and only the bootstrap loader is visible
 * to every one of them.</p>
 *
 * <p>Options are "key=value" pairs separated by '|': "state" is the folder with Campfire's state
 * files, "mappings" the official mapping file of an obfuscated release.</p>
 */
public final class CampfireAgent {
    private static final String TICK_SIGNATURE = "tickServer(java.util.function.BooleanSupplier)";

    private static boolean installed;

    private CampfireAgent() {
    }

    public static void agentmain(String args, Instrumentation instrumentation) {
        String state = null;
        String mappings = null;
        if (args != null) {
            for (String option : args.split("\\|")) {
                if (option.startsWith("state=")) {
                    state = option.substring("state=".length());
                } else if (option.startsWith("mappings=")) {
                    mappings = option.substring("mappings=".length());
                }
            }
        }
        // Every attach may name a newer state folder; the hook always reads the latest one.
        if (state != null) {
            System.setProperty("campfire.state", state);
        }
        if (installed) {
            System.err.println("[Campfire] Agent already loaded.");
            return;
        }
        installed = true;

        try {
            URL location = CampfireAgent.class.getProtectionDomain().getCodeSource().getLocation();
            if (location != null && "file".equalsIgnoreCase(location.getProtocol())) {
                File agentJar = new File(location.toURI());
                File hookJar = new File(agentJar.getParentFile(), "CampfireHook.jar");
                if (!hookJar.isFile()) {
                    System.err.println("[Campfire] Missing " + hookJar);
                    return;
                }
                // Only the small hook goes on the bootstrap class path; the agent and ASM stay out of
                // the game's class loader namespace.
                instrumentation.appendToBootstrapClassLoaderSearch(new JarFile(hookJar));
            }

            // The hook classes now resolve from the bootstrap path, the same ones the game will call.
            CampfireNames.load(mappings, CampfireHook.MEMBER_NAMES);
            String tick = CampfireNames.runtimeName("net.minecraft.server.MinecraftServer", TICK_SIGNATURE);
            String descriptor = CampfireTransformer.TICK_DESCRIPTOR;
            if (tick == null) {
                // Up to 1.12 the tick takes no argument.
                tick = CampfireNames.runtimeName("net.minecraft.server.MinecraftServer", "tickServer()");
                descriptor = tick == null ? descriptor : "()V";
            }
            instrumentation.addTransformer(new CampfireTransformer(tick, descriptor), true);

            int transformed = 0;
            for (Class<?> type : instrumentation.getAllLoadedClasses()) {
                if (!"net.minecraft.server.MinecraftServer".equals(type.getName())) {
                    continue;
                }
                try {
                    instrumentation.retransformClasses(type);
                    transformed++;
                } catch (Throwable error) {
                    System.err.println("[Campfire] Could not retransform " + type.getName() + ": " + error);
                }
            }

            CampfireHook.log("Agent loaded" + (tick != null ? " with mappings" : "")
                + "; transformed " + transformed + " loaded class(es).");
        } catch (Throwable error) {
            System.err.println("[Campfire] Agent failed: " + error);
            error.printStackTrace();
        }
    }
}
