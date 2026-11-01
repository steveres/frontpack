// Copies the FrontPack accounts (the Users table) from one storage account to another, for the cut-over from
// the single API (slideshow-rg) to the platform (frontpack-rg). Rows are written as they are (insert or
// replace), so running it again is safe. Signs in as you with the Azure CLI: shared keys are off, so you
// need Storage Table Data Reader on the source and Storage Table Data Contributor on the target.
//   node infra/copy-users.mjs <source account> <target account> [--dry-run]
import { execSync } from 'node:child_process';

const [source, target] = process.argv.slice(2).filter(a => !a.startsWith('--'));
const dryRun = process.argv.includes('--dry-run');
if (!source || !target) throw new Error('Usage: node infra/copy-users.mjs <source account> <target account> [--dry-run]');

const token = execSync('az account get-access-token --resource https://storage.azure.com/ --query accessToken --output tsv',
  { encoding: 'utf8', shell: true }).trim();
const headers = {
  Authorization: `Bearer ${token}`,
  'x-ms-version': '2020-12-06',
  Accept: 'application/json;odata=minimalmetadata', // keeps the type of each value (e.g. dates)
};
const table = account => `https://${account}.table.core.windows.net/Users`;

async function readAll(account) {
  const rows = [];
  let next = '';
  do {
    const res = await fetch(`${table(account)}()${next}`, { headers });
    if (!res.ok) throw new Error(`Reading ${account}: ${res.status} ${await res.text()}`);
    rows.push(...(await res.json()).value);
    const pk = res.headers.get('x-ms-continuation-NextPartitionKey'), rk = res.headers.get('x-ms-continuation-NextRowKey');
    next = pk ? `?NextPartitionKey=${encodeURIComponent(pk)}${rk ? `&NextRowKey=${encodeURIComponent(rk)}` : ''}` : '';
  } while (next);
  return rows;
}

const key = s => encodeURIComponent(s.replaceAll("'", "''"));

const rows = await readAll(source);
console.log(`${source}: ${rows.length} accounts.`);
if (dryRun) {
  console.log(`${target}: ${(await readAll(target)).length} accounts now. Dry run: nothing copied.`);
  process.exit(0);
}

for (const row of rows) {
  const { 'odata.etag': _etag, 'odata.metadata': _meta, Timestamp, 'Timestamp@odata.type': _ts, ...entity } = row;
  const res = await fetch(`${table(target)}(PartitionKey='${key(row.PartitionKey)}',RowKey='${key(row.RowKey)}')`, {
    method: 'PUT', // no If-Match: insert or replace
    headers: { ...headers, 'Content-Type': 'application/json' },
    body: JSON.stringify(entity),
  });
  if (!res.ok) throw new Error(`Writing ${row.PartitionKey} to ${target}: ${res.status} ${await res.text()}`);
}

const copied = await readAll(target);
const missing = rows.filter(r => !copied.some(c => c.PartitionKey === r.PartitionKey && c.RowKey === r.RowKey));
console.log(`${target}: ${copied.length} accounts (${missing.length} of the source's missing).`);
if (missing.length) process.exit(1);
