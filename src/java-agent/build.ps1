# Builds the in-game overlay agent and the attach bridge embedded in Campfire.exe.
#
#   powershell -File build.ps1
#
# Requires a JDK 21 or newer (javac and jar on PATH). ASM is kept in java-agent/lib.

param(
    [string]$AsmJar = (Join-Path $PSScriptRoot 'lib\asm-9.11.1.jar'),
    [string]$Output = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command javac -ErrorAction SilentlyContinue)) {
    throw 'javac was not found on PATH. Install a JDK 21 or newer.'
}
if (-not (Test-Path $AsmJar)) {
    throw "ASM was not found at $AsmJar."
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('campfire-agent-' + [Guid]::NewGuid().ToString('N'))
$hookClasses = Join-Path $work 'hook'
$agentClasses = Join-Path $work 'agent'
$attachClasses = Join-Path $work 'attach'
New-Item -ItemType Directory -Force $hookClasses, $agentClasses, $attachClasses, $Output | Out-Null

try {
    $source = Join-Path $PSScriptRoot 'src'

    # The hook and the agent run inside the game, which is Java 8 up to Minecraft 1.16; the attach
    # bridge runs on a Java 9 or later that has the attach module.
    & javac -encoding UTF-8 --release 8 -Xlint:-options -d $hookClasses `
        (Join-Path $source 'CampfireHook.java') (Join-Path $source 'CampfireNames.java')
    if ($LASTEXITCODE -ne 0) { throw 'The hook classes did not compile.' }

    # The agent calls into the hook, which it finds on the bootstrap class path at run time.
    & javac -encoding UTF-8 --release 8 -Xlint:-options -cp "$AsmJar;$hookClasses" -d $agentClasses `
        (Join-Path $source 'CampfireTransformer.java') (Join-Path $source 'CampfireAgent.java')
    if ($LASTEXITCODE -ne 0) { throw 'The agent classes did not compile.' }

    & javac -encoding UTF-8 --release 9 --add-modules jdk.attach -d $attachClasses (Join-Path $source 'CampfireAttach.java')
    if ($LASTEXITCODE -ne 0) { throw 'CampfireAttach.java did not compile.' }

    # Shade ASM into the agent jar; the hook jar holds no third-party classes.
    Push-Location $agentClasses
    try {
        & jar xf $AsmJar
        Remove-Item -Force 'module-info.class' -ErrorAction SilentlyContinue
    }
    finally {
        Pop-Location
    }

    $agentManifest = Join-Path $work 'agent.mf'
    @('Manifest-Version: 1.0', 'Agent-Class: campfire.CampfireAgent', 'Can-Retransform-Classes: true',
      'Can-Redefine-Classes: true', '') | Set-Content -Path $agentManifest -Encoding ascii

    $attachManifest = Join-Path $work 'attach.mf'
    @('Manifest-Version: 1.0', 'Main-Class: campfire.CampfireAttach', '') |
        Set-Content -Path $attachManifest -Encoding ascii

    & jar --create --file (Join-Path $Output 'CampfireAgent.jar') --manifest $agentManifest -C $agentClasses .
    if ($LASTEXITCODE -ne 0) { throw 'CampfireAgent.jar could not be created.' }

    & jar --create --file (Join-Path $Output 'CampfireHook.jar') -C $hookClasses .
    if ($LASTEXITCODE -ne 0) { throw 'CampfireHook.jar could not be created.' }

    & jar --create --file (Join-Path $Output 'CampfireAttach.jar') --manifest $attachManifest -C $attachClasses .
    if ($LASTEXITCODE -ne 0) { throw 'CampfireAttach.jar could not be created.' }

    Get-ChildItem $Output |
        Select-Object Name, Length |
        Format-Table -AutoSize
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
