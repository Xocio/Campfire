import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
import java.util.function.Predicate;
import java.util.jar.*;
import java.util.stream.*;
import org.objectweb.asm.*;
import org.objectweb.asm.tree.*;

/**
 * Dev tool: finds the names the Campfire agent needs in an obfuscated client jar Mojang published no mappings
 * for, from the bytecode alone, and writes them as a ProGuard-style table keyed by today's official names.
 * Every lookup must match exactly one candidate; otherwise the tool stops.
 *
 * args: <client.jar> <version> <client sha1> <output table>
 */
public class NameFinder {
    static final Map<String, ClassNode> classes = new HashMap<>();
    static final List<String> lines = new ArrayList<>();
    static final List<String> notes = new ArrayList<>();

    public static void main(String[] args) throws Exception {
        try (JarFile jar = new JarFile(args[0])) {
            for (JarEntry e : Collections.list(jar.entries())) {
                if (!e.getName().endsWith(".class")) continue;
                ClassNode node = new ClassNode();
                try (InputStream in = jar.getInputStream(e)) {
                    new ClassReader(in).accept(node, ClassReader.SKIP_FRAMES);
                }
                classes.put(node.name, node);
            }
        }
        String version = args[1];

        // ---- server ----
        ClassNode server = need(classes.get("net/minecraft/server/MinecraftServer"), "MinecraftServer");
        MethodNode tick = one(server.methods, m -> hasLdc(m, "tallying"), "tick");
        ClassNode integrated = one(classes.values(), c -> server.name.equals(c.superName) && hasLdcAnywhere(c, "Starting integrated minecraft server"), "IntegratedServer");
        MethodNode share = one(integrated.methods, m -> hasLdcPrefix(m, "Started on") || hasLdcPrefix(m, "Started serving on"), "shareToLAN");
        Type[] shareArgs = Type.getArgumentTypes(share.desc);
        String gameType = shareArgs[0].getInternalName();
        boolean shareReturnsPort = Type.getReturnType(share.desc).equals(Type.getType(String.class));

        FieldInsnNode publishedFlag = null;
        for (AbstractInsnNode i : share.instructions) {
            if (i.getOpcode() == Opcodes.PUTFIELD && ((FieldInsnNode) i).desc.equals("Z") && i.getPrevious() != null && i.getPrevious().getOpcode() == Opcodes.ICONST_1) {
                publishedFlag = (FieldInsnNode) i;
            }
        }
        MethodNode isPublished = null;
        if (publishedFlag != null) {
            final FieldInsnNode flag = publishedFlag;
            isPublished = optional(integrated.methods, m -> m.desc.equals("()Z") && returnsField(m, flag.name), "isPublished");
        }
        MethodNode defaultGameType = oneOf(server.methods, m -> m.desc.equals("()L" + gameType + ";"), m -> (m.access & Opcodes.ACC_ABSTRACT) != 0, "getDefaultGameType");

        // Owner and folder name: the integrated server's constructor sets both.
        String ownerField = null, folderField = null;
        for (MethodNode init : integrated.methods) {
            if (!init.name.equals("<init>")) continue;
            for (AbstractInsnNode i : init.instructions) {
                if (i.getOpcode() != Opcodes.INVOKEVIRTUAL) continue;
                MethodInsnNode call = (MethodInsnNode) i;
                if (!call.desc.equals("(Ljava/lang/String;)V") || !(call.owner.equals(integrated.name) || call.owner.equals(server.name))) continue;
                MethodNode setter = find(server, call.name, call.desc);
                if (setter == null) continue;
                String field = storedField(setter);
                AbstractInsnNode previous = real(i.getPrevious());
                if (previous instanceof MethodInsnNode && Type.getReturnType(((MethodInsnNode) previous).desc).equals(Type.getType(String.class)) && ownerField == null) {
                    ownerField = field;
                } else if (previous instanceof VarInsnNode && ((VarInsnNode) previous).var == 2 && folderField == null) {
                    folderField = field;
                }
            }
        }
        if (folderField == null) {
            // 1.14: the folder name is the last argument of MinecraftServer's own constructor.
            for (MethodNode init : server.methods) {
                if (!init.name.equals("<init>")) continue;
                Type[] params = Type.getArgumentTypes(init.desc);
                if (params.length == 0 || !params[params.length - 1].equals(Type.getType(String.class))) continue;
                int slot = 1;
                for (int p = 0; p < params.length - 1; p++) slot += params[p].getSize();
                for (AbstractInsnNode i : init.instructions) {
                    if (i.getOpcode() == Opcodes.PUTFIELD && ((FieldInsnNode) i).desc.equals("Ljava/lang/String;")
                            && real(i.getPrevious()) instanceof VarInsnNode && ((VarInsnNode) real(i.getPrevious())).var == slot) {
                        folderField = ((FieldInsnNode) i).name;
                    }
                }
            }
        }
        need(ownerField, "owner field");
        need(folderField, "folder field");
        final String ownerName = ownerField, folderName = folderField;
        MethodNode ownerGetter = one(server.methods, m -> m.desc.equals("()Ljava/lang/String;") && returnsField(m, ownerName), "getSingleplayerName");
        MethodNode folderGetter = one(server.methods, m -> m.desc.equals("()Ljava/lang/String;") && returnsField(m, folderName), "getLevelIdName");

        // ---- players ----
        ClassNode playerList = one(classes.values(), c -> hasLdcAnywhere(c, "multiplayer.player.joined"), "PlayerList");
        MethodNode getPlayerList = one(server.methods, m -> m.desc.equals("()L" + playerList.name + ";"), "getPlayerList");
        ClassNode serverPlayer = one(classes.values(), c -> hasLdcAnywhere(c, "playerGameType") && !isSuperOfAny(c), "ServerPlayer");
        FieldNode playersField = optional(playerList.fields, f -> f.desc.equals("Ljava/util/List;") && f.signature != null && f.signature.contains("L" + serverPlayer.name + ";"), "players field");
        if (playersField == null) {
            // Up to 1.8.1 the field has no generic signature; it is the only list the class keeps.
            playersField = one(playerList.fields, f -> f.desc.equals("Ljava/util/List;") && (f.access & Opcodes.ACC_STATIC) == 0, "players field");
        }
        final FieldNode players = playersField;
        MethodNode getPlayers = optional(playerList.methods, m -> m.desc.equals("()Ljava/util/List;") && returnsField(m, players.name), "getPlayers");

        ClassNode entity = serverPlayer;
        while (classes.containsKey(entity.superName)) entity = classes.get(entity.superName);
        MethodNode getUuid = oneOf(entity.methods, m -> m.desc.equals("()Ljava/util/UUID;") && (m.access & Opcodes.ACC_STATIC) == 0, m -> m.instructions.size() < 8, "getUUID");

        // Packet: among the one-argument void methods of the player's field types, the parameter type with by far
        // the most subtypes. It is an interface since 1.8 and an abstract class before.
        Map<String, Integer> counts = new HashMap<>();
        for (ClassNode c : classes.values()) {
            for (String i : c.interfaces) counts.merge(i, 1, Integer::sum);
            counts.merge(c.superName, 1, Integer::sum);
        }
        String packet = null;
        int best = 0;
        for (FieldNode f : serverPlayer.fields) {
            if (!f.desc.startsWith("L") || !classes.containsKey(Type.getType(f.desc).getInternalName())) continue;
            for (MethodNode m : classes.get(Type.getType(f.desc).getInternalName()).methods) {
                Type[] p = Type.getArgumentTypes(m.desc);
                if (p.length != 1 || p[0].getSort() != Type.OBJECT || !Type.getReturnType(m.desc).equals(Type.VOID_TYPE)) continue;
                int n = counts.getOrDefault(p[0].getInternalName(), 0);
                if (n > best) { best = n; packet = p[0].getInternalName(); }
            }
        }
        if (best < 50) throw new IllegalStateException("packet type not found");
        final String packetType = packet;

        FieldNode connection = one(serverPlayer.fields, f -> f.desc.startsWith("L") && hasSender(f.desc, packetType), "connection");
        ClassNode listener = classes.get(Type.getType(connection.desc).getInternalName());
        MethodNode send = one(listener.methods, m -> m.desc.equals("(L" + packetType + ";)V") && (m.access & Opcodes.ACC_STATIC) == 0, "send");
        String latency = null;
        for (MethodNode m : listener.methods) {
            for (AbstractInsnNode i : m.instructions) {
                if (i.getOpcode() == Opcodes.PUTFIELD && ((FieldInsnNode) i).owner.equals(serverPlayer.name) && ((FieldInsnNode) i).desc.equals("I")) {
                    AbstractInsnNode a = real(i.getPrevious()), b = a == null ? null : real(a.getPrevious());
                    if (a != null && a.getOpcode() == Opcodes.IDIV && b != null && b.getOpcode() == Opcodes.ICONST_4) latency = ((FieldInsnNode) i).name;
                }
            }
        }
        need(latency, "latency");

        // ---- text ----
        ClassNode text = one(classes.values(), c -> hasLdcAnywhere(c, "TextComponent{text="), "TextComponent");
        String component = null;
        for (ClassNode c = text; c != null && component == null; c = classes.get(c.superName))
            for (String i : c.interfaces) if (classes.containsKey(i) && (classes.get(i).access & Opcodes.ACC_INTERFACE) != 0) component = i;
        need(component, "Component");
        ClassNode componentNode = classes.get(component);
        final String comp = component;
        MethodNode append = oneOf(componentNode.methods, m -> m.desc.equals("(L" + comp + ";)L" + comp + ";"), m -> calledFrom(componentNode, "(Ljava/lang/String;)L" + comp + ";", m.name, m.desc), "append");
        need(text.methods.stream().anyMatch(m -> m.name.equals("<init>") && m.desc.equals("(Ljava/lang/String;)V")) ? "" : null, "TextComponent(String)");
        ClassNode formatting = one(classes.values(), c -> (c.access & Opcodes.ACC_ENUM) != 0 && hasLdcAnywhere(c, "DARK_PURPLE") && hasLdcAnywhere(c, "LIGHT_PURPLE"), "ChatFormatting");
        MethodNode byName = oneOf(formatting.methods, m -> (m.access & Opcodes.ACC_STATIC) != 0 && m.desc.equals("(Ljava/lang/String;)L" + formatting.name + ";"), m -> !m.name.equals("valueOf"), "getByName");

        // ---- packets ----
        List<ClassNode> packets = classes.values().stream().filter(c -> c.interfaces.contains(packetType) || packetType.equals(c.superName)).collect(Collectors.toList());
        ClassNode chat = one(packets, c -> isChatPacket(c, comp), "chat packet");
        ClassNode tab = optional(packets, c -> c.fields.stream().filter(f -> (f.access & Opcodes.ACC_STATIC) == 0).count() == 2
                && c.fields.stream().filter(f -> (f.access & Opcodes.ACC_STATIC) == 0).allMatch(f -> f.desc.equals("L" + comp + ";")), "tab packet");

        // ---- cheats setting of the world ----
        ClassNode levelData = optional(classes.values(), c -> hasLdcAnywhere(c, "allowCommands") && c.methods.stream().anyMatch(m -> m.desc.equals("()Z") && returnsField(m, allowField(c))), "LevelData");
        String levelDataLine = null, levelLine = null, getLevelLine = null, dimensionLines = null;
        if (levelData != null) {
            String allow = allowField(levelData);
            MethodNode getAllow = one(levelData.methods, m -> m.desc.equals("()Z") && returnsField(m, allow), "getAllowCommands");
            levelDataLine = "    boolean getAllowCommands() -> " + getAllow.name;
            // World: the class with a no-argument getter of the level data that a server world extends.
            for (MethodNode m : server.methods) {
                Type[] p = Type.getArgumentTypes(m.desc);
                Type r = Type.getReturnType(m.desc);
                if (p.length != 1 || r.getSort() != Type.OBJECT || !classes.containsKey(r.getInternalName())) continue;
                ClassNode world = classes.get(classes.get(r.getInternalName()).superName);
                if (world == null) continue;
                MethodNode getData = world.methods.stream().filter(x -> x.desc.equals("()L" + levelData.name + ";")).findFirst().orElse(null);
                if (getData == null) continue;
                if (p[0].equals(Type.INT_TYPE)) {
                    getLevelLine = "    net.minecraft.server.level.ServerLevel getLevel(int) -> " + m.name;
                } else if (p[0].getSort() == Type.OBJECT && classes.containsKey(p[0].getInternalName())) {
                    ClassNode dim = classes.get(p[0].getInternalName());
                    FieldNode overworld = dim.fields.stream().filter(f -> (f.access & Opcodes.ACC_STATIC) != 0 && f.desc.equals("L" + dim.name + ";")).findFirst().orElse(null);
                    if (overworld == null) continue;
                    getLevelLine = "    net.minecraft.server.level.ServerLevel getLevel(net.minecraft.world.level.dimension.DimensionType) -> " + m.name;
                    dimensionLines = "net.minecraft.world.level.dimension.DimensionType -> " + dim.name.replace('/', '.') + ":\n    net.minecraft.world.level.dimension.DimensionType OVERWORLD -> " + overworld.name;
                } else continue;
                levelLine = "net.minecraft.server.level.ServerLevel -> " + r.getInternalName().replace('/', '.') + ":\n"
                        + "net.minecraft.world.level.Level -> " + world.name.replace('/', '.') + ":\n"
                        + "    net.minecraft.world.level.storage.LevelData getLevelData() -> " + getData.name;
                break;
            }
        }
        if (getLevelLine == null) notes.add("cheat setting not found; LAN opens with cheats off");

        // A free port, for releases where the LAN screen picks it (1.13 and later).
        String portLines = null;
        if (!shareReturnsPort) {
            ClassNode http = one(classes.values(), c -> c.methods.stream().anyMatch(m -> (m.access & Opcodes.ACC_STATIC) != 0 && m.desc.equals("()I") && creates(m, "java/net/ServerSocket")), "HttpUtil");
            MethodNode port = one(http.methods, m -> (m.access & Opcodes.ACC_STATIC) != 0 && m.desc.equals("()I") && creates(m, "java/net/ServerSocket"), "getAvailablePort");
            portLines = "net.minecraft.util.HttpUtil -> " + dot(http.name) + ":\n    int getAvailablePort() -> " + port.name;
        }

        // ---- write ----
        lines.add("# Names the in-game agent needs in the Minecraft " + version + " client jar (SHA-1 " + args[2] + ").");
        lines.add("# client " + args[2]);
        lines.add("# Mojang published no mappings for this release; these were found by reading the jar's own bytecode.");
        lines.add("# Keys are the official names of releases since 1.14.4, so the agent looks everything up the same way.");
        lines.add("net.minecraft.server.MinecraftServer -> net.minecraft.server.MinecraftServer:");
        lines.add(tick.desc.equals("()V") ? "    void tickServer() -> " + tick.name : "    void tickServer(java.util.function.BooleanSupplier) -> " + tick.name);
        lines.add("    net.minecraft.server.players.PlayerList getPlayerList() -> " + getPlayerList.name);
        lines.add("    java.lang.String getSingleplayerName() -> " + ownerGetter.name);
        lines.add("    java.lang.String getLevelIdName() -> " + folderGetter.name);
        lines.add("    net.minecraft.world.level.GameType getDefaultGameType() -> " + defaultGameType.name);
        if (getLevelLine != null) lines.add(getLevelLine);
        lines.add("net.minecraft.client.server.IntegratedServer -> " + dot(integrated.name) + ":");
        lines.add(shareReturnsPort
                ? "    java.lang.String publishServer(net.minecraft.world.level.GameType,boolean) -> " + share.name
                : "    boolean publishServer(net.minecraft.world.level.GameType,boolean,int) -> " + share.name);
        if (isPublished != null) lines.add("    boolean isPublished() -> " + isPublished.name);
        lines.add("net.minecraft.server.players.PlayerList -> " + dot(playerList.name) + ":");
        lines.add(getPlayers != null ? "    java.util.List getPlayers() -> " + getPlayers.name : "    java.util.List players -> " + players.name);
        lines.add("net.minecraft.server.level.ServerPlayer -> " + dot(serverPlayer.name) + ":");
        lines.add("    net.minecraft.server.network.ServerGamePacketListenerImpl connection -> " + connection.name);
        lines.add("    int latency -> " + latency);
        lines.add("net.minecraft.world.entity.Entity -> " + dot(entity.name) + ":");
        lines.add("    java.util.UUID getUUID() -> " + getUuid.name);
        lines.add("net.minecraft.server.network.ServerGamePacketListenerImpl -> " + dot(listener.name) + ":");
        lines.add("    void send(net.minecraft.network.protocol.Packet) -> " + send.name);
        lines.add("net.minecraft.network.protocol.Packet -> " + dot(packet) + ":");
        lines.add("net.minecraft.network.chat.Component -> " + dot(component) + ":");
        lines.add("    net.minecraft.network.chat.Component append(net.minecraft.network.chat.Component) -> " + append.name);
        lines.add("net.minecraft.network.chat.TextComponent -> " + dot(text.name) + ":");
        lines.add("net.minecraft.ChatFormatting -> " + dot(formatting.name) + ":");
        lines.add("    net.minecraft.ChatFormatting getByName(java.lang.String) -> " + byName.name);
        lines.add("net.minecraft.network.protocol.game.ClientboundChatPacket -> " + dot(chat.name) + ":");
        if (tab != null) {
            List<FieldNode> f = tab.fields.stream().filter(x -> (x.access & Opcodes.ACC_STATIC) == 0).collect(Collectors.toList());
            lines.add("net.minecraft.network.protocol.game.ClientboundTabListPacket -> " + dot(tab.name) + ":");
            lines.add("    net.minecraft.network.chat.Component header -> " + f.get(0).name);
            lines.add("    net.minecraft.network.chat.Component footer -> " + f.get(1).name);
        } else {
            notes.add("no tab list packet; the room shows in chat only");
        }
        lines.add("net.minecraft.world.level.GameType -> " + dot(gameType) + ":");
        if (levelLine != null) {
            lines.add(levelLine);
            lines.add("net.minecraft.world.level.storage.LevelData -> " + dot(levelData.name) + ":");
            lines.add(levelDataLine);
        }
        if (dimensionLines != null) lines.add(dimensionLines);
        if (portLines != null) lines.add(portLines);
        Files.write(Paths.get(args[3]), lines, StandardCharsets.UTF_8);
        System.out.println(version + ": OK" + (notes.isEmpty() ? "" : "  (" + String.join("; ", notes) + ")"));
    }

