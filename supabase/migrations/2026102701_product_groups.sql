-- Include standalone product groups when rebuilding snapshots from delta entities.
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
    'productGroups', coalesce((select payload from active where entity_type = 'product-groups' and entity_id = 'root'), p_fallback->'productGroups', '[]'::jsonb),
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
