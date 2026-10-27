package campfire;

import com.sun.tools.attach.AgentLoadException;
import com.sun.tools.attach.VirtualMachine;

/**
 * Small bridge that attaches to the game process and loads the agent. It runs with the game's own
 * Java when that has the attach module, and with another Java 9 or later on this computer when the
 * game runs on Java 8, whose runtime cannot attach.
 */
public final class CampfireAttach {
    private CampfireAttach() {
    }

    public static void main(String[] args) throws Exception {
        if (args.length < 2) {
            System.err.println("usage: CampfireAttach <pid> <agent.jar> [options]");
            System.exit(2);
        }

        VirtualMachine machine = VirtualMachine.attach(args[0]);
        try {
            machine.loadAgent(args[1], args.length > 2 ? args[2] : null);
        } catch (AgentLoadException error) {
            // A Java 8 game answers a successful load with a bare "0", which newer attach code takes
            // for an error code. The agent is loaded all the same.
            String message = error.getMessage();
            String code = message == null ? "" : message.trim();
            if (!code.equals("0") && !code.endsWith(": 0")) {
                throw error;
            }
        } finally {
            machine.detach();
        }
    }
}
