// Runs server/schema.sql in PGlite with a Supabase-like auth stub and checks every policy.
import { PGlite } from '@electric-sql/pglite';
import { readFileSync } from 'fs';
import { createHash } from 'crypto';

const schema = readFileSync(process.argv[2], 'utf8');
const db = new PGlite();
let fails = 0;
const check = (name, ok, detail) => { console.log((ok ? 'PASS ' : 'FAIL ') + name + (detail ? '  [' + detail + ']' : '')); if (!ok) fails++; };

await db.exec(`
  create role anon nologin; create role authenticated nologin;
  create schema auth;
  create table auth.users (id uuid primary key, email text);
  create function auth.uid() returns uuid language sql stable as
    $$ select nullif(current_setting('request.jwt.claim.sub', true), '')::uuid $$;
  grant usage on schema auth to anon, authenticated;
  grant execute on function auth.uid() to anon, authenticated;
  grant usage on schema public to anon, authenticated;
  -- what Supabase does by default, so the schema's revokes get tested
  alter default privileges in schema public grant all on tables to anon, authenticated;
  alter default privileges in schema public grant all on functions to anon, authenticated;
`);
await db.exec(schema);
await db.exec(schema); // re-runnable
check('schema applies twice', true);

const U = { alice: '00000000-0000-0000-0000-00000000000a', bob: '00000000-0000-0000-0000-00000000000b',
            owner: '00000000-0000-0000-0000-00000000000c', eve: '00000000-0000-0000-0000-00000000000e' };
for (const [n, id] of Object.entries(U)) await db.query(`insert into auth.users values ($1, $2)`, [id, n + '@x']);
for (const n of ['alice', 'bob', 'owner']) await db.query(`insert into public.profiles (id, username) values ($1, $2)`, [U[n], n]);
await db.query(`insert into public.admins values ($1)`, [U.owner]);

async function as(who, sql, params = []) {
  try {
    await db.exec('begin');
    await db.exec(who === 'anon' ? 'set local role anon' : 'set local role authenticated');
    await db.query(`select set_config('request.jwt.claim.sub', $1, true)`, [who === 'anon' ? '' : U[who]]);
    const r = await db.query(sql, params);
    await db.exec('commit');
    return { rows: r.rows };
  } catch (e) {
    await db.exec('rollback');
    return { error: e.message };
  }
}
const ok = r => !r.error;

const src1 = '[DeskWidget(Name = "Moon")] public sealed class Moon : WidgetWindow { /* é ☾ */ }';
const src2 = src1 + ' // v2';
const upload = (who, id, src) => as(who,
  `insert into public.versions (widget_id, name, category, description, source) values ($1, 'Moon', 'clocks', 'Tonight', $2)`, [id, src]);

check('alice creates u.alice.moon', ok(await as('alice', `insert into public.widgets (id, owner, slug) values ('u.alice.moon', $1, 'moon')`, [U.alice])));
let r = await as('alice', `insert into public.widgets (id, owner, slug) values ('u.bob.moon', $1, 'moon')`, [U.alice]);
check('alice cannot claim u.bob.*', !ok(r), r.error);
r = await as('alice', `insert into public.widgets (id, owner, slug) values ('u.alice.x', $1, 'x')`, [U.bob]);
check('alice cannot set another owner', !ok(r), r.error);
r = await as('eve', `insert into public.widgets (id, owner, slug) values ('u.eve.x', $1, 'x')`, [U.eve]);
check('account without profile cannot upload', !ok(r), r.error);
r = await as('alice', `insert into public.widgets (id, owner, slug) values ('u.alice.Moon', $1, 'Moon')`, [U.alice]);
check('bad slug rejected', !ok(r), r.error);

check('alice uploads v1', ok(await upload('alice', 'u.alice.moon', src1)));
check('alice uploads v2', ok(await upload('alice', 'u.alice.moon', src2)));
r = await as('alice', `insert into public.versions (widget_id, name, category, source, status) values ('u.alice.moon', 'Moon', 'clocks', 'x', 'approved')`);
check('uploader cannot insert approved', !ok(r), r.error);
r = await as('alice', `insert into public.versions (widget_id, name, category, source, sha256) values ('u.alice.moon', 'Moon', 'clocks', 'x', repeat('a', 64))`);
check('uploader cannot set sha256', !ok(r), r.error);
r = await as('alice', `insert into public.versions (widget_id, version, name, category, source) values ('u.alice.moon', 1, 'Moon', 'clocks', 'x')`);
check('uploader cannot choose version', !ok(r), r.error);
r = await upload('bob', 'u.alice.moon', 'x');
check("bob cannot upload to alice's widget", !ok(r), r.error);
r = await as('alice', `select version from public.versions where widget_id = 'u.alice.moon' order by version`);
check('server numbers versions 1, 2', ok(r) && r.rows.map(x => x.version).join() === '1,2', JSON.stringify(r.rows || r.error));

r = await as('alice', `update public.versions set source = 'evil' where widget_id = 'u.alice.moon'`);
check('uploader cannot update', !ok(r), r.error);
r = await as('alice', `delete from public.versions`);
check('uploader cannot delete', !ok(r), r.error);
r = await as('owner', `update public.versions set status = 'approved'`);
check('even admin cannot update directly', !ok(r), r.error);

