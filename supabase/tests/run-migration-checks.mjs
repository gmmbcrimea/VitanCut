import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { PGlite } from '@electric-sql/pglite';
import { pgcrypto } from '@electric-sql/pglite/contrib/pgcrypto';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '..', '..');
const migrationDir = path.join(repo, 'supabase', 'migrations');
const migrationFiles = [
  '2026090901_vitan_cut_cloud_sync.sql',
  '2026090902_vitan_cut_addressable_sync.sql',
  '2026090903_vitan_cut_cloud_upgrade.sql',
  '2026091004_vitan_cut_delta_sync.sql',
  '2026091005_fix_delta_sync_revision.sql',
  '2026101201_secure_and_seed_delta_workspaces.sql'
];
const userId = '11111111-1111-4111-8111-111111111111';
const otherUserId = '22222222-2222-4222-8222-222222222222';
const payload = {
  counterparties: ['Synthetic customer'],
  materialGroups: ['ЛДСП', 'Кромка'],
  materials: { ЛДСП: [{ id: 'material-1', name: 'Synthetic board' }] },
  catalog: { 'Synthetic customer': [{ id: 'catalog-1', name: 'Synthetic product', details: [{ id: 'detail-1' }] }] },
  projects: [{ id: 'project-1', name: 'Synthetic project' }],
  preferences: { cutPalette: 'blue', cutTrim: 10, cutGap: 6 }
};

const db = new PGlite({ extensions: { pgcrypto } });
const check = async (label, sql, expected, params = []) => {
  const result = await db.query(sql, params);
  assert.deepEqual(result.rows, expected, label);
  console.log(`PASS: ${label}`);
};

