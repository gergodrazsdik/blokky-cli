// Dump how yaml-language-server's own parser types plain scalars (yamlVersion 1.2 vs 1.1).
const path = require('path');
const srv = path.dirname(require.resolve('yaml-language-server/package.json', { paths: [process.argv[2]] }));
const { parse } = require(path.join(srv, 'out/server/src/languageservice/parser/yamlParser07.js'));
const scalars = ['1.10', '1.0', 'on', 'off', 'yes', 'no', 'y', 'n', 'True', 'TRUE', '010', '08', '0o10', '0x10', '1e3', '3.0', '.inf', '~', 'null', 'Null', '', '30', '30s', '1_000', '+1'];
const rows = [];
for (const s of scalars) {
  const row = [s === '' ? '(empty)' : s];
  for (const v of ['1.2', '1.1']) {
    const doc = parse(`k: ${s}\n`, { customTags: [], yamlVersion: v }).documents[0];
    const val = doc.root.children[0].valueNode;
    row.push(val ? `${val.type} ${JSON.stringify(val.value)}` : 'null (no node)');
  }
  rows.push(row);
}
console.log('| Plain scalar | yamlVersion 1.2 | yamlVersion 1.1 |\n|---|---|---|');
for (const r of rows) console.log(`| \`${r[0]}\` | ${r[1]} | ${r[2]} |`);
