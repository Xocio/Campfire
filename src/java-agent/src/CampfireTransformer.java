package campfire;

import java.lang.instrument.ClassFileTransformer;
import java.security.ProtectionDomain;

import org.objectweb.asm.ClassReader;
import org.objectweb.asm.ClassVisitor;
import org.objectweb.asm.ClassWriter;
import org.objectweb.asm.MethodVisitor;
import org.objectweb.asm.Opcodes;

/**
 * Adds a single static call to the start of the server tick. The call has no effect if the hook
 * cannot find the classes it needs; it never changes authentication, encryption or world state.
 */
public final class CampfireTransformer implements ClassFileTransformer {
    // The integrated server of a LAN world inherits this method, so one hook covers both kinds of server.
    // The class keeps its name even in obfuscated releases; only the method is renamed there.
    private static final String MINECRAFT_SERVER = "net/minecraft/server/MinecraftServer";
    private static final String TICK_NAME = "tickServer";
    static final String TICK_DESCRIPTOR = "(Ljava/util/function/BooleanSupplier;)V";

    /** The obfuscated name of the tick method, or {@code null} when the game runs with official names. */
    private final String obfuscatedTick;
    private final String obfuscatedDescriptor;

    public CampfireTransformer(String obfuscatedTick, String obfuscatedDescriptor) {
        this.obfuscatedTick = obfuscatedTick;
        this.obfuscatedDescriptor = obfuscatedDescriptor;
    }

    @Override
    public byte[] transform(ClassLoader loader, String className, Class<?> classBeingRedefined,
                            ProtectionDomain protectionDomain, byte[] classfileBuffer) {
        if (!MINECRAFT_SERVER.equals(className)) {
            return null;
        }

        try {
            // A game that runs with official names, such as one with a loader that remaps it, has
            // tickServer even when Campfire passed the mappings of its release.
            ClassReader reader = new ClassReader(classfileBuffer);
            boolean official = hasMethod(reader, TICK_NAME);
            String target = official ? TICK_NAME : obfuscatedTick;
            String descriptor = official ? TICK_DESCRIPTOR : obfuscatedDescriptor;
            if (target == null) {
                return null;
            }

            ClassWriter writer = new ClassWriter(reader, ClassWriter.COMPUTE_MAXS);
            ClassVisitor visitor = new ClassVisitor(Opcodes.ASM9, writer) {
                @Override
                public MethodVisitor visitMethod(int access, String name, String methodDescriptor,
                                                 String signature, String[] exceptions) {
                    MethodVisitor method = super.visitMethod(access, name, methodDescriptor, signature, exceptions);
                    if (method == null || !target.equals(name) || !descriptor.equals(methodDescriptor)) {
                        return method;
                    }
                    return new MethodVisitor(Opcodes.ASM9, method) {
                        @Override
                        public void visitCode() {
                            super.visitCode();
                            method.visitVarInsn(Opcodes.ALOAD, 0);
                            method.visitMethodInsn(Opcodes.INVOKESTATIC, "campfire/CampfireHook", "tick",
                                "(Ljava/lang/Object;)V", false);
                        }
                    };
                }
            };
            reader.accept(visitor, 0);
            return writer.toByteArray();
        } catch (Throwable error) {
            System.err.println("[Campfire] Could not transform " + className + ": " + error);
            return null;
        }
    }

    private static boolean hasMethod(ClassReader reader, String wanted) {
        boolean[] found = new boolean[1];
        reader.accept(new ClassVisitor(Opcodes.ASM9) {
            @Override
            public MethodVisitor visitMethod(int access, String name, String descriptor,
                                             String signature, String[] exceptions) {
                if (wanted.equals(name) && TICK_DESCRIPTOR.equals(descriptor)) {
                    found[0] = true;
                }
                return null;
            }
        }, ClassReader.SKIP_CODE | ClassReader.SKIP_DEBUG | ClassReader.SKIP_FRAMES);
        return found[0];
    }
}
