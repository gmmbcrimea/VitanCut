-- The return column `revision` is also a PL/pgSQL output variable.
-- Qualify table fields so Postgres never treats them as the output variable.

create or replace function public.sync_vitan_entity_changes(
    p_workspace_id uuid,
    p_expected_snapshot_revision bigint,
    p_changes jsonb
)
returns table(revision bigint)
language plpgsql security definer set search_path = public as $$
#variable_conflict use_column
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

    select snapshot.revision, snapshot.payload into v_snapshot_revision, v_snapshot_payload
    from public.workspace_snapshots as snapshot
    where snapshot.workspace_id = p_workspace_id
    for update;
    if v_snapshot_revision is null then raise exception 'workspace_not_found'; end if;
    if v_snapshot_revision <> p_expected_snapshot_revision then raise exception 'workspace_revision_conflict'; end if;

    for v_change in select * from jsonb_to_recordset(p_changes) as change_set(
        entity_type text, entity_id text, payload jsonb, expected_revision bigint, deleted boolean
    ) loop
        select entity.revision into v_current_revision
        from public.workspace_entities as entity
        where entity.workspace_id = p_workspace_id
          and entity.entity_type = v_change.entity_type
          and entity.entity_id = v_change.entity_id
        for update;

        if v_current_revision is null then
            if coalesce(v_change.expected_revision, 0) <> 0 then
                raise exception 'entity_revision_conflict:%:%', v_change.entity_type, v_change.entity_id;
            end if;
            insert into public.workspace_entities(workspace_id, entity_type, entity_id, payload, revision, deleted, updated_by)
            values (p_workspace_id, v_change.entity_type, v_change.entity_id, v_change.payload, 1, coalesce(v_change.deleted, false), auth.uid());
        else
            if v_current_revision <> coalesce(v_change.expected_revision, 0) then
                raise exception 'entity_revision_conflict:%:%', v_change.entity_type, v_change.entity_id;
            end if;
            update public.workspace_entities
            set payload = v_change.payload,
                revision = v_current_revision + 1,
                deleted = coalesce(v_change.deleted, false),
                updated_by = auth.uid(),
                updated_at = now()
            where workspace_id = p_workspace_id
              and entity_type = v_change.entity_type
              and entity_id = v_change.entity_id;
        end if;
    end loop;

    v_next_payload := public.compose_vitan_snapshot(p_workspace_id, v_snapshot_payload);
    update public.workspace_snapshots
    set revision = v_snapshot_revision + 1,
        payload = v_next_payload,
        updated_by = auth.uid(),
        updated_at = now()
    where workspace_id = p_workspace_id;
    insert into public.workspace_snapshot_history(workspace_id, revision, payload, saved_by)
    values (p_workspace_id, v_snapshot_revision + 1, v_next_payload, auth.uid());
    delete from public.workspace_snapshot_history where workspace_id = p_workspace_id and revision not in (
        select history.revision from public.workspace_snapshot_history as history
        where history.workspace_id = p_workspace_id
        order by history.revision desc
        limit 6);
    update public.workspaces set updated_at = now() where id = p_workspace_id;
    return query select v_snapshot_revision + 1;
end;
$$;

revoke all on function public.sync_vitan_entity_changes(uuid, bigint, jsonb) from public;
grant execute on function public.sync_vitan_entity_changes(uuid, bigint, jsonb) to authenticated;
