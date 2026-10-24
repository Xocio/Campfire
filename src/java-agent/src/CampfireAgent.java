import java.io.File;
import java.lang.instrument.Instrumentation;
import java.net.URL;
import java.util.jar.JarFile;

/**
 * Entry point loaded by the attach helper. The agent is retransform-only: it adds the room status
 * hook to the already loaded server tick method.
 *
 * <p>The hook itself is put on the bootstrap class path. Minecraft may load its server classes in a
 * child class loader (the dedicated server bundler does), and only the bootstrap loader is visible
 * to every one of them.</p>
 */
public final class CampfireAgent {
    private static final String STATE_OPTION = "state=";

    private static boolean installed;

    private CampfireAgent() {
    }

    public static void agentmain(String args, Instrumentation instrumentation) {
        // Every room attaches again; the hook is installed once per game, but always reads the state
        // file from the folder named by the latest attach.
        if (args != null && args.startsWith(STATE_OPTION)) {
            System.setProperty("campfire.state", args.substring(STATE_OPTION.length()));
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

            instrumentation.addTransformer(new CampfireTransformer(), true);

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

            System.err.println("[Campfire] Agent loaded; transformed " + transformed + " class(es).");
        } catch (Throwable error) {
            System.err.println("[Campfire] Agent failed: " + error);
            error.printStackTrace();
        }
    }
}
