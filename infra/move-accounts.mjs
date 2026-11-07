// Moves people's Slideshow data from their old account IDs to their new ones, for the move to the "frontpack"
// External ID tenant (docs/tenant-move.md). Signing in to the new tenant gives each person a new account ID, so
// their albums, files and share links have to be re-filed under it. Run it once both have signed in to the new
// tenant (which creates their new account).
//
//   node infra/move-accounts.mjs <accounts storage> <slideshow storage>                 report what would move
//   node infra/move-accounts.mjs <accounts storage> <slideshow storage> --copy          copy to the new IDs
//   node infra/move-accounts.mjs <accounts storage> <slideshow storage> --delete-old    then remove the old copies
//   ... --map <old-id>=<new-id>   pair accounts by hand (otherwise: by email, the newer account being the new one)
//
// Copying leaves the old data alone, and running it again is safe. Delete the old copies only after checking the
// site. Signs in as you with the Azure CLI (shared keys are off): you need Storage Table Data Contributor on both
// accounts and Storage Blob Data Contributor on the slideshow account.
import { execSync } from 'node:child_process';

const args = process.argv.slice(2);
const [accountsStorage, slideshowStorage] = args.filter(a => !a.startsWith('--') && !a.includes('='));
const mode = args.includes('--delete-old') ? 'delete-old' : args.includes('--copy') ? 'copy' : 'report';
const manual = new Map(args.filter(a => a.includes('=')).map(a => a.split('=')));
if (!accountsStorage || !slideshowStorage) throw new Error('Usage: node infra/move-accounts.mjs <accounts storage> <slideshow storage> [--copy | --delete-old] [--map old=new ...]');

// Storage tokens last about an hour, and a big copy takes longer: get a fresh one every 40 minutes.
let token = '', tokenAt = 0;
function authHeaders() {
  if (Date.now() - tokenAt > 40 * 60_000) {
    token = execSync('az account get-access-token --resource https://storage.azure.com/ --query accessToken --output tsv',
      { encoding: 'utf8', shell: true }).trim();
    tokenAt = Date.now();
  }
  return { Authorization: `Bearer ${token}`, 'x-ms-version': '2021-08-06' };
}
const tableUrl = (account, table) => `https://${account}.table.core.windows.net/${table}`;
const blobUrl = (name = '') => `https://${slideshowStorage}.blob.core.windows.net/media${name ? '/' + name.split('/').map(encodeURIComponent).join('/') : ''}`;
const key = s => encodeURIComponent(s.replaceAll("'", "''"));

async function check(res, what) {
  if (!res.ok) throw new Error(`${what}: ${res.status} ${await res.text()}`);
  return res;
}

// ───────── Tables ─────────

async function query(account, table, filter) {
  const rows = [];
  let next = '';
  do {
    const sep = next ? '&' : '';
    const res = await check(await fetch(`${tableUrl(account, table)}()?$filter=${encodeURIComponent(filter)}${sep}${next}`,
      { headers: { ...authHeaders(), Accept: 'application/json;odata=minimalmetadata' } }), `Reading ${table}`);
    rows.push(...(await res.json()).value);
    const pk = res.headers.get('x-ms-continuation-NextPartitionKey'), rk = res.headers.get('x-ms-continuation-NextRowKey');
    next = pk ? `NextPartitionKey=${encodeURIComponent(pk)}${rk ? `&NextRowKey=${encodeURIComponent(rk)}` : ''}` : '';
  } while (next);
  return rows;
}

/** Writes the row (insert or replace) under the given keys, without the old row's service fields. */
async function put(account, table, row, partitionKey, rowKey) {
  const { 'odata.etag': _e, 'odata.metadata': _m, Timestamp, 'Timestamp@odata.type': _t, ...entity } = row;
  Object.assign(entity, { PartitionKey: partitionKey, RowKey: rowKey });
  await check(await fetch(`${tableUrl(account, table)}(PartitionKey='${key(partitionKey)}',RowKey='${key(rowKey)}')`, {
    method: 'PUT', headers: { ...authHeaders(), 'Content-Type': 'application/json', Accept: 'application/json;odata=nometadata' }, body: JSON.stringify(entity),
  }), `Writing ${table}`);
}

async function remove(account, table, row) {
  const res = await fetch(`${tableUrl(account, table)}(PartitionKey='${key(row.PartitionKey)}',RowKey='${key(row.RowKey)}')`,
    { method: 'DELETE', headers: { ...authHeaders(), 'If-Match': '*' } });
  if (res.status !== 404) await check(res, `Deleting from ${table}`);
}

// ───────── Blobs ─────────

async function listBlobs(prefix) {
  const names = [];
  let marker = '';
  do {
    const res = await check(await fetch(`${blobUrl()}?restype=container&comp=list&prefix=${encodeURIComponent(prefix)}${marker ? `&marker=${encodeURIComponent(marker)}` : ''}`,
      { headers: authHeaders() }), 'Listing files');
    const xml = await res.text();
    for (const m of xml.matchAll(/<Name>([^<]*)<\/Name>/g)) names.push(m[1].replaceAll('&amp;', '&'));
    marker = /<NextMarker>([^<]*)<\/NextMarker>/.exec(xml)?.[1] ?? '';
  } while (marker);
  return names;
}

/**
 * Has Azure copy the file within the storage account (Put Blob From URL: the bytes never leave Azure), unless the
 * copy is already there. Returns false when it was. A copy is made whole or not at all, so one that's there is complete.
 */
