import com.sun.tools.attach.VirtualMachine;

/**
 * Small bridge run with the game's own Java. It attaches to the game process and loads the agent.
 * Keeping the attach API in Java means Campfire does not need a JDK on the host beyond the one the
 * game already uses.
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
        } finally {
            machine.detach();
        }
    }
}
