// Compares the default release APK with the single-style builds and fails when a single-style build is not smaller.
// Usage: node tools/size/styles.cjs default.apk miuix.apk material.apk
const fs = require('fs')
const path = require('path')
const { execFileSync } = require('child_process')

const [defaultApk, miuixApk, materialApk] = process.argv.slice(2)
if (!materialApk) {
  console.error('usage: styles.cjs default.apk miuix.apk material.apk')
  process.exit(2)
}

function measure(apk) {
  const out = execFileSync('node', [path.join(__dirname, 'dex-methods.cjs'), apk]).toString()
  return { bytes: fs.statSync(apk).size, methods: Number(/method_refs=(\d+)/.exec(out)[1]) }
}

const base = measure(defaultApk)
console.log(`default   bytes ${base.bytes}  method refs ${base.methods}`)
let failed = false
for (const [name, apk] of [['miuix', miuixApk], ['material', materialApk]]) {
  const m = measure(apk)
  const bytes = m.bytes - base.bytes
  const methods = m.methods - base.methods
  const ok = m.bytes < base.bytes && m.methods < base.methods
  failed ||= !ok
  console.log(
    `${ok ? 'ok  ' : 'FAIL'} ${name.padEnd(8)} bytes ${m.bytes} (${bytes}, ${(bytes / base.bytes * 100).toFixed(1)}%)  method refs ${m.methods} (${methods})`,
  )
}
process.exit(failed ? 1 : 0)