    // ---- helpers ----

    /** Whether a method of {@code c} with descriptor {@code callerDesc} calls {@code name}{@code desc}. */
    static boolean calledFrom(ClassNode c, String callerDesc, String name, String desc) {
        for (MethodNode m : c.methods)
            if (m.desc.equals(callerDesc))
                for (AbstractInsnNode i : m.instructions)
                    if (i instanceof MethodInsnNode && ((MethodInsnNode) i).name.equals(name) && ((MethodInsnNode) i).desc.equals(desc)) return true;
        return false;
    }

    static boolean isChatPacket(ClassNode c, String comp) {
        List<FieldNode> f = c.fields.stream().filter(x -> (x.access & Opcodes.ACC_STATIC) == 0).collect(Collectors.toList());
        long components = f.stream().filter(x -> x.desc.equals("L" + comp + ";")).count();
        if (components != 1) return false;
        boolean oneArg = c.methods.stream().anyMatch(m -> m.name.equals("<init>") && m.desc.equals("(L" + comp + ";)V"));
        if (!oneArg) return false;
        // Besides the text: the message type, as a byte, a boolean or a small enum.
        return f.size() == 2 && f.stream().anyMatch(x -> x.desc.equals("B") || x.desc.equals("Z") || isSmallEnum(x.desc));
    }

