-- Read-only, anonymous availability probe. No access to user data or auth tokens.
create or replace function public.vitan_cloud_health()
returns boolean language sql stable security invoker set search_path = '' as $$
    select pg_catalog.current_database() is not null;
$$;
revoke all on function public.vitan_cloud_health() from public;
grant execute on function public.vitan_cloud_health() to anon, authenticated;
notify pgrst, 'reload schema';
