create extension if not exists pgcrypto;

alter table public.workspaces drop column if exists invite_code;

create table if not exists public.workspace_snapshot_history (
    workspace_id uuid not null references public.workspaces(id) on delete cascade,
    revision bigint not null check (revision > 0),
    payload jsonb not null,
    saved_by uuid not null references auth.users(id) on delete restrict,
    saved_at timestamptz not null default now(),
    primary key (workspace_id, revision)
);

insert into public.workspace_snapshot_history(workspace_id, revision, payload, saved_by, saved_at)
select workspace_id, revision, payload, updated_by, updated_at
from public.workspace_snapshots
on conflict (workspace_id, revision) do nothing;

alter table public.workspace_snapshot_history enable row level security;
revoke all on public.workspace_snapshot_history from anon, authenticated;
grant select on public.workspace_snapshot_history to authenticated;

do $$
begin
    if not exists (
        select 1 from pg_policies
        where schemaname = 'public' and tablename = 'workspace_snapshot_history'
          and policyname = 'members read snapshot history'
    ) then
        create policy "members read snapshot history" on public.workspace_snapshot_history for select to authenticated
        using (public.is_vitan_member(workspace_id));
    end if;
end;
$$;

drop function if exists public.join_vitan_workspace(uuid);
drop function if exists public.create_vitan_workspace(text, jsonb);

create function public.create_vitan_workspace(p_name text, p_payload jsonb)
returns table(workspace_id uuid, revision bigint)
language plpgsql security definer set search_path = public as $$
declare v_workspace_id uuid;
begin
    if auth.uid() is null then raise exception 'authentication_required'; end if;

    select id into v_workspace_id
    from public.workspaces
    where owner_id = auth.uid() and name = trim(p_name)
    limit 1;

    if v_workspace_id is not null then
        select workspace_snapshots.revision into revision
        from public.workspace_snapshots
        where workspace_snapshots.workspace_id = v_workspace_id;
        workspace_id := v_workspace_id;
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
    return query select v_workspace_id, 1::bigint;
end;
$$;

create or replace function public.publish_vitan_snapshot(p_workspace_id uuid, p_expected_revision bigint, p_payload jsonb)
returns table(revision bigint) language plpgsql security definer set search_path = public as $$
declare v_revision bigint;
begin
    if auth.uid() is null or not public.is_vitan_member(p_workspace_id) then
        raise exception 'workspace_access_denied';
    end if;
    select revision into v_revision from public.workspace_snapshots where workspace_id = p_workspace_id for update;
    if v_revision is null then raise exception 'workspace_not_found'; end if;
    if v_revision <> p_expected_revision then raise exception 'revision_conflict'; end if;
    update public.workspace_snapshots
    set revision = v_revision + 1, payload = p_payload, updated_by = auth.uid(), updated_at = now()
    where workspace_id = p_workspace_id;
    insert into public.workspace_snapshot_history(workspace_id, revision, payload, saved_by)
    values (p_workspace_id, v_revision + 1, p_payload, auth.uid());
    delete from public.workspace_snapshot_history
    where workspace_id = p_workspace_id
      and revision not in (
          select revision from public.workspace_snapshot_history
          where workspace_id = p_workspace_id
          order by revision desc
          limit 6
      );
    update public.workspaces set updated_at = now() where id = p_workspace_id;
    return query select v_revision + 1;
end;
$$;

revoke all on function public.create_vitan_workspace(text, jsonb) from public;
revoke all on function public.publish_vitan_snapshot(uuid, bigint, jsonb) from public;
grant execute on function public.create_vitan_workspace(text, jsonb) to authenticated;
grant execute on function public.publish_vitan_snapshot(uuid, bigint, jsonb) to authenticated;