    static boolean isSmallEnum(String desc) {
        if (!desc.startsWith("L")) return false;
        ClassNode e = classes.get(Type.getType(desc).getInternalName());
        return e != null && (e.access & Opcodes.ACC_ENUM) != 0 && e.fields.stream().filter(f -> (f.access & Opcodes.ACC_ENUM) != 0).count() == 3;
    }

    static String allowField(ClassNode c) {
        for (MethodNode m : c.methods)
            for (AbstractInsnNode i : m.instructions)
                if (i instanceof LdcInsnNode && "allowCommands".equals(((LdcInsnNode) i).cst))
                    for (AbstractInsnNode j = i.getNext(); j != null; j = j.getNext()) {
                        if (j.getOpcode() == Opcodes.PUTFIELD && ((FieldInsnNode) j).desc.equals("Z") && ((FieldInsnNode) j).owner.equals(c.name)) return ((FieldInsnNode) j).name;
                        if (j instanceof LdcInsnNode) break;
                    }
        return "\0";
    }

    static boolean hasSender(String desc, String packet) {
        ClassNode t = classes.get(Type.getType(desc).getInternalName());
        return t != null && t.methods.stream().anyMatch(m -> m.desc.equals("(L" + packet + ";)V") && (m.access & Opcodes.ACC_STATIC) == 0);
    }

