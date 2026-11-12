// Fails when platform color, type or shape values, or raw color literals, appear in the app
// beyond the recorded baseline. The total may only go down.
// Usage: node tools/check/tokens.cjs check    compares against tools/check/token-baseline.json
//        node tools/check/tokens.cjs update   rewrites the baseline from the current sources
const fs = require('fs')
const path = require('path')

const root = path.resolve(__dirname, '..', '..')
const baselinePath = path.join(__dirname, 'token-baseline.json')
const pattern = /\b(MaterialTheme|MiuixTheme)\.(colorScheme|typography|shapes|textStyles)\b|\bColor\(0x[0-9A-Fa-f]{6,8}\)/g
const roots = ['app/src/main/java']

function walk(dir, out) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name)
    if (entry.isDirectory()) walk(full, out)
    else if (entry.name.endsWith('.kt')) out.push(full)
  }
  return out
}

const counts = {}
for (const base of roots) {
  for (const file of walk(path.join(root, base), [])) {
    const rel = path.relative(root, file).split(path.sep).join('/')
    const n = (fs.readFileSync(file, 'utf8').match(pattern) || []).length
    if (n > 0) counts[rel] = n
  }
}

const mode = process.argv[2]
if (mode === 'update') {
  const sorted = Object.fromEntries(Object.entries(counts).sort(([a], [b]) => a.localeCompare(b)))
  fs.writeFileSync(baselinePath, JSON.stringify(sorted, null, 2) + '\n')
  console.log(`baseline written: ${Object.keys(sorted).length} files, ${Object.values(sorted).reduce((a, b) => a + b, 0)} uses`)
} else if (mode === 'check') {
  const baseline = fs.existsSync(baselinePath) ? JSON.parse(fs.readFileSync(baselinePath, 'utf8')) : {}
  const violations = []
  const reducible = []
  for (const [file, n] of Object.entries(counts)) {
    const allowedCount = baseline[file] || 0
    if (n > allowedCount) violations.push(`${file}: ${n} uses, baseline ${allowedCount}`)
  }
  for (const [file, allowedCount] of Object.entries(baseline)) {
    const n = counts[file] || 0
    if (n < allowedCount) reducible.push(`${file}: ${n} uses, baseline ${allowedCount}`)
  }
  if (violations.length) {
    console.error('Platform colors, type styles, shapes or raw color literals appear in the app beyond the baseline. Use ZeroTheme tokens, and do not move existing uses between files:')
    violations.forEach((v) => console.error('  ' + v))
    process.exit(1)
  }
  if (reducible.length) {
    console.log('Fewer uses than the baseline; run `node tools/check/tokens.cjs update` to lock in the progress:')
    reducible.forEach((v) => console.log('  ' + v))
  }
  console.log('token boundary check passed')
} else {
  console.error('usage: tokens.cjs check|update')
  process.exit(2)
}
