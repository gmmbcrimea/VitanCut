-- The client sends only changed records.  The server keeps the complete snapshot
-- for first-time downloads and the five-version recovery history.

do $$
declare
    v_snapshot record;
    v_group record;
    v_item jsonb;
begin
    for v_snapshot in select workspace_id, payload, updated_by, updated_at from public.workspace_snapshots loop
        insert into public.workspace_entities(workspace_id, entity_type, entity_id, payload, updated_by, updated_at)
        select v_snapshot.workspace_id, 'counterparties', 'root', coalesce(v_snapshot.payload->'counterparties', '[]'::jsonb), v_snapshot.updated_by, v_snapshot.updated_at
        on conflict do nothing;
        insert into public.workspace_entities(workspace_id, entity_type, entity_id, payload, updated_by, updated_at)
        select v_snapshot.workspace_id, 'material-groups', 'root', coalesce(v_snapshot.payload->'materialGroups', '[]'::jsonb), v_snapshot.updated_by, v_snapshot.updated_at
        on conflict do nothing;
        insert into public.workspace_entities(workspace_id, entity_type, entity_id, payload, updated_by, updated_at)
        select v_snapshot.workspace_id, 'cut-settings', 'root', jsonb_build_object(
            'palette', coalesce(v_snapshot.payload->'preferences'->'cutPalette', '"Sky"'::jsonb),
            'exportFormat', coalesce(v_snapshot.payload->'preferences'->'cutExportFormat', '"Pdf"'::jsonb),
            'trim', coalesce(v_snapshot.payload->'preferences'->'cutTrim', '10'::jsonb),
            'gap', coalesce(v_snapshot.payload->'preferences'->'cutGap', '6'::jsonb),
            'usefulRemainder', coalesce(v_snapshot.payload->'preferences'->'cutUsefulRemainder', '250'::jsonb)),
            v_snapshot.updated_by, v_snapshot.updated_at
        on conflict do nothing;

        for v_item in select value from jsonb_array_elements(coalesce(v_snapshot.payload->'projects', '[]'::jsonb)) loop
            insert into public.workspace_entities(workspace_id, entity_type, entity_id, payload, updated_by, updated_at)
            values (v_snapshot.workspace_id, 'project', v_item->>'id', v_item, v_snapshot.updated_by, v_snapshot.updated_at)
            on conflict do nothing;
        end loop;
        for v_group in select key, value from jsonb_each(coalesce(v_snapshot.payload->'materials', '{}'::jsonb)) loop
            for v_item in select value from jsonb_array_elements(v_group.value) loop
                insert into public.workspace_entities(workspace_id, entity_type, entity_id, payload, updated_by, updated_at)
                values (v_snapshot.workspace_id, 'material', v_item->>'id', jsonb_build_object('group', v_group.key, 'material', v_item), v_snapshot.updated_by, v_snapshot.updated_at)
                on conflict do nothing;
            end loop;
        end loop;
        for v_group in select key, value from jsonb_each(coalesce(v_snapshot.payload->'catalog', '{}'::jsonb)) loop
            for v_item in select value from jsonb_array_elements(v_group.value) loop
                insert into public.workspace_entities(workspace_id, entity_type, entity_id, payload, updated_by, updated_at)
                values (v_snapshot.workspace_id, 'catalog-product', v_item->>'id', jsonb_build_object('counterparty', v_group.key, 'product', v_item), v_snapshot.updated_by, v_snapshot.updated_at)
                on conflict do nothing;
            end loop;
        end loop;
    end loop;
end;
$$;

