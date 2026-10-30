package campfire;

import java.io.BufferedReader;
import java.io.IOException;
import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Paths;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * Finds Minecraft classes and members by their official names, in any release since 1.14.4.
 *
 * <p>Releases up to 1.21.11 ship with obfuscated names; Mojang publishes a mapping file for each
 * of them, which Campfire downloads and passes on. Newer releases, and loaders that run the game
 * with the official names, need no mapping: every lookup first tries the name as it is.</p>
 *
 * <p>Members are matched by name and parameter types, walking up the class hierarchy.</p>
 */
public final class CampfireNames {
    /** Official class name to runtime name, and back. */
    private static final Map<String, String> classes = new HashMap<>();
    private static final Map<String, String> officialOf = new HashMap<>();

    /** Per official class: "name(param,param)" or "name" for fields, to the runtime member name. */
    private static final Map<String, Map<String, String>> members = new HashMap<>();

    private static volatile boolean loaded;

    private CampfireNames() {
    }

    /** Whether a mapping file was loaded, meaning the game runs with obfuscated names. */
    public static boolean hasMappings() {
        return loaded;
    }

    /**
     * Reads a ProGuard mapping file as Mojang publishes it. Only the members whose official name
     * is in {@code memberNames} are kept.
     */
    public static synchronized void load(String path, Set<String> memberNames) throws IOException {
        if (loaded || path == null || path.isEmpty()) {
            return;
        }
        Map<String, String> current = null;
        try (BufferedReader reader = Files.newBufferedReader(Paths.get(path), StandardCharsets.UTF_8)) {
            String line;
            while ((line = reader.readLine()) != null) {
                if (line.isEmpty() || line.charAt(0) == '#') {
                    continue;
                }
                if (line.charAt(0) != ' ') {
                    // "net.minecraft.Foo -> abc:"
                    int arrow = line.indexOf(" -> ");
                    if (arrow < 0 || !line.endsWith(":")) {
                        current = null;
                        continue;
                    }
                    String official = line.substring(0, arrow);
                    String runtime = line.substring(arrow + 4, line.length() - 1);
                    classes.put(official, runtime);
                    officialOf.put(runtime, official);
                    current = new HashMap<>();
                    members.put(official, current);
                    continue;
                }
                if (current == null) {
                    continue;
                }
                // "    12:34:void name(int,java.lang.String) -> a" or "    int field -> b"
                String member = line.trim();
                int arrow = member.indexOf(" -> ");
                if (arrow < 0) {
                    continue;
                }
                String runtime = member.substring(arrow + 4);
                String left = member.substring(0, arrow);
                int space = left.indexOf(' ');
                if (space < 0) {
                    continue;
                }
                String signature = left.substring(space + 1);
                int paren = signature.indexOf('(');
                String name = paren < 0 ? signature : signature.substring(0, paren);
                if (memberNames.contains(name)) {
                    current.put(signature, runtime);
                }
            }
        }
        loaded = true;
    }

    /**
     * The runtime name the mapping gives a member, such as "tickServer(java.util.function.BooleanSupplier)"
     * of "net.minecraft.server.MinecraftServer"; {@code null} without mappings.
     */
    public static String runtimeName(String officialClass, String signature) {
        Map<String, String> table = members.get(officialClass);
        return table == null ? null : table.get(signature);
    }

    /** The runtime class for an official name. */
    public static Class<?> type(String official, ClassLoader loader) throws ClassNotFoundException {
        switch (official) {
            case "boolean": return boolean.class;
            case "byte": return byte.class;
            case "char": return char.class;
            case "short": return short.class;
            case "int": return int.class;
            case "long": return long.class;
            case "float": return float.class;
            case "double": return double.class;
            case "void": return void.class;
            default: break;
        }
        if (official.endsWith("[]")) {
            Class<?> element = type(official.substring(0, official.length() - 2), loader);
            return java.lang.reflect.Array.newInstance(element, 0).getClass();
        }
        try {
            return Class.forName(official, false, loader);
        } catch (ClassNotFoundException missing) {
            String runtime = classes.get(official);
            if (runtime == null) {
                throw missing;
            }
            return Class.forName(runtime, false, loader);
        }
    }