r = await as('anon', `select * from public.versions`);
check('anon sees no pending', ok(r) && r.rows.length === 0, r.error || r.rows.length + ' rows');
r = await as('bob', `select * from public.versions`);
check('bob sees no pending of alice', ok(r) && r.rows.length === 0, r.error || r.rows.length + ' rows');
r = await as('anon', `select * from public.widgets`);
check('anon sees no unapproved widget', ok(r) && r.rows.length === 0, r.error || r.rows.length + ' rows');

const hash = s => createHash('sha256').update(Buffer.from(s, 'utf8')).digest('hex');
r = await as('alice', `select public.approve('u.alice.moon', 1, $1)`, [hash(src1)]);
check('uploader cannot approve', !ok(r), r.error);
r = await as('anon', `select public.approve('u.alice.moon', 1, $1)`, [hash(src1)]);
check('anon cannot approve', !ok(r), r.error);
r = await as('owner', `select public.approve('u.alice.moon', 1, $1)`, [hash(src2)]);
check('approve with wrong hash fails', !ok(r), r.error);
r = await as('owner', `select public.approve('u.alice.moon', 1, $1)`, [hash(src1)]);
check('approve with hash of shown text', ok(r), r.error);
r = await as('owner', `select public.approve('u.alice.moon', 1, $1)`, [hash(src1)]);
check('approved version cannot be approved again', !ok(r), r.error);
r = await as('owner', `select public.reject('u.alice.moon', 2)`);
check('admin rejects v2', ok(r), r.error);

r = await as('anon', `select widget_id, version, sha256, source from public.versions`);
check('anon sees exactly approved v1 with source', ok(r) && r.rows.length === 1 && r.rows[0].version === 1 && r.rows[0].sha256 === hash(src1) && r.rows[0].source === src1,
  JSON.stringify(r.error || r.rows.map(x => x.version)));
r = await as('anon', `select * from public.explore`);
check('explore lists v1 by alice, no source', ok(r) && r.rows.length === 1 && r.rows[0].author === 'alice' && !('source' in r.rows[0]), JSON.stringify(r.error || r.rows[0]));
r = await as('anon', `select * from public.widgets`);
check('anon sees the approved widget', ok(r) && r.rows.length === 1, r.error);
r = await as('anon', `select * from public.profiles`);
check('anon cannot read profiles', !ok(r), r.error);
r = await as('anon', `select username from public.authors order by username`);
check('anon reads usernames via authors', ok(r) && r.rows.length === 3, r.error);
r = await as('bob', `select username from public.profiles`);
check('bob reads only his profile', ok(r) && r.rows.length === 1 && r.rows[0].username === 'bob', JSON.stringify(r.rows || r.error));
r = await as('bob', `update public.profiles set username = 'alice2' where id = $1`, [U.bob]);
check('usernames are immutable', !ok(r), r.error);
r = await as('anon', `insert into public.widgets (id, owner, slug) values ('u.alice.y', $1, 'y')`, [U.alice]);
check('anon cannot insert', !ok(r), r.error);
r = await as('alice', `select version, status from public.versions order by version`);
check('alice sees her rows in every status', ok(r) && r.rows.map(x => x.status).join() === 'approved,rejected', JSON.stringify(r.rows || r.error));
r = await as('bob', `insert into public.admins values ($1)`, [U.bob]);
check('nobody can make themselves admin', !ok(r), r.error);
r = await as('owner', `select version, status from public.versions where widget_id = 'u.alice.moon' order by version`);
check('admin sees all versions', ok(r) && r.rows.length === 2, r.error);

// Source must read the way it compiles (Trojan Source): hidden line breaks, invisible and bidi
// characters are refused by the database, whichever way they're uploaded.
const hidden = {
  'lone CR (the review-bypass exploit)': '// Harmless note\rSystem.Diagnostics.Process.Start("powershell", "-enc AAAA");',
  'U+2028 line separator': '// note\u2028Evil();',
  'U+2029 paragraph separator': '// note\u2029Evil();',
  'U+0085 next line': '// note\u0085Evil();',
  'U+202E bidi override': 'var s = "\u202Eevil";',
  'U+2066 bidi isolate': '/* \u2066 */ Evil();',
  'U+200B zero-width space': 'Ev\u200Bil();',
  'U+FEFF inside the text': 'class A {}\uFEFF',
  'other control character': 'class A {}\u0007',
};
for (const [what, src] of Object.entries(hidden)) {
  r = await upload('alice', 'u.alice.moon', src);
  check('refuses ' + what, !ok(r) && /versions_source_plain/.test(r.error), r.error || 'accepted');
}
r = await upload('alice', 'u.alice.moon', 'class A\r\n{\r\n\tint x; // é ☾ 日本\r\n}\n');
check('accepts CRLF, LF, tab and Unicode text', ok(r), r.error);

console.log(fails ? fails + ' FAILED' : 'all passed');
process.exit(fails ? 1 : 0);
