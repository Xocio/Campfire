import java.io.IOException;
import java.lang.reflect.Array;
import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.nio.file.StandardOpenOption;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;

/**
 * Runs inside the host's Minecraft process and writes the room status straight into the server's
 * outgoing packets. This is why it works in an authenticated LAN world, where Campfire cannot read
 * the encrypted connection.
 *
 * <p>Campfire keeps a small state file up to date while the room is open: a timestamp and the texts
 * in its display language. The overlay only shows while that file is fresh, and clears the player
 * list header and footer once the room ends or Campfire stops, so the game is left as it was.</p>
 *
 * <p>Everything is reflective so the agent compiles without Minecraft on the classpath. If a class
 * or method is missing, the overlay disables itself and the game is left untouched.</p>
 */
public final class CampfireHook {
    private static final long UPDATE_INTERVAL_MS = 5000L;
    private static final long JOIN_GRACE_MS = 2000L;
    private static final long STATE_CHECK_MS = 2000L;
    private static final long STATE_TIMEOUT_MS = 15000L;
    private static final String WEBSITE = "McCampfire.top";

    // Only touched on the server thread. Keyed by player id: respawning creates a new player object.
    private static final Map<UUID, Long> FirstSeen = new HashMap<>();
    private static final Set<UUID> Welcomed = new HashSet<>();

    private static boolean initialized;
    private static boolean disabled;
    private static boolean active;
    private static long lastStateCheck;
    private static long lastUpdate;
    private static int lastCount = -1;
    private static Map<String, String> texts = Collections.emptyMap();

    private static Class<?> componentClass;
    private static Class<?> mutableClass;
    private static Class<?> formattingClass;
    private static Class<?> packetClass;
    private static Class<?> tabPacketClass;
    private static Class<?> chatPacketClass;
    private static Class<?> serverPlayerClass;
    private static Class<?> connectionClass;
    private static Method literal;
    private static Method empty;
    private static Method append;
    private static Method withStyle;
    private static Method getPlayerList;
    private static Method getPlayers;
    private static Method getUuid;
    private static Method latency;
    private static Method send;
    private static Method getProfile;
    private static Method getName;
    private static Field connectionField;
    private static Constructor<?> tabConstructor;
    private static Constructor<?> chatConstructor;

    private static Object gold;
    private static Object gray;
    private static Object darkGray;
    private static Object white;
    private static Object aqua;
    private static Object green;
    private static Object yellow;
    private static Object red;
    private static Object bold;
    private static Object underline;

    private CampfireHook() {
    }

    /** Called at the head of MinecraftServer.tickServer by the transformer. */
    public static void tick(Object server) {
        if (disabled) {
            return;
        }
        try {
            if (!initialized) {
                initialize(server.getClass().getClassLoader());
            }

            long nowMs = System.currentTimeMillis();
            if (nowMs - lastStateCheck >= STATE_CHECK_MS) {
                lastStateCheck = nowMs;
                boolean wasActive = active;
                active = readState(nowMs, wasActive);
                if (wasActive && !active) {
                    clear(server);
                } else if (!wasActive && active) {
                    lastCount = -1;
                    markReady();
                }
            }
            if (!active) {
                return;
            }

            List<?> players = snapshotPlayers(server);
            if (players == null) {
                return;
            }

            Set<UUID> present = new HashSet<>();
            for (Object player : players) {
                present.add((UUID) getUuid.invoke(player));
            }
            // Someone who left and comes back is welcomed again.
            FirstSeen.keySet().retainAll(present);
            Welcomed.retainAll(present);

            int count = players.size();
            boolean countChanged = count != lastCount;
            lastCount = count;
            if (players.isEmpty()) {
                return;
            }

            String host = readHostName(server);
            boolean periodic = countChanged || nowMs - lastUpdate >= UPDATE_INTERVAL_MS;
            if (periodic) {
                lastUpdate = nowMs;
            }

            for (Object player : players) {
                try {
                    UUID id = (UUID) getUuid.invoke(player);
                    long firstSeen = FirstSeen.computeIfAbsent(id, key -> nowMs);
                    if (nowMs - firstSeen < JOIN_GRACE_MS) {
                        continue;
                    }

                    Object connection = connectionField.get(player);
                    if (connection == null) {
                        continue;
                    }

                    int ping = readLatency(connection);
                    boolean first = Welcomed.add(id);
                    if (first) {
                        send(connection, chatConstructor.newInstance(buildWelcome(host, count, ping), false));
                    }
                    if (first || periodic) {
                        send(connection, tabConstructor.newInstance(buildHeader(host, count, ping), buildFooter()));
                    }
                } catch (Throwable playerError) {
                    // A player may disconnect between the snapshot and the send. Keep the overlay alive.
                    log("Player update skipped: " + playerError);
                }
            }
        } catch (Throwable error) {
            disabled = true;
            log("Overlay disabled: " + error);
            error.printStackTrace();
        }
    }