    static boolean isSuperOfAny(ClassNode c) {
        return classes.values().stream().anyMatch(x -> c.name.equals(x.superName));
    }

    static boolean creates(MethodNode m, String type) {
        for (AbstractInsnNode i : m.instructions) if (i.getOpcode() == Opcodes.NEW && ((TypeInsnNode) i).desc.equals(type)) return true;
        return false;
    }

    static boolean hasLdc(MethodNode m, String s) {
        for (AbstractInsnNode i : m.instructions) if (i instanceof LdcInsnNode && s.equals(((LdcInsnNode) i).cst)) return true;
        return false;
    }

    static boolean hasLdcPrefix(MethodNode m, String s) {
        for (AbstractInsnNode i : m.instructions) if (i instanceof LdcInsnNode && ((LdcInsnNode) i).cst instanceof String && ((String) ((LdcInsnNode) i).cst).startsWith(s)) return true;
        // Since 1.14 the message is built by string concatenation through invokedynamic.
        for (AbstractInsnNode i : m.instructions)
            if (i instanceof InvokeDynamicInsnNode) for (Object a : ((InvokeDynamicInsnNode) i).bsmArgs) if (a instanceof String && ((String) a).startsWith(s)) return true;
        return false;
    }

    static boolean hasLdcAnywhere(ClassNode c, String s) {
        for (MethodNode m : c.methods) if (hasLdc(m, s) || hasLdcPrefix(m, s)) return true;
        return false;
    }

