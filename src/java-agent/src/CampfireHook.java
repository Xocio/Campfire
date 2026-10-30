package campfire;

import java.io.IOException;
import java.lang.management.ManagementFactory;
import java.lang.ref.WeakReference;
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
import java.util.Arrays;
import java.util.Collections;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;

/**
 * Runs inside the host's Minecraft process, at the head of every server tick. It does two things,
 * each only while Campfire asks for it through a small state file:
 *
 * <ul>
 * <li>Room overlay: writes the room status into the server's outgoing packets. The player list header
 * and footer are cleared once the room ends or Campfire stops.</li>
 * <li>Open to LAN: once the host is in the world Campfire imported, opens it to LAN the same way
 * the game's own button does.</li>
 * </ul>
 *
 * <p>Everything is reflective. Names go through {@link CampfireNames}, which also handles the
 * obfuscated releases. If something a feature needs is missing, that feature disables itself.</p>
 */
public final class CampfireHook {
    private static final long UPDATE_INTERVAL_MS = 5000L;
    private static final long JOIN_GRACE_MS = 2000L;
    private static final long STATE_CHECK_MS = 2000L;
    private static final long STATE_TIMEOUT_MS = 15000L;
    /** How long the host must have been in the world before it is opened to LAN; loading must settle first. */
    private static final long LAN_DELAY_MS = 3000L;
    private static final String WEBSITE = "McCampfire.top";

    private static final String COMPONENT = "net.minecraft.network.chat.Component";
    private static final String FORMATTING = "net.minecraft.ChatFormatting";
    private static final String PACKET = "net.minecraft.network.protocol.Packet";
    private static final String GAME_TYPE = "net.minecraft.world.level.GameType";
    private static final String SCOPE = "net.minecraft.server.MinecraftServer$MultiplayerScope";

    /** Every official member name the hook looks up; the mapping file is only read for these. */
    public static final Set<String> MEMBER_NAMES = Collections.unmodifiableSet(new HashSet<>(Arrays.asList(
        "tickServer", "getPlayerList", "getPlayers", "getUUID", "connection", "send", "latency",
        "getSingleplayerProfile", "getSingleplayerName", "literal", "empty", "append", "withStyle",
        "getByName", "SYSTEM", "publishServer", "isPublished", "getAvailablePort", "storageSource",
        "getLevelId", "getLevelIdName", "getDefaultGameType", "getWorldData", "getAllowCommands",
        "isAllowCommands", "LAN", "header", "footer", "getLevel", "getLevelData", "OVERWORLD", "players")));

    // Only touched on the server thread. Keyed by player id: respawning creates a new player object.
    private static final Map<UUID, Long> FirstSeen = new HashMap<>();
    private static final Set<UUID> Welcomed = new HashSet<>();

    private static String pid;
    private static boolean initialized;
    private static boolean overlayDisabled;
    private static boolean lanDisabled;

    private static boolean active;
    private static long lastStateCheck;
    private static long lastUpdate;
    private static int lastCount = -1;
    private static Map<String, String> texts = Collections.emptyMap();

    private static Map<String, String> lanState;
    private static long lastLanCheck;
    private static long lanPlayersSince;
    /**
     * The server and request the LAN step already ran for.
     */
    private static WeakReference<Object> lanHandled = new WeakReference<>(null);
    private static String lanHandledRequest = "";

    // ---- Text ----
    private static Class<?> componentClass;
    private static Class<?> formattingClass;
    private static Method literal;
    private static Method empty;
    private static Constructor<?> textConstructor;
    private static Method append;
    private static Method withStyle;
    private static Method formattingByName;

    // ---- Packets and players ----
    private static Constructor<?> tabConstructor;
    private static Field tabHeader;
    private static Field tabFooter;
    private static Constructor<?> chatConstructor;
    private static Object[] chatExtra;
    private static Method getPlayerList;
    private static Method getPlayers;
    private static Field playersField;
    private static Method getUuid;
    private static Field connectionField;
    private static Method send;
    private static Method latencyMethod;
    private static Field latencyField;
    private static Method getProfile;
    private static Method getProfileName;
    private static Method getHostName;

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
        if (overlayDisabled && lanDisabled) {
            refuseLan();
            return;
        }
        try {
            if (!initialized) {
                initialize(server);
            }
        } catch (Throwable error) {
            overlayDisabled = true;
            lanDisabled = true;
            log("Hook disabled: " + error);
            refuseLan();
            return;
        }