    private static void initialize(ClassLoader loader) throws Exception {
        // The hook is loaded by the bootstrap class loader so every game class loader can find it.
        // Minecraft itself is not visible there, so resolve its classes through the server's loader.
        ClassLoader game = loader != null ? loader : Thread.currentThread().getContextClassLoader();
        componentClass = Class.forName("net.minecraft.network.chat.Component", true, game);
        mutableClass = Class.forName("net.minecraft.network.chat.MutableComponent", true, game);
        formattingClass = Class.forName("net.minecraft.ChatFormatting", true, game);
        packetClass = Class.forName("net.minecraft.network.protocol.Packet", true, game);
        tabPacketClass = Class.forName("net.minecraft.network.protocol.game.ClientboundTabListPacket", true, game);
        chatPacketClass = Class.forName("net.minecraft.network.protocol.game.ClientboundSystemChatPacket", true, game);
        serverPlayerClass = Class.forName("net.minecraft.server.level.ServerPlayer", true, game);
        connectionClass = Class.forName("net.minecraft.server.network.ServerCommonPacketListenerImpl", true, game);

        Class<?> minecraftServer = Class.forName("net.minecraft.server.MinecraftServer", true, game);
        Class<?> playerList = Class.forName("net.minecraft.server.players.PlayerList", true, game);
        Class<?> gameProfile = Class.forName("com.mojang.authlib.GameProfile", true, game);

        literal = componentClass.getMethod("literal", String.class);
        empty = componentClass.getMethod("empty");
        append = mutableClass.getMethod("append", componentClass);
        withStyle = mutableClass.getMethod("withStyle", Array.newInstance(formattingClass, 0).getClass());
        getPlayerList = minecraftServer.getMethod("getPlayerList");
        getPlayers = playerList.getMethod("getPlayers");
        getUuid = serverPlayerClass.getMethod("getUUID");
        latency = connectionClass.getMethod("latency");
        send = connectionClass.getMethod("send", packetClass);
        getProfile = minecraftServer.getMethod("getSingleplayerProfile");
        try {
            getName = gameProfile.getMethod("getName");
        } catch (NoSuchMethodException newerAuthlib) {
            // Authlib 10 made GameProfile a record with name() instead of getName().
            getName = gameProfile.getMethod("name");
        }
        connectionField = serverPlayerClass.getField("connection");

        tabConstructor = tabPacketClass.getConstructor(componentClass, componentClass);
        chatConstructor = chatPacketClass.getConstructor(componentClass, boolean.class);

        gold = formatting("GOLD");
        gray = formatting("GRAY");
        darkGray = formatting("DARK_GRAY");
        white = formatting("WHITE");
        aqua = formatting("AQUA");
        green = formatting("GREEN");
        yellow = formatting("YELLOW");
        red = formatting("RED");
        bold = formatting("BOLD");
        underline = formatting("UNDERLINE");

        initialized = true;
        log("Overlay ready.");
    }

    private static Object formatting(String name) throws Exception {
        @SuppressWarnings({"unchecked", "rawtypes"})
        Object value = Enum.valueOf((Class) formattingClass, name);
        return value;
    }

    // ======================== Room state ========================

    private static Path stateDirectory() {
        String directory = System.getProperty("campfire.state", "");
        if (!directory.isEmpty()) {
            return Paths.get(directory);
        }
        String local = System.getenv("LOCALAPPDATA");
        return Paths.get(local != null ? local : System.getProperty("user.home", "."), "Campfire", "Link");
    }

    private static Path stateFile(String extension) {
        return stateDirectory().resolve("overlay-" + ProcessHandle.current().pid() + extension);
    }

    /**
     * Whether the room is open: the state file exists and Campfire wrote it recently. Lines are
     * "key=value"; "time" is in Unix milliseconds and the rest are the texts to show.
     */
    private static boolean readState(long nowMs, boolean wasActive) {
        Path path = stateFile(".txt");
        if (!Files.exists(path)) {
            return false;
        }
        try {
            Map<String, String> values = new HashMap<>();
            for (String line : Files.readAllLines(path, StandardCharsets.UTF_8)) {
                int split = line.indexOf('=');
                if (split > 0) {
                    values.put(line.substring(0, split), line.substring(split + 1));
                }
            }
            long time = Long.parseLong(values.getOrDefault("time", "0"));
            if (nowMs - time > STATE_TIMEOUT_MS) {
                return false;
            }
            texts = values;
            return true;
        } catch (IOException busy) {
            // Campfire may be replacing the file right now; keep the last answer.
            return wasActive;
        } catch (NumberFormatException broken) {
            return false;
        }
    }

    /** Tells Campfire the overlay works, so its own fallback stays quiet. */
    private static void markReady() {
        try {
            Files.write(stateFile(".ready"), "ok".getBytes(StandardCharsets.UTF_8));
        } catch (IOException error) {
            log("Could not confirm the overlay: " + error);
        }
    }

