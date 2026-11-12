// Prints the total method reference count of every classes*.dex in an APK.
// Usage: node tools/size/dex-methods.cjs path/to/app.apk
const { execFileSync } = require('child_process')
const path = process.argv[2]
const names = execFileSync('unzip', ['-Z1', path]).toString().split('\n').filter((n) => /^classes\d*\.dex$/.test(n))
let total = 0
for (const name of names) {
  const header = execFileSync('unzip', ['-p', path, name], { maxBuffer: 1 << 28 }).subarray(0, 0x60)
  total += header.readUInt32LE(0x58)
}
console.log(`dex_files=${names.length} method_refs=${total}`)
