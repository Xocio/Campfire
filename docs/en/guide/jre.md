# JRE Tool

Some launchers mark every Java without `javac.exe`, that is every JRE, as disabled by default, and a JRE newer than Java 8 can never be selected at all. A computer with only a JRE installed then cannot start the game with it. The JRE Tool lifts this restriction so a JRE is available by default, just like a JDK.

The tool is separate from the launcher repair; each can be used on its own and their backups do not interfere.

## How to use it

1. Close the launcher.
2. Choose **JRE Tool** on Campfire's **Tools** tab.
3. Campfire looks for the launcher in its own folder; you can also click **Browse** or drag it into the window.
4. When **Changes** shows **Pending**, click **Allow JRE**.

Afterwards, newly found or imported JREs are enabled by default.

::: warning JREs already marked as disabled
A JRE the launcher had already recorded as disabled before the change is not re-enabled automatically. Enable it once in the launcher's Java list.
:::

## What changes

The tool removes only the two JRE-related conditions:

- Java is no longer disabled by default for lacking `javac.exe`;
- JREs newer than Java 8 are no longer excluded from selection.

The check that the Java architecture must match the system is kept, so a Java that cannot run is never chosen. Only a handful of bytes change; the file size and bundle layout stay the same and nothing is repacked.

## Inspection results

| Message | Meaning |
| --- | --- |
| JREs are disabled by default and can be allowed | Ready to apply |
| JREs can already be used | Already applied |
| This build has no JRE restriction | Nothing to do |
| The selected file is not a supported launcher | That launcher does not have the restriction |
| The code differs from known builds and is not supported yet | The patch point cannot be confirmed; nothing will be changed |

## Backup and restore

The launcher as it was before is kept in the same folder as a `.jre.bak` file, independent of the `.bak` file of the launcher repair. Click **Restore** to bring it back.