try {
  await db.exec(`
    create role anon nologin;
    create role authenticated nologin;
    create schema auth;
    create table auth.users(id uuid primary key);
    create function auth.uid() returns uuid language sql stable as $$
      select nullif(current_setting('request.jwt.claim.sub', true), '')::uuid
    $$;
    insert into auth.users(id) values ('${userId}'), ('${otherUserId}');
  `);

  for (const file of migrationFiles.slice(0, -1)) {
    await db.exec(await readFile(path.join(migrationDir, file), 'utf8'));
    console.log(`PASS: applied ${file}`);
  }

  await db.exec(`set request.jwt.claim.sub = '${userId}';`);
  await db.query('select * from public.create_vitan_workspace($1, $2::jsonb)', ['Legacy fixture', JSON.stringify(payload)]);
  await check('Pre-migration legacy workspace has no entity rows',
    'select count(*)::int as count from public.workspace_entities', [{ count: 0 }]);

  const finalMigration = await readFile(path.join(migrationDir, migrationFiles.at(-1)), 'utf8');
  await db.exec(finalMigration);
  await check('Migration backfills each entity family from the legacy snapshot',
    'select entity_type, count(*)::int as count from public.workspace_entities group by entity_type order by entity_type',
    [
      { entity_type: 'catalog-product', count: 1 },
      { entity_type: 'counterparties', count: 1 },
      { entity_type: 'cut-settings', count: 1 },
      { entity_type: 'material', count: 1 },
      { entity_type: 'material-groups', count: 1 },
      { entity_type: 'project', count: 1 }
    ]);
  await db.exec(finalMigration);
  await check('Migration is idempotent for existing entity keys',
    'select count(*)::int as count from public.workspace_entities', [{ count: 6 }]);

  const productGroupsMigration = await readFile(path.join(migrationDir, '2026102701_product_groups.sql'), 'utf8');
  await db.exec(productGroupsMigration);
  await db.exec(productGroupsMigration);
  console.log('PASS: product group migration applies idempotently');
  const healthMigration = await readFile(path.join(migrationDir, '2026102702_cloud_health.sql'), 'utf8');
  await db.exec(healthMigration);
  await db.exec(healthMigration);
  await db.exec('set role anon');
  await check('Anonymous cloud health RPC returns only a database availability boolean',
    'select public.vitan_cloud_health() as healthy', [{ healthy: true }]);
  await assert.rejects(db.query('select * from public.workspace_snapshots'), /permission denied/);
  await db.exec('reset role');
  await check('Cloud health RPC does not elevate privileges',
    "select prosecdef as definer from pg_proc where oid = 'public.vitan_cloud_health()'::regprocedure", [{ definer: false }]);

  await check('Composer is not executable by public, anon, or authenticated', `
    select
      has_function_privilege('public', 'public.compose_vitan_snapshot(uuid,jsonb)', 'execute') as public_exec,
      has_function_privilege('anon', 'public.compose_vitan_snapshot(uuid,jsonb)', 'execute') as anon_exec,
      has_function_privilege('authenticated', 'public.compose_vitan_snapshot(uuid,jsonb)', 'execute') as auth_exec
  `, [{ public_exec: false, anon_exec: false, auth_exec: false }]);

  const workspace = await db.query('select id from public.workspaces limit 1');
  const legacyWorkspaceId = workspace.rows[0].id;
  await db.exec(`set role authenticated; set request.jwt.claim.sub = '${userId}';`);
  await check('Workspace owner reads the backfilled entity rows',
    'select count(*)::int as count from public.workspace_entities where workspace_id = $1', [{ count: 6 }], [legacyWorkspaceId]);
  await db.exec(`set request.jwt.claim.sub = '${otherUserId}';`);
  await check('Non-member cannot read another workspace entity rows',
    'select count(*)::int as count from public.workspace_entities where workspace_id = $1', [{ count: 0 }], [legacyWorkspaceId]);
  await db.exec('reset role');
  await db.query('insert into public.workspace_members(workspace_id, user_id, role) values ($1, $2, $3)',
    [legacyWorkspaceId, otherUserId, 'editor']);
  await db.exec(`set role authenticated; set request.jwt.claim.sub = '${otherUserId}';`);
  await check('Workspace editor can read member-scoped entities',
    'select count(*)::int as count from public.workspace_entities where workspace_id = $1', [{ count: 6 }], [legacyWorkspaceId]);
  await db.exec('reset role');
  await db.exec('set role anon');
  await assert.rejects(db.query('select * from public.workspace_snapshots'), /permission denied/);
  console.log('PASS: anon cannot read cloud snapshots');
  await db.exec(`reset role; set role authenticated; set request.jwt.claim.sub = '${userId}';`);

  const created = await db.query('select * from public.create_vitan_workspace($1, $2::jsonb)', ['New fixture', JSON.stringify(payload)]);
  const newWorkspaceId = created.rows[0].workspace_id;
  await check('New workspace creation seeds all entity families',
    'select count(*)::int as count from public.workspace_entities where workspace_id = $1', [{ count: 6 }], [newWorkspaceId]);
  const invalidPayload = { ...payload, projects: [{ name: 'Missing ID' }] };
  await assert.rejects(db.query('select * from public.create_vitan_workspace($1, $2::jsonb)', ['Invalid fixture', JSON.stringify(invalidPayload)]),
    /workspace_entity_missing_id:project/);
  await check('Invalid initial payload rolls back workspace and all seeded entities',
    "select count(*)::int as count from public.workspaces where name = 'Invalid fixture'", [{ count: 0 }]);

  const change = [{ entity_type: 'project', entity_id: 'project-1', payload: { id: 'project-1', name: 'Updated synthetic project' }, expected_revision: 1, deleted: false }];
  const published = await db.query('select * from public.sync_vitan_entity_changes($1, 1, $2::jsonb)', [newWorkspaceId, JSON.stringify(change)]);
  assert.equal(Number(published.rows[0].revision), 2, 'valid addressable update should increment snapshot revision');
  console.log('PASS: valid addressable update increments the snapshot revision');
  await check('Addressable update changes the entity and composed snapshot', `
    select s.revision::int as revision,
      s.payload->'projects'->0->>'name' as project_name,
      e.payload->>'name' as entity_name
    from public.workspace_snapshots s
    join public.workspace_entities e using (workspace_id)
    where s.workspace_id = $1 and e.entity_type = 'project' and e.entity_id = 'project-1'
  `, [{ revision: 2, project_name: 'Updated synthetic project', entity_name: 'Updated synthetic project' }], [newWorkspaceId]);

  const staleChange = [{ ...change[0], payload: { id: 'project-1', name: 'Should roll back' }, expected_revision: 0 }];
  await assert.rejects(
    db.query('select * from public.sync_vitan_entity_changes($1, 2, $2::jsonb)', [newWorkspaceId, JSON.stringify(staleChange)]),
    /entity_revision_conflict/);
  await check('Entity revision conflict rolls back without changing the snapshot', `
    select s.revision::int as revision, e.payload->>'name' as entity_name
    from public.workspace_snapshots s join public.workspace_entities e using (workspace_id)
    where s.workspace_id = $1 and e.entity_type = 'project' and e.entity_id = 'project-1'
  `, [{ revision: 2, entity_name: 'Updated synthetic project' }], [newWorkspaceId]);

  await assert.rejects(db.query('select * from public.sync_vitan_entity_changes($1, 1, $2::jsonb)',
    [newWorkspaceId, JSON.stringify([])]), /workspace_revision_conflict/);
  await check('Stale snapshot revision is rejected without advancing workspace state',
    'select revision::int as revision from public.workspace_snapshots where workspace_id = $1', [{ revision: 2 }], [newWorkspaceId]);

  const deleteProject = [{ ...change[0], payload: {}, expected_revision: 2, deleted: true }];
  const deleted = await db.query('select * from public.sync_vitan_entity_changes($1, 2, $2::jsonb)',
    [newWorkspaceId, JSON.stringify(deleteProject)]);
  assert.equal(Number(deleted.rows[0].revision), 3, 'valid entity deletion should advance snapshot revision');
  await check('Entity deletion composes out the project and preserves its tombstone', `
    select s.revision::int as revision,
      jsonb_array_length(s.payload->'projects')::int as projects,
      e.deleted as deleted
    from public.workspace_snapshots s
    join public.workspace_entities e using (workspace_id)
    where s.workspace_id = $1 and e.entity_type = 'project' and e.entity_id = 'project-1'
  `, [{ revision: 3, projects: 0, deleted: true }], [newWorkspaceId]);

  const groupChange = [{ entity_type: 'product-groups', entity_id: 'root', payload: ['Empty group'], expected_revision: 0, deleted: false }];
  await db.query('select * from public.sync_vitan_entity_changes($1, 3, $2::jsonb)',
    [newWorkspaceId, JSON.stringify(groupChange)]);
  await check('Empty product groups appear in cloud snapshots after delta synchronization',
    "select payload->'productGroups' as groups from public.workspace_snapshots where workspace_id = $1",
    [{ groups: ['Empty group'] }], [newWorkspaceId]);
} finally {
  await db.close();
}