create or replace function public.compose_vitan_snapshot(p_workspace_id uuid, p_fallback jsonb)
returns jsonb language sql stable security definer set search_path = public as $$
with active as (
    select entity_type, entity_id, payload
    from public.workspace_entities
    where workspace_id = p_workspace_id and not deleted
), material_groups as (
    select payload->>'group' as group_name, jsonb_agg(payload->'material' order by entity_id) as items
    from active where entity_type = 'material'
    group by payload->>'group'
), catalog_groups as (
    select payload->>'counterparty' as group_name, jsonb_agg(payload->'product' order by entity_id) as items
    from active where entity_type = 'catalog-product'
    group by payload->>'counterparty'
), cut_settings as (
    select payload from active where entity_type = 'cut-settings' and entity_id = 'root'
)
select p_fallback || jsonb_build_object(
    'counterparties', coalesce((select payload from active where entity_type = 'counterparties' and entity_id = 'root'), p_fallback->'counterparties', '[]'::jsonb),
    'materialGroups', coalesce((select payload from active where entity_type = 'material-groups' and entity_id = 'root'), p_fallback->'materialGroups', '[]'::jsonb),
    'materials', coalesce((select jsonb_object_agg(group_name, items) from material_groups), '{}'::jsonb),
    'catalog', coalesce((select jsonb_object_agg(group_name, items) from catalog_groups), '{}'::jsonb),
    'projects', coalesce((select jsonb_agg(payload order by entity_id) from active where entity_type = 'project'), '[]'::jsonb),
    'preferences', coalesce(p_fallback->'preferences', '{}'::jsonb) || coalesce((select jsonb_build_object(
        'cutPalette', payload->'palette',
        'cutExportFormat', payload->'exportFormat',
        'cutTrim', payload->'trim',
        'cutGap', payload->'gap',
        'cutUsefulRemainder', payload->'usefulRemainder') from cut_settings), '{}'::jsonb)
);
$$;

create or replace function public.sync_vitan_entity_changes(
    p_workspace_id uuid,
    p_expected_snapshot_revision bigint,
    p_changes jsonb
)
returns table(revision bigint)
language plpgsql security definer set search_path = public as $$
declare
    v_snapshot_revision bigint;
    v_current_revision bigint;
    v_snapshot_payload jsonb;
    v_next_payload jsonb;
    v_change record;
begin
    if auth.uid() is null or not public.is_vitan_member(p_workspace_id) then
        raise exception 'workspace_access_denied';
    end if;
    select revision, payload into v_snapshot_revision, v_snapshot_payload
    from public.workspace_snapshots where workspace_id = p_workspace_id for update;
    if v_snapshot_revision is null then raise exception 'workspace_not_found'; end if;
    if v_snapshot_revision <> p_expected_snapshot_revision then raise exception 'workspace_revision_conflict'; end if;

    for v_change in select * from jsonb_to_recordset(p_changes) as change_set(
        entity_type text, entity_id text, payload jsonb, expected_revision bigint, deleted boolean
    ) loop
        select revision into v_current_revision from public.workspace_entities
        where workspace_id = p_workspace_id and entity_type = v_change.entity_type and entity_id = v_change.entity_id for update;
        if v_current_revision is null then
            if coalesce(v_change.expected_revision, 0) <> 0 then raise exception 'entity_revision_conflict:%:%', v_change.entity_type, v_change.entity_id; end if;
            insert into public.workspace_entities(workspace_id, entity_type, entity_id, payload, revision, deleted, updated_by)
            values (p_workspace_id, v_change.entity_type, v_change.entity_id, v_change.payload, 1, coalesce(v_change.deleted, false), auth.uid());
        else
            if v_current_revision <> coalesce(v_change.expected_revision, 0) then raise exception 'entity_revision_conflict:%:%', v_change.entity_type, v_change.entity_id; end if;
            update public.workspace_entities set payload = v_change.payload, revision = v_current_revision + 1,
                deleted = coalesce(v_change.deleted, false), updated_by = auth.uid(), updated_at = now()
            where workspace_id = p_workspace_id and entity_type = v_change.entity_type and entity_id = v_change.entity_id;
        end if;
    end loop;

    v_next_payload := public.compose_vitan_snapshot(p_workspace_id, v_snapshot_payload);
    update public.workspace_snapshots set revision = v_snapshot_revision + 1, payload = v_next_payload,
        updated_by = auth.uid(), updated_at = now() where workspace_id = p_workspace_id;
    insert into public.workspace_snapshot_history(workspace_id, revision, payload, saved_by)
    values (p_workspace_id, v_snapshot_revision + 1, v_next_payload, auth.uid());
    delete from public.workspace_snapshot_history where workspace_id = p_workspace_id and revision not in (
        select revision from public.workspace_snapshot_history where workspace_id = p_workspace_id order by revision desc limit 6);
    update public.workspaces set updated_at = now() where id = p_workspace_id;
    return query select v_snapshot_revision + 1;
end;
$$;

revoke all on function public.sync_vitan_entity_changes(uuid, bigint, jsonb) from public;
grant execute on function public.sync_vitan_entity_changes(uuid, bigint, jsonb) to authenticated;