        long nowMs = System.currentTimeMillis();
        if (!lanDisabled) {
            try {
                tickLan(server, nowMs);
            } catch (Throwable error) {
                lanDisabled = true;
                log("Open to LAN disabled: " + error);
                refuseLan();
            }
        }
        if (!overlayDisabled) {
            try {
                tickOverlay(server, nowMs);
            } catch (Throwable error) {
                overlayDisabled = true;
                log("Overlay disabled: " + error);
                error.printStackTrace();
            }
        }
    }

    // ======================== Setup ========================

    private static void initialize(Object server) throws Exception {
        initialized = true;
        // The hook is loaded by the bootstrap class loader so every game class loader can find it.
        // Minecraft's classes are resolved through the server's loader.
        ClassLoader loader = server.getClass().getClassLoader();
        initialize(loader != null ? loader : Thread.currentThread().getContextClassLoader());
    }

    /** Looks up everything the hook uses in the game classes {@code game} loads. */
    static void initialize(ClassLoader game) throws Exception {

        Class<?> minecraftServer = CampfireNames.type("net.minecraft.server.MinecraftServer", game);
        Class<?> playerList = CampfireNames.type("net.minecraft.server.players.PlayerList", game);
        Class<?> serverPlayer = CampfireNames.type("net.minecraft.server.level.ServerPlayer", game);
        getPlayerList = require(CampfireNames.method(minecraftServer, "getPlayerList"), "getPlayerList");
        getPlayers = CampfireNames.method(playerList, "getPlayers");
        if (getPlayers == null) {
            // Up to 1.7 the list is a public field without a getter.
            playersField = require(CampfireNames.field(playerList, "players"), "getPlayers");
        }
        getUuid = require(CampfireNames.method(serverPlayer, "getUUID"), "getUUID");
        connectionField = require(CampfireNames.field(serverPlayer, "connection"), "connection");

        try {
            initializeText(game);
        } catch (Throwable error) {
            // Without text the overlay cannot work; opening to LAN still can, just without a message.
            componentClass = null;
            overlayDisabled = true;
            log("Overlay unavailable: " + error);
        }

        Class<?> listener = CampfireNames.type("net.minecraft.server.network.ServerGamePacketListenerImpl", game);
        send = CampfireNames.method(listener, "send", PACKET);
        latencyMethod = CampfireNames.method(listener, "latency");
        if (latencyMethod == null) {
            // Before 1.20.2 the latency is a field of the player.
            latencyField = CampfireNames.field(serverPlayer, "latency");
        }

        getProfile = CampfireNames.method(minecraftServer, "getSingleplayerProfile");
        if (getProfile != null) {
            Class<?> gameProfile = Class.forName("com.mojang.authlib.GameProfile", false, game);
            try {
                getProfileName = gameProfile.getMethod("getName");
            } catch (NoSuchMethodException newerAuthlib) {
                // Authlib 10 made GameProfile a record with name() instead of getName().
                getProfileName = gameProfile.getMethod("name");
            }
        } else {
            // Before 1.19 the server only knows the host by name.
            getHostName = CampfireNames.method(minecraftServer, "getSingleplayerName");
        }

        // The player list header and footer exist since 1.8; before, the room shows in chat alone.
        if (send == null || chatConstructor == null) {
            overlayDisabled = true;
        }
        log("Hook ready" + (CampfireNames.hasMappings() ? " with mappings" : "")
            + "; overlay " + (overlayDisabled ? "unavailable" : "ready") + ".");
    }

    /** Text components and the two packets that carry them, which changed shape over the years. */
    private static void initializeText(ClassLoader game) throws Exception {
        componentClass = CampfireNames.type(COMPONENT, game);
        formattingClass = CampfireNames.type(FORMATTING, game);

        // 1.19 and later: Component.literal and Component.empty. Before: new TextComponent(text).
        literal = CampfireNames.method(componentClass, "literal", "java.lang.String");
        empty = CampfireNames.method(componentClass, "empty");
        if (literal == null) {
            Class<?> textComponent = CampfireNames.type("net.minecraft.network.chat.TextComponent", game);
            textConstructor = require(CampfireNames.constructor(textComponent, "java.lang.String"), "TextComponent");
        }

        // append and withStyle live on MutableComponent since 1.16, on Component before.
        Class<?> mutable = CampfireNames.optionalType("net.minecraft.network.chat.MutableComponent", game);
        Class<?> styled = mutable != null ? mutable : componentClass;
        String array = FORMATTING + "[]";
        append = require(CampfireNames.method(styled, "append", COMPONENT), "append");
        // 1.8.9 has no withStyle; text then carries section-sign codes instead.
        withStyle = CampfireNames.method(styled, "withStyle", array);
        formattingByName = CampfireNames.method(formattingClass, "getByName", "java.lang.String");

        // 1.7 has no player list header and footer at all.
        Class<?> tabPacket = CampfireNames.optionalType("net.minecraft.network.protocol.game.ClientboundTabListPacket", game);
        tabConstructor = tabPacket == null ? null : CampfireNames.constructor(tabPacket, COMPONENT, COMPONENT);
        if (tabPacket != null && tabConstructor == null) {
            // Up to 1.16 the client jar keeps only the empty constructor; header and footer are fields.
            tabConstructor = CampfireNames.constructor(tabPacket);
            tabHeader = CampfireNames.field(tabPacket, "header");
            tabFooter = CampfireNames.field(tabPacket, "footer");
            if (tabConstructor == null || tabHeader == null || tabFooter == null) {
                tabConstructor = null;
                tabHeader = null;
            }
        }

        // 1.19 and later: a system chat packet. Before: the chat packet with the SYSTEM chat type,
        // and since 1.16 also a sender id, which is all zeros for the server itself.
        Class<?> systemChat = CampfireNames.optionalType("net.minecraft.network.protocol.game.ClientboundSystemChatPacket", game);
        if (systemChat != null) {
            chatConstructor = CampfireNames.constructor(systemChat, COMPONENT, "boolean");
            chatExtra = new Object[] { false };
            if (chatConstructor == null) {
                // 1.19 and 1.19.1 take the chat type id instead; 1 is the system type.
                chatConstructor = CampfireNames.constructor(systemChat, COMPONENT, "int");
                chatExtra = new Object[] { 1 };
            }
        } else {
            Class<?> chat = CampfireNames.type("net.minecraft.network.protocol.game.ClientboundChatPacket", game);
            Class<?> chatType = CampfireNames.optionalType("net.minecraft.network.chat.ChatType", game);
            if (chatType != null) {
                Object system = CampfireNames.constant(chatType, "SYSTEM");
                chatConstructor = CampfireNames.constructor(chat, COMPONENT, "net.minecraft.network.chat.ChatType", "java.util.UUID");
                chatExtra = new Object[] { system, new UUID(0L, 0L) };
                if (chatConstructor == null) {
                    chatConstructor = CampfireNames.constructor(chat, COMPONENT, "net.minecraft.network.chat.ChatType");
                    chatExtra = new Object[] { system };
                }
            } else {
                // 1.8.9: the one-argument constructor sends a system message.
                chatConstructor = CampfireNames.constructor(chat, COMPONENT);
                chatExtra = new Object[0];
            }
        }

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
    }

    /**
     * A formatting code by its constant name. The constants themselves are renamed in obfuscated
     * releases, but every release finds them by their lower-case name through getByName.
     */
    private static Object formatting(String name) throws Exception {
        if (formattingByName != null) {
            Object value = formattingByName.invoke(null, name.toLowerCase());
            if (value != null) {
                return value;
            }
        }
        @SuppressWarnings({"unchecked", "rawtypes"})
        Object value = Enum.valueOf((Class) formattingClass, name);
        return value;
    }

    private static <T> T require(T value, String what) throws NoSuchMethodException {
        if (value == null) {
            throw new NoSuchMethodException(what);
        }
        return value;
    }

    // ======================== Open to LAN ========================

    /**
     * Opens the world to LAN once the host has been in it for a moment, if Campfire asked for this world,
     * or for any world ("*") when a room is being created. Each request runs once per world load: if the
     * host closes LAN again, it stays closed.
     */
    private static void tickLan(Object server, long nowMs) throws Exception {
        if (nowMs - lastLanCheck >= STATE_CHECK_MS) {
            lastLanCheck = nowMs;
            lanState = readStateFile("lan-", nowMs);
        }
        String request = lanState == null ? "" : lanState.getOrDefault("id", "");
        if (lanState == null || lanHandled.get() == server && request.equals(lanHandledRequest)) {
            lanPlayersSince = 0;
            return;
        }

        List<?> players = snapshotPlayers(server);
        if (players == null || players.isEmpty()) {
            lanPlayersSince = 0;
            return;
        }
        if (lanPlayersSince == 0) {
            lanPlayersSince = nowMs;
        }
        if (nowMs - lanPlayersSince < LAN_DELAY_MS) {
            return;
        }

        lanHandled = new WeakReference<>(server);
        lanHandledRequest = request;
        String wanted = lanState.getOrDefault("world", "");
        String world = levelId(server);
        if (!"*".equals(wanted) && (world == null || !world.equalsIgnoreCase(wanted))) {
            return;
        }
        if (world == null) {
            world = "the world";
        }

        Method isPublished = CampfireNames.method(server.getClass(), "isPublished");
        if (isPublished != null && Boolean.TRUE.equals(isPublished.invoke(server))) {
            writeLanResult("port=already");
            return;
        }

        int port = publish(server);
        if (port <= 0) {
            log("Open to LAN failed for " + world + ".");
            writeLanResult("port=0");
            return;
        }

        log("Opened " + world + " to LAN on port " + port + ".");
        writeLanResult("port=" + port);
        if (componentClass != null && send != null && chatConstructor != null) {
            Object line = component();
            line = add(line, text("Campfire", gold, bold));
            line = add(line, text(" · ", darkGray));
            line = add(line, text(lanText("opened", "已自动对局域网开放，端口 {0}").replace("{0}", String.valueOf(port)), white));
            for (Object player : players) {
                Object connection = connectionField.get(player);
                if (connection != null) {
                    send(connection, chatPacket(line));
                }
            }
        }
    }

    /**
     * Answers a pending request with a failure once this game cannot open worlds to LAN.
     */
    private static void refuseLan() {
        long nowMs = System.currentTimeMillis();
        if (nowMs - lastLanCheck < STATE_CHECK_MS) {
            return;
        }
        lastLanCheck = nowMs;
        if (readStateFile("lan-", nowMs) != null && !Files.exists(stateDirectory().resolve("lan-" + pid() + ".done"))) {
            writeLanResult("port=0");
        }
    }

    /** Opens the integrated server to LAN on a free port; returns the port, or 0 when it did not work. */
    private static int publish(Object server) throws Exception {
        ClassLoader game = server.getClass().getClassLoader();
        Class<?> minecraftServer = CampfireNames.type("net.minecraft.server.MinecraftServer", game);

        // 26.2 and later: publishServer(scope, port) keeps the world's game mode and cheat setting.
        Class<?> scope = CampfireNames.optionalType(SCOPE, game);
        if (scope != null) {
            Method method = CampfireNames.method(server.getClass(), "publishServer", SCOPE, "int");
            if (method != null) {
                int port = availablePort(game);
                Object lan = CampfireNames.constant(scope, "LAN");
                return Boolean.TRUE.equals(method.invoke(server, lan, port)) ? port : 0;
            }
        }

        // 1.14 to 26.1: publishServer(game mode, allow cheats, port), as the Open to LAN screen calls it
        // with the world's own settings.
        Object gameType = require(CampfireNames.method(minecraftServer, "getDefaultGameType"), "getDefaultGameType").invoke(server);
        boolean cheats = allowCommands(server, minecraftServer);
        Method method = CampfireNames.method(server.getClass(), "publishServer", GAME_TYPE, "boolean", "int");
        if (method != null) {
            int port = availablePort(game);
            return Boolean.TRUE.equals(method.invoke(server, gameType, cheats, port)) ? port : 0;
        }

        // 1.8.9: the game picks the port itself and returns it as text, or null when it failed.
        method = require(CampfireNames.method(server.getClass(), "publishServer", GAME_TYPE, "boolean"), "publishServer");
        Object port = method.invoke(server, gameType, cheats);
        try {
            return port == null ? 0 : Integer.parseInt(String.valueOf(port).trim());
        } catch (NumberFormatException unknown) {
            return 0;
        }
    }

    private static int availablePort(ClassLoader game) throws Exception {
        Class<?> httpUtil = CampfireNames.type("net.minecraft.util.HttpUtil", game);
        Method availablePort = require(CampfireNames.method(httpUtil, "getAvailablePort"), "getAvailablePort");
        return ((Number) availablePort.invoke(null)).intValue();
    }

    /** Whether the world allows cheats; {@code false} when the release does not tell. */
    private static boolean allowCommands(Object server, Class<?> minecraftServer) {
        try {
            Object data = null;
            Method getWorldData = CampfireNames.method(minecraftServer, "getWorldData");
            if (getWorldData != null) {
                data = getWorldData.invoke(server);
            } else {
                // 1.8.9 to 1.15 keep the settings on the overworld's level data. 1.14 and 1.15 name the
                // overworld by its dimension type, 1.8.9 by its number, 0.
                ClassLoader game = server.getClass().getClassLoader();
                String dimension = "net.minecraft.world.level.dimension.DimensionType";
                Class<?> dimensionType = CampfireNames.optionalType(dimension, game);
                Method getLevel = dimensionType == null ? null : CampfireNames.method(minecraftServer, "getLevel", dimension);
                Object level = getLevel == null ? null : getLevel.invoke(server, CampfireNames.constant(dimensionType, "OVERWORLD"));
                if (level == null) {
                    Method byNumber = CampfireNames.method(minecraftServer, "getLevel", "int");
                    level = byNumber == null ? null : byNumber.invoke(server, 0);
                }
                Method getLevelData = level == null ? null : CampfireNames.method(level.getClass(), "getLevelData");
                data = getLevelData == null ? null : getLevelData.invoke(level);
            }
            if (data == null) {
                return false;
            }
            Method allow = CampfireNames.method(data.getClass(), "getAllowCommands");
            if (allow == null) {
                allow = CampfireNames.method(data.getClass(), "isAllowCommands");
            }
            return allow != null && Boolean.TRUE.equals(allow.invoke(data));
        } catch (Throwable unknown) {
            return false;
        }
    }

    /** The folder name of the running world. */
    private static String levelId(Object server) throws Exception {
        Class<?> minecraftServer = CampfireNames.type("net.minecraft.server.MinecraftServer", server.getClass().getClassLoader());
        Field storage = CampfireNames.field(minecraftServer, "storageSource");
        if (storage != null) {
            Object access = storage.get(server);
            Method getLevelId = access == null ? null : CampfireNames.method(access.getClass(), "getLevelId");
            if (getLevelId != null) {
                return (String) getLevelId.invoke(access);
            }
        }
        // 1.14 and 1.15 keep the folder name on the server itself.
        Method getLevelIdName = CampfireNames.method(minecraftServer, "getLevelIdName");
        return getLevelIdName == null ? null : (String) getLevelIdName.invoke(server);
    }

    private static String lanText(String key, String fallback) {
        String value = lanState == null ? null : lanState.get(key);
        return value == null || value.isEmpty() ? fallback : value;
    }

    private static void writeLanResult(String line) {
        try {
            Files.write(stateDirectory().resolve("lan-" + pid() + ".done"), line.getBytes(StandardCharsets.UTF_8));
        } catch (IOException error) {
            log("Could not report the LAN result: " + error);
        }
    }

    // ======================== Overlay ========================

    private static void tickOverlay(Object server, long nowMs) throws Exception {
        if (nowMs - lastStateCheck >= STATE_CHECK_MS) {
            lastStateCheck = nowMs;
            boolean wasActive = active;
            Map<String, String> state = readStateFile("overlay-", nowMs);
            active = state != null;
            if (active) {
                texts = state;
            }
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
                Long seen = FirstSeen.get(id);
                if (seen == null) {
                    FirstSeen.put(id, nowMs);
                    seen = nowMs;
                }
                if (nowMs - seen < JOIN_GRACE_MS) {
                    continue;
                }

                Object connection = connectionField.get(player);
                if (connection == null) {
                    continue;
                }

                int ping = readLatency(player, connection);
                boolean first = Welcomed.add(id);
                if (first) {
                    for (Object message : buildWelcome(host, count, ping)) {
                        send(connection, chatPacket(message));
                    }
                }
                if (first || periodic) {
                    if (tabConstructor != null) {
                        send(connection, tabPacket(buildHeader(host, count, ping), buildFooter()));
                    }
                }
            } catch (Throwable playerError) {
                // A player may disconnect between the snapshot and the send. Keep the overlay alive.
                log("Player update skipped: " + playerError);
            }
        }
    }

    // ======================== State files ========================

    private static Path stateDirectory() {
        String directory = System.getProperty("campfire.state", "");
        if (!directory.isEmpty()) {
            return Paths.get(directory);
        }
        String local = System.getenv("LOCALAPPDATA");
        return Paths.get(local != null ? local : System.getProperty("user.home", "."), "Campfire", "Link");
    }

    /** This process's id; the state files carry it, so two games never read each other's. */
    private static String pid() {
        if (pid == null) {
            // "1234@host" on every Java release; ProcessHandle only exists since Java 9.
            String name = ManagementFactory.getRuntimeMXBean().getName();
            int at = name.indexOf('@');
            pid = at > 0 ? name.substring(0, at) : name;
        }
        return pid;
    }

    /**
     * Reads "&lt;prefix&gt;&lt;pid&gt;.txt" when it exists and Campfire wrote it recently, else returns
     * {@code null}. Lines are "key=value"; "time" is in Unix milliseconds.
     */
    private static Map<String, String> readStateFile(String prefix, long nowMs) {
        Path path = stateDirectory().resolve(prefix + pid() + ".txt");
        if (!Files.exists(path)) {
            return null;
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
            return nowMs - time > STATE_TIMEOUT_MS ? null : values;
        } catch (IOException busy) {
            // Campfire may be replacing the file right now; keep the last answer.
            return "overlay-".equals(prefix) ? (active ? texts : null) : lanState;
        } catch (NumberFormatException broken) {
            return null;
        }
    }

    /** Tells Campfire the overlay works, so its own fallback stays quiet. */
    private static void markReady() {
        try {
            Files.write(stateDirectory().resolve("overlay-" + pid() + ".ready"), "ok".getBytes(StandardCharsets.UTF_8));
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
                if (connection != null && tabConstructor != null) {
                    send(connection, tabPacket(component(), component()));
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
        List<?> live = (List<?>) (getPlayers != null ? getPlayers.invoke(playerList) : playersField.get(playerList));
        // A snapshot of the server's player list.
        return live == null ? null : new ArrayList<>(live);
    }

    // ======================== Messages ========================

    private static String readHostName(Object server) {
        try {
            Object name = null;
            if (getProfile != null) {
                Object profile = getProfile.invoke(server);
                name = profile == null ? null : getProfileName.invoke(profile);
            } else if (getHostName != null) {
                name = getHostName.invoke(server);
            }
            if (name != null && !String.valueOf(name).isEmpty()) {
                return String.valueOf(name);
            }
        } catch (Throwable ignored) {
        }
        return t("host", "房主");
    }

    private static int readLatency(Object player, Object connection) {
        try {
            Object value = latencyMethod != null ? latencyMethod.invoke(connection)
                : latencyField != null ? latencyField.get(player) : null;
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

    /**
     * The welcome as chat messages: one message of three lines, or three messages in 1.7, whose chat shows
     * line breaks as boxes. 1.7 is the only release without the player list header, which tells it apart.
     */
    private static List<Object> buildWelcome(String host, int count, int ping) throws Exception {
        Object title = component();
        title = add(title, text("Campfire", gold, bold));
        title = add(title, text(" · ", darkGray));
        title = add(title, text(t("welcome", "欢迎加入联机房间"), white));
        Object status = add(component(), status(host, count, ping));
        Object website = add(component(), text(WEBSITE, aqua, bold, underline));
        if (tabConstructor == null) {
            return Arrays.asList(title, status, website);
        }
        Object line = add(title, text("\n", white));
        line = add(line, status);
        line = add(line, text("\n", white));
        line = add(line, website);
        return Collections.singletonList(line);
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
        return empty != null ? empty.invoke(null) : textConstructor.newInstance("");
    }

    private static Object text(String value, Object... styles) throws Exception {
        String content = value == null ? "" : value;
        if (withStyle == null) {
            // 1.8.9: a formatting's text is its section-sign code; a reset first keeps earlier styles from leaking in.
            StringBuilder codes = new StringBuilder("§r");
            for (Object style : styles) {
                codes.append(style);
            }
            content = codes + content;
        }
        Object result = literal != null ? literal.invoke(null, content) : textConstructor.newInstance(content);
        if (styles.length > 0 && withStyle != null) {
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

    private static Object tabPacket(Object header, Object footer) throws Exception {
        if (tabHeader == null) {
            return tabConstructor.newInstance(header, footer);
        }
        Object packet = tabConstructor.newInstance();
        tabHeader.set(packet, header);
        tabFooter.set(packet, footer);
        return packet;
    }

    private static Object chatPacket(Object message) throws Exception {
        Object[] arguments = new Object[chatExtra.length + 1];
        arguments[0] = message;
        System.arraycopy(chatExtra, 0, arguments, 1, chatExtra.length);
        return chatConstructor.newInstance(arguments);
    }

    private static void send(Object connection, Object packet) throws Exception {
        send.invoke(connection, packet);
    }

    public static void log(String message) {
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