    static boolean returnsField(MethodNode m, String field) {
        for (AbstractInsnNode i : m.instructions)
            if (i.getOpcode() == Opcodes.GETFIELD && ((FieldInsnNode) i).name.equals(field)) {
                AbstractInsnNode n = real(i.getNext());
                return n != null && (n.getOpcode() == Opcodes.ARETURN || n.getOpcode() == Opcodes.IRETURN);
            }
        return false;
    }

    static String storedField(MethodNode setter) {
        for (AbstractInsnNode i : setter.instructions) if (i.getOpcode() == Opcodes.PUTFIELD) return ((FieldInsnNode) i).name;
        return null;
    }

    static MethodNode find(ClassNode c, String name, String desc) {
        for (MethodNode m : c.methods) if (m.name.equals(name) && m.desc.equals(desc)) return m;
        return null;
    }

    static AbstractInsnNode real(AbstractInsnNode i) {
        while (i != null && i.getOpcode() < 0) i = i.getPrevious();
        return i;
    }

    static String dot(String internal) {
        return internal.replace('/', '.');
    }

    static <T> T need(T value, String what) {
        if (value == null) throw new IllegalStateException("not found: " + what);
        return value;
    }

    static <T> T one(Collection<T> items, Predicate<T> test, String what) {
        List<T> found = items.stream().filter(test).collect(Collectors.toList());
        if (found.size() != 1) throw new IllegalStateException(what + ": " + found.size() + " candidates");
        return found.get(0);
    }

    static <T> T optional(Collection<T> items, Predicate<T> test, String what) {
        List<T> found = items.stream().filter(test).collect(Collectors.toList());
        if (found.size() > 1) throw new IllegalStateException(what + ": " + found.size() + " candidates");
        return found.isEmpty() ? null : found.get(0);
    }

    /** One match, narrowed by a tie-breaker when the first test finds several. */
    static <T> T oneOf(Collection<T> items, Predicate<T> test, Predicate<T> prefer, String what) {
        List<T> found = items.stream().filter(test).collect(Collectors.toList());
        if (found.size() > 1) found = found.stream().filter(prefer).collect(Collectors.toList());
        if (found.size() != 1) throw new IllegalStateException(what + ": " + found.size() + " candidates");
        return found.get(0);
    }
}