    /** Empties the player list header and footer and forgets who was welcomed. */
    private static void clear(Object server) throws Exception {
        FirstSeen.clear();
        Welcomed.clear();
        lastCount = -1;
        List<?> players = snapshotPlayers(server);
        if (players == null) {
            return;
        }
        for (Object player : players) {
            try {
                Object connection = connectionField.get(player);
                if (connection != null) {
                    send(connection, tabConstructor.newInstance(component(), component()));
                }
            } catch (Throwable playerError) {
                log("Player update skipped: " + playerError);
            }
        }
    }

    private static List<?> snapshotPlayers(Object server) throws Exception {
        Object playerList = getPlayerList.invoke(server);
        if (playerList == null) {
            return null;
        }
        List<?> live = (List<?>) getPlayers.invoke(playerList);
        // The server's player list is live; take a snapshot so a join or leave cannot break the tick.
        return live == null ? null : new ArrayList<>(live);
    }

    // ======================== Messages ========================

    private static String readHostName(Object server) {
        try {
            Object profile = getProfile.invoke(server);
            if (profile != null) {
                Object name = getName.invoke(profile);
                if (name != null && !String.valueOf(name).isEmpty()) {
                    return String.valueOf(name);
                }
            }
        } catch (Throwable ignored) {
        }
        return t("host", "房主");
    }

    private static int readLatency(Object connection) {
        try {
            Object value = latency.invoke(connection);
            return value instanceof Number ? ((Number) value).intValue() : -1;
        } catch (Throwable ignored) {
            return -1;
        }
    }

    private static Object buildHeader(String host, int count, int ping) throws Exception {
        Object line = component();
        line = add(line, text("Campfire", gold, bold));
        line = add(line, text(" · ", darkGray));
        line = add(line, text(t("tabTitle", "联机房间"), white));
        line = add(line, text("\n", white));
        line = add(line, status(host, count, ping));
        return line;
    }

    private static Object buildFooter() throws Exception {
        Object line = component();
        line = add(line, text(WEBSITE, aqua, bold, underline));
        return line;
    }

    private static Object buildWelcome(String host, int count, int ping) throws Exception {
        Object line = component();
        line = add(line, text("Campfire", gold, bold));
        line = add(line, text(" · ", darkGray));
        line = add(line, text(t("welcome", "欢迎加入联机房间"), white));
        line = add(line, text("\n", white));
        line = add(line, status(host, count, ping));
        line = add(line, text("\n", white));
        line = add(line, text(WEBSITE, aqua, bold, underline));
        return line;
    }

    private static Object status(String host, int count, int ping) throws Exception {
        Object line = component();
        line = add(line, text(t("host", "房主") + " ", gray));
        line = add(line, text(host, white));
        line = add(line, text(" · ", darkGray));
        line = add(line, text(t("online", "在线") + " ", gray));
        line = add(line, text(t("players", "{0} 人").replace("{0}", String.valueOf(count)), white));
        line = add(line, text(" · ", darkGray));
        line = add(line, text(t("latency", "延迟") + " ", gray));
        if (ping < 0) {
            line = add(line, text(t("measuring", "测量中"), gray));
        } else {
            String value = t("latencyValue", "{0} ms").replace("{0}", String.valueOf(ping));
            line = add(line, text(value, ping <= 80 ? green : ping <= 160 ? yellow : red));
        }
        return line;
    }

    /** A text from the state file, so it follows Campfire's display language. */
    private static String t(String key, String fallback) {
        String value = texts.get(key);
        return value == null || value.isEmpty() ? fallback : value;
    }

    private static Object component() throws Exception {
        return empty.invoke(null);
    }

    private static Object text(String value, Object... styles) throws Exception {
        Object result = literal.invoke(null, value == null ? "" : value);
        if (styles.length > 0) {
            Object array = Array.newInstance(formattingClass, styles.length);
            for (int i = 0; i < styles.length; i++) {
                Array.set(array, i, styles[i]);
            }
            result = withStyle.invoke(result, new Object[] { array });
        }
        return result;
    }

    private static Object add(Object base, Object part) throws Exception {
        return append.invoke(base, part);
    }

    private static void send(Object connection, Object packet) throws Exception {
        send.invoke(connection, packet);
    }

    static void log(String message) {
        String line = "[Campfire] " + message;
        System.err.println(line);
        try {
            String local = System.getenv("LOCALAPPDATA");
            Path path = local != null && !local.isEmpty()
                ? Paths.get(local, "Campfire", "agent.log")
                : Paths.get(System.getProperty("user.home", "."), ".campfire-agent.log");
            Files.createDirectories(path.getParent());
            Files.write(path, (line + System.lineSeparator()).getBytes(StandardCharsets.UTF_8),
                StandardOpenOption.CREATE, StandardOpenOption.APPEND);
        } catch (IOException ignored) {
        }
    }
}