    /** Like {@link #type}, but {@code null} when the class does not exist in this release. */
    public static Class<?> optionalType(String official, ClassLoader loader) {
        try {
            return type(official, loader);
        } catch (ClassNotFoundException | LinkageError missing) {
            return null;
        }
    }

    /**
     * A method by official name and official parameter types, declared on {@code owner} or any of
     * its superclasses and interfaces; {@code null} when there is none.
     */
    public static Method method(Class<?> owner, String name, String... parameters) {
        ClassLoader loader = owner.getClassLoader();
        Class<?>[] types = resolve(parameters, loader);
        if (types == null) {
            return null;
        }
        for (Class<?> type : hierarchy(owner)) {
            for (String candidate : candidates(type, name, parameters)) {
                try {
                    Method method = type.getDeclaredMethod(candidate, types);
                    method.setAccessible(true);
                    return method;
                } catch (NoSuchMethodException | SecurityException ignored) {
                    // Try the next class up.
                }
            }
        }
        return null;
    }

    /** A field by official name, on {@code owner} or a superclass; {@code null} when there is none. */
    public static Field field(Class<?> owner, String name) {
        for (Class<?> type : hierarchy(owner)) {
            for (String candidate : candidates(type, name, null)) {
                try {
                    Field field = type.getDeclaredField(candidate);
                    field.setAccessible(true);
                    return field;
                } catch (NoSuchFieldException | SecurityException ignored) {
                    // Try the next class up.
                }
            }
        }
        return null;
    }

    /** A constructor by official parameter types; {@code null} when there is none. */
    public static Constructor<?> constructor(Class<?> owner, String... parameters) {
        Class<?>[] types = resolve(parameters, owner.getClassLoader());
        if (types == null) {
            return null;
        }
        try {
            Constructor<?> constructor = owner.getDeclaredConstructor(types);
            constructor.setAccessible(true);
            return constructor;
        } catch (NoSuchMethodException | SecurityException missing) {
            return null;
        }
    }

    /** The value of a static field, such as an enum constant, by official name. */
    public static Object constant(Class<?> owner, String name) throws IllegalAccessException {
        Field field = field(owner, name);
        return field == null ? null : field.get(null);
    }

    /** The official name, then the runtime name the mapping gives for this member of {@code type}. */
    private static List<String> candidates(Class<?> type, String name, String[] parameters) {
        List<String> names = new ArrayList<>(2);
        names.add(name);
        String official = officialOf.getOrDefault(type.getName(), type.getName());
        Map<String, String> table = members.get(official);
        if (table != null) {
            String key = parameters == null ? name : name + "(" + String.join(",", parameters) + ")";
            String runtime = table.get(key);
            if (runtime != null && !runtime.equals(name)) {
                names.add(runtime);
            }
        }
        return names;
    }

    private static Class<?>[] resolve(String[] parameters, ClassLoader loader) {
        Class<?>[] types = new Class<?>[parameters.length];
        for (int i = 0; i < parameters.length; i++) {
            try {
                types[i] = type(parameters[i], loader);
            } catch (ClassNotFoundException | LinkageError missing) {
                return null;
            }
        }
        return types;
    }

    private static List<Class<?>> hierarchy(Class<?> owner) {
        List<Class<?>> result = new ArrayList<>();
        Set<Class<?>> seen = new HashSet<>();
        List<Class<?>> pending = new ArrayList<>(Arrays.asList(owner));
        while (!pending.isEmpty()) {
            Class<?> type = pending.remove(0);
            if (type == null || !seen.add(type)) {
                continue;
            }
            result.add(type);
            pending.add(type.getSuperclass());
            pending.addAll(Arrays.asList(type.getInterfaces()));
        }
        return result;
    }
}
