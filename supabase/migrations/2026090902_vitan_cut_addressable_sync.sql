create table if not exists public.workspace_entities (
    workspace_id uuid not null references public.workspaces(id) on delete cascade,
    entity_type text not null check (char_length(entity_type) between 1 and 80),
    entity_id text not null check (char_length(entity_id) between 1 and 160),
    payload jsonb not null,
    revision bigint not null default 1 check (revision > 0),
    deleted boolean not null default false,
    updated_by uuid not null references auth.users(id) on delete restrict,
    updated_at timestamptz not null default now(),
    primary key (workspace_id, entity_type, entity_id)
);

create index if not exists workspace_entities_workspace_updated_idx
on public.workspace_entities (workspace_id, updated_at desc);

alter table public.workspace_entities enable row level security;
revoke all on public.workspace_entities from anon, authenticated;
grant select on public.workspace_entities to authenticated;

create policy "members read workspace entities" on public.workspace_entities for select to authenticated
using (public.is_vitan_member(workspace_id));

create or replace function public.sync_vitan_entities(
    p_workspace_id uuid,
    p_expected_snapshot_revision bigint,
    p_changes jsonb,
    p_snapshot_payload jsonb
)
returns table(revision bigint)
language plpgsql security definer set search_path = public as $$
declare
    v_snapshot_revision bigint;
    v_current_revision bigint;
    v_change record;
begin
    if auth.uid() is null or not public.is_vitan_member(p_workspace_id) then
        raise exception 'workspace_access_denied';
    end if;

    select revision into v_snapshot_revision
    from public.workspace_snapshots
    where workspace_id = p_workspace_id
    for update;
    if v_snapshot_revision is null then raise exception 'workspace_not_found'; end if;
    if v_snapshot_revision <> p_expected_snapshot_revision then raise exception 'workspace_revision_conflict'; end if;

    for v_change in
        select * from jsonb_to_recordset(p_changes) as change_set(
            entity_type text,
            entity_id text,
            payload jsonb,
            expected_revision bigint,
            deleted boolean
        )
    loop
        select revision into v_current_revision
        from public.workspace_entities
        where workspace_id = p_workspace_id
          and entity_type = v_change.entity_type
          and entity_id = v_change.entity_id
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

    update public.workspace_snapshots
    set revision = v_snapshot_revision + 1,
        payload = p_snapshot_payload,
        updated_by = auth.uid(),
        updated_at = now()
    where workspace_id = p_workspace_id;

    insert into public.workspace_snapshot_history(workspace_id, revision, payload, saved_by)
    values (p_workspace_id, v_snapshot_revision + 1, p_snapshot_payload, auth.uid());
    delete from public.workspace_snapshot_history
    where workspace_id = p_workspace_id
      and revision not in (
          select revision from public.workspace_snapshot_history
          where workspace_id = p_workspace_id
          order by revision desc
          limit 6
      );

    update public.workspaces set updated_at = now() where id = p_workspace_id;
    return query select v_snapshot_revision + 1;
end;
$$;

revoke all on function public.sync_vitan_entities(uuid, bigint, jsonb, jsonb) from public;
grant execute on function public.sync_vitan_entities(uuid, bigint, jsonb, jsonb) to authenticated;