async function copyBlob(from, to) {
  const headers = authHeaders();
  const res = await fetch(blobUrl(to), {
    method: 'PUT',
    headers: {
      ...headers,
      'x-ms-blob-type': 'BlockBlob',
      'Content-Length': '0',
      'x-ms-copy-source': blobUrl(from),
      'x-ms-copy-source-authorization': headers.Authorization,
      'If-None-Match': '*', // already copied: leave it
    },
  });
  if (res.status === 409 || res.status === 412) { await res.body?.cancel(); return false; }
  await check(res, `Copying ${from}`);
  return true;
}

const COPIES_AT_ONCE = 16;

/** Runs `work`, trying again after a dropped connection or a server hiccup (a long copy meets a few). */
async function retrying(work, what, tries = 5) {
  for (let attempt = 1; ; attempt++) {
    try { return await work(); }
    catch (err) {
      if (attempt >= tries) throw err;
      console.log(`  ${what}: ${err.cause?.code ?? err.message}; trying again (${attempt + 1} of ${tries})`);
      await new Promise(r => setTimeout(r, 1000 * 2 ** attempt));
    }
  }
}

async function deleteBlob(name) {
  const res = await fetch(blobUrl(name), { method: 'DELETE', headers: authHeaders() });
  if (res.status !== 404) await check(res, `Deleting ${name}`);
}

// ───────── Who moves where ─────────

const users = await query(accountsStorage, 'Users', "RowKey eq 'user'");
const byId = new Map(users.map(u => [u.PartitionKey, u]));
const pairs = new Map(manual);
if (!manual.size) {
  const byEmail = Map.groupBy(users.filter(u => u.Email), u => u.Email.toLowerCase());
  for (const [email, rows] of byEmail) {
    if (rows.length !== 2) { if (rows.length > 2) console.log(`${email}: ${rows.length} accounts; pair them by hand with --map.`); continue; }
    const [older, newer] = rows.sort((a, b) => String(a.CreatedAt).localeCompare(String(b.CreatedAt)));
    pairs.set(older.PartitionKey, newer.PartitionKey);
  }
  const noEmail = users.filter(u => !u.Email).length;
  if (noEmail) console.log(`${noEmail} account(s) have no email: pair them by hand with --map old=new if they need moving.`);
}
if (!pairs.size) { console.log('Nothing to move: no one has an old and a new account yet.'); process.exit(0); }

for (const [oldId, newId] of pairs) {
  if (!byId.has(newId)) throw new Error(`${newId} has no account: have them sign in to the new tenant first.`);
  const who = byId.get(newId).Email ?? newId;
  const albums = await query(slideshowStorage, 'Albums', `PartitionKey eq '${oldId}'`);
  const media = await query(slideshowStorage, 'Media', `PartitionKey eq '${oldId}'`);
  const shares = await query(slideshowStorage, 'Shares', `OwnerId eq '${oldId}'`);
  const blobs = await listBlobs(`${oldId}/`);
  console.log(`${who}: ${oldId} -> ${newId}: ${albums.length} albums, ${media.length} files, ${shares.length} share links, ${blobs.length} stored files (with previews and smaller versions).`);
  if (mode === 'report') continue;

  if (mode === 'copy') {
    // Files first, then the rows that point at them, so the new account never lists a file that isn't there yet.
    // The copies happen inside Azure, so several at a time.
    let copied = 0, done = 0;
    for (let i = 0; i < blobs.length; i += COPIES_AT_ONCE) {
      const batch = blobs.slice(i, i + COPIES_AT_ONCE);
      const results = await Promise.all(batch.map(name => retrying(() => copyBlob(name, newId + name.slice(oldId.length)), name)));
      copied += results.filter(Boolean).length;
      done += batch.length;
      if (done % 200 < COPIES_AT_ONCE || done === blobs.length) console.log(`  ${done} of ${blobs.length} files`);
    }
    for (const m of media) {
      const moved = { ...m, BlobName: String(m.BlobName).startsWith(`${oldId}/`) ? newId + String(m.BlobName).slice(oldId.length) : m.BlobName };
      await put(slideshowStorage, 'Media', moved, newId, m.RowKey);
    }
    for (const a of albums) await put(slideshowStorage, 'Albums', a, newId, a.RowKey);
    for (const s of shares) await put(slideshowStorage, 'Shares', { ...s, OwnerId: newId }, s.PartitionKey, s.RowKey); // links keep working
    const check2 = [(await query(slideshowStorage, 'Albums', `PartitionKey eq '${newId}'`)).length, (await query(slideshowStorage, 'Media', `PartitionKey eq '${newId}'`)).length];
    console.log(`  copied ${copied} files (${blobs.length - copied} were there already); the new account now has ${check2[0]} albums and ${check2[1]} files.`);
  } else {
    // Only what was copied: each old row or file is removed once its copy is confirmed.
    const newAlbums = new Set((await query(slideshowStorage, 'Albums', `PartitionKey eq '${newId}'`)).map(a => a.RowKey));
    const newMedia = new Set((await query(slideshowStorage, 'Media', `PartitionKey eq '${newId}'`)).map(m => m.RowKey));
    const newBlobs = new Set(await listBlobs(`${newId}/`));
    const missing = [...albums.filter(a => !newAlbums.has(a.RowKey)), ...media.filter(m => !newMedia.has(m.RowKey))].length
      + blobs.filter(n => !newBlobs.has(newId + n.slice(oldId.length))).length;
    if (missing) { console.log(`  ${missing} items have no copy yet: run --copy first. Nothing deleted.`); continue; }
    for (const m of media) await remove(slideshowStorage, 'Media', m);
    for (const a of albums) await remove(slideshowStorage, 'Albums', a);
    for (const name of blobs) await deleteBlob(name);
    await remove(accountsStorage, 'Users', byId.get(oldId));
    console.log('  old copies and the old account removed.');
  }
}
