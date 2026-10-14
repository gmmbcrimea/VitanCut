-- Restrict the internal snapshot composer to its SECURITY DEFINER callers.
revoke all on function public.compose_vitan_snapshot(uuid, jsonb) from public, anon, authenticated;

create or replace function public.seed_vitan_workspace_entities(
    p_workspace_id uuid,
    p_payload jsonb,
    p_actor uuid,
    p_updated_at timestamptz default now()
)
returns void
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_group record;
    v_item jsonb;
begin
    insert into public.workspace_entities(workspace_id, entity_type, entity_id, payload, updated_by, updated_at)
    values
        (p_workspace_id, 'counterparties', 'root', coalesce(p_payload->'counterparties', '[]'::jsonb), p_actor, p_updated_at),
        (p_workspace_id, 'material-groups', 'root', coalesce(p_payload->'materialGroups', '[]'::jsonb), p_actor, p_updated_at),
        (p_workspace_id, 'cut-settings', 'root', jsonb_build_object(
            'palette', coalesce(p_payload->'preferences'->'cutPalette', '"Sky"'::jsonb),
            'exportFormat', coalesce(p_payload->'preferences'->'cutExportFormat', '"Pdf"'::jsonb),
            'trim', coalesce(p_payload->'preferences'->'cutTrim', '10'::jsonb),
            'gap', coalesce(p_payload->'preferences'->'cutGap', '6'::jsonb),
            'usefulRemainder', coalesce(p_payload->'preferences'->'cutUsefulRemainder', '250'::jsonb)
        ), p_actor, p_updated_at)
    on conflict (workspace_id, entity_type, entity_id) do nothing;

    for v_item in select value from jsonb_array_elements(coalesce(p_payload->'projects', '[]'::jsonb)) loop
        if nullif(v_item->>'id', '') is null then raise exception 'workspace_entity_missing_id:project'; end if;
        insert into public.workspace_entities(workspace_id, entity_type, entity_id, payload, updated_by, updated_at)
        values (p_workspace_id, 'project', v_item->>'id', v_item, p_actor, p_updated_at)
        on conflict (workspace_id, entity_type, entity_id) do nothing;
    end loop;

    for v_group in select key, value from jsonb_each(coalesce(p_payload->'materials', '{}'::jsonb)) loop
        for v_item in select value from jsonb_array_elements(v_group.value) loop
            if nullif(v_item->>'id', '') is null then raise exception 'workspace_entity_missing_id:material'; end if;
            insert into public.workspace_entities(workspace_id, entity_type, entity_id, payload, updated_by, updated_at)
            values (p_workspace_id, 'material', v_item->>'id', jsonb_build_object('group', v_group.key, 'material', v_item), p_actor, p_updated_at)
            on conflict (workspace_id, entity_type, entity_id) do nothing;
        end loop;
    end loop;

    for v_group in select key, value from jsonb_each(coalesce(p_payload->'catalog', '{}'::jsonb)) loop
        for v_item in select value from jsonb_array_elements(v_group.value) loop
            if nullif(v_item->>'id', '') is null then raise exception 'workspace_entity_missing_id:catalog-product'; end if;
            insert into public.workspace_entities(workspace_id, entity_type, entity_id, payload, updated_by, updated_at)
            values (p_workspace_id, 'catalog-product', v_item->>'id', jsonb_build_object('counterparty', v_group.key, 'product', v_item), p_actor, p_updated_at)
            on conflict (workspace_id, entity_type, entity_id) do nothing;
        end loop;
    end loop;
end;
$$;
revoke all on function public.seed_vitan_workspace_entities(uuid, jsonb, uuid, timestamptz) from public, anon, authenticated;

-- Backfill missed/new legacy workspaces without replacing already addressable rows.
do $$
declare
    v_snapshot record;
begin
    for v_snapshot in
        select workspace_id, payload, updated_by, updated_at
        from public.workspace_snapshots
    loop
        perform public.seed_vitan_workspace_entities(
            v_snapshot.workspace_id, v_snapshot.payload, v_snapshot.updated_by, v_snapshot.updated_at
        );
    end loop;
end;
$$;

create or replace function public.create_vitan_workspace(p_name text, p_payload jsonb)
returns table(workspace_id uuid, revision bigint)
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_workspace_id uuid;
    v_revision bigint;
    v_existing_payload jsonb;
    v_actor uuid;
    v_updated_at timestamptz;
begin
    if auth.uid() is null then raise exception 'authentication_required'; end if;
    if jsonb_typeof(p_payload) <> 'object' then raise exception 'invalid_workspace_payload'; end if;

    select w.id, s.revision, s.payload, s.updated_by, s.updated_at
    into v_workspace_id, v_revision, v_existing_payload, v_actor, v_updated_at
    from public.workspaces as w
    join public.workspace_snapshots as s on s.workspace_id = w.id
    where w.owner_id = auth.uid() and w.name = trim(p_name)
    limit 1;

    if v_workspace_id is not null then
        -- Fill entity rows for an existing workspace created before delta seeding.
        perform public.seed_vitan_workspace_entities(v_workspace_id, v_existing_payload, v_actor, v_updated_at);
        workspace_id := v_workspace_id;
        revision := v_revision;
        return next;
        return;
    end if;

    insert into public.workspaces(name, owner_id)
    values (trim(p_name), auth.uid())
    returning id into v_workspace_id;
    insert into public.workspace_members(workspace_id, user_id, role)
    values (v_workspace_id, auth.uid(), 'owner') on conflict do nothing;
    insert into public.workspace_snapshots(workspace_id, revision, payload, updated_by)
    values (v_workspace_id, 1, p_payload, auth.uid());
    insert into public.workspace_snapshot_history(workspace_id, revision, payload, saved_by)
    values (v_workspace_id, 1, p_payload, auth.uid());
    perform public.seed_vitan_workspace_entities(v_workspace_id, p_payload, auth.uid(), now());
    return query select v_workspace_id, 1::bigint;
end;
$$;

revoke all on function public.create_vitan_workspace(text, jsonb) from public;
grant execute on function public.create_vitan_workspace(text, jsonb) to authenticated;
revoke all on function public.is_vitan_member(uuid) from public, anon;
grant execute on function public.is_vitan_member(uuid) to authenticated;
