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
    private static final String MINECRAFT_SERVER = "net/minecraft/server/MinecraftServer";
    private static final String TICK_DESCRIPTOR = "(Ljava/util/function/BooleanSupplier;)V";

    @Override
    public byte[] transform(ClassLoader loader, String className, Class<?> classBeingRedefined,
                            ProtectionDomain protectionDomain, byte[] classfileBuffer) {
        if (!MINECRAFT_SERVER.equals(className)) {
            return null;
        }

        try {
            ClassReader reader = new ClassReader(classfileBuffer);
            ClassWriter writer = new ClassWriter(reader, ClassWriter.COMPUTE_MAXS);
            ClassVisitor visitor = new ClassVisitor(Opcodes.ASM9, writer) {
                @Override
                public MethodVisitor visitMethod(int access, String name, String descriptor,
                                                 String signature, String[] exceptions) {
                    MethodVisitor method = super.visitMethod(access, name, descriptor, signature, exceptions);
                    if (method == null || !"tickServer".equals(name) || !TICK_DESCRIPTOR.equals(descriptor)) {
                        return method;
                    }
                    return new MethodVisitor(Opcodes.ASM9, method) {
                        @Override
                        public void visitCode() {
                            super.visitCode();
                            method.visitVarInsn(Opcodes.ALOAD, 0);
                            method.visitMethodInsn(Opcodes.INVOKESTATIC, "CampfireHook", "tick",
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
}
