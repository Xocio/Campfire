// Fails when the unsigned release APK exceeds the limits in tools/size/limits.json.
// Usage: node tools/size/check.cjs [path/to/app-release-unsigned.apk]
const fs = require('fs')
const path = require('path')
const { execFileSync } = require('child_process')

const root = path.resolve(__dirname, '..', '..')
const apk = process.argv[2] || path.join(root, 'app/build/outputs/apk/release/app-release-unsigned.apk')
const limits = JSON.parse(fs.readFileSync(path.join(__dirname, 'limits.json'), 'utf8'))

const bytes = fs.statSync(apk).size
const methods = Number(
  /method_refs=(\d+)/.exec(execFileSync('node', [path.join(__dirname, 'dex-methods.cjs'), apk]).toString())[1],
)

const rows = [
  ['release APK bytes', bytes, limits.releaseApkBytes],
  ['release method refs', methods, limits.releaseMethodRefs],
]
let failed = false
for (const [name, value, limit] of rows) {
  const ok = value <= limit
  failed ||= !ok
  console.log(`${ok ? 'ok  ' : 'FAIL'} ${name}: ${value} (limit ${limit})`)
}
process.exit(failed ? 1 : 0)
