# Project-specific R8 rules.

# Moves every class into one package and widens access for inlining and merging.
-repackageclasses
-allowaccessmodification

# Removes verbose and debug log calls.
-assumenosideeffects class android.util.Log {
    public static int v(...);
    public static int d(...);
}

# Removes Kotlin null checks on parameters and platform values.
-assumenosideeffects class kotlin.jvm.internal.Intrinsics {
    public static void checkNotNull(...);
    public static void checkNotNullParameter(...);
    public static void checkParameterIsNotNull(...);
    public static void checkNotNullExpressionValue(...);
    public static void checkExpressionValueIsNotNull(...);
    public static void checkReturnedValueIsNotNull(...);
    public static void checkFieldIsNotNull(...);
}
