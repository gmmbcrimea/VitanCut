# Cutting workflow

## Optimization

The primary report contains only sheet materials whose name includes `ДСП`
(for example, `ЛДСП`). The primary report shows `Доп. раскрой` when a project
also contains other sheet materials. That opens an independent report for ДВП,
glass and other sheet materials without `ДСП` in the name. Each report has its
own four automatic variants, working changes, named snapshots and active plan.

`CutOptimization` compares four runs: two MaxRects heuristics and two guillotine
split heuristics. The primary objective is total sheet count across materials;
equal-count layouts favour concentrated occupancy. Each run explores alternative
part orders recursively, with depth 3, at most 28 search nodes per material/run,
and an 8-second budget for optional search. Initial packing passes always finish.
The UI remains responsive because calculation and edit geometry run off-thread.

This is bounded heuristic optimization, not an exhaustive proof of the global
minimum. Four results are retained even when some layouts coincide. The existing
double-precision baseline participates in comparison. Conservative integer
quantization in the library never reduces part sizes; when it prevents packing,
the exact baseline is retained and MaxRects labels identify this fallback.

The engine dependency is RectangleBinPack.CSharp 1.0.4, a C# port of the
MaxRects/Guillotine algorithms in https://github.com/juj/RectangleBinPack.
Package source: https://github.com/la667-j/RectangleBinPackingSharp.
Guillotine free rectangles are not merged. Rotation locks, texture, edge trim
and kerf apply to every generated result. Manual edits need not remain guillotine.

## Editing and persistence

Ctrl-click toggles selection. Dragging on blank sheet space selects with a
rectangle; Ctrl retains the existing selection. Dragging a selected part moves
the selection. A target-sheet selector also supports transfers to offscreen
sheets. Transfers between different materials are rejected.

Snapping considers sheet bounds and neighbour edges/corners. A conflicting drop
first searches for the nearest legal rigid translation; if the group cannot fit
rigidly, parts are placed near their desired positions individually. Overflow
creates sheet proposals immediately after the affected sheet. Rotation follows
the same rule. Oversized or rotation-locked parts reject the entire operation.
Cancelling confirmation never mutates the report or project.

Rotation of a group first tries aligned rows with several row lengths, retaining
the closest legal position near the group's original origin. Unselected parts
remain fixed. If obstacles prevent a complete row layout, individual placement
uses a common origin instead of each part's pre-rotation offset. Dragging still
preserves relative positions. The context menu distinguishes identical parts
on the current sheet from identical parts on all sheets. Overflow warnings name
the affected original material/sheet so an offscreen conflict is not misleading.

The four automatic plans are immutable in the editor. Edits are persisted as a
separate working plan; named saves create snapshots. Switching results does not
discard those plans. Undo is local to the current editing session. Export and
print use the displayed plan. Sheet membership and empty sheets are preserved.

Saved plans have an input signature covering instance IDs, dimensions, rotation
constraints, materials, trim and kerf. Stale plans remain in the list but cannot
be activated. Restoration validates completeness, dimensions and non-overlap.
An editor cannot overwrite changed project inputs or plans saved in another
report window. Legacy per-sheet edits migrate to a manual plan.

## Verification

Run `dotnet run --project native-winui-tests/VitanCut.CoreChecks.csproj -c Release`.
`CutWorkflowChecks` covers optimization, randomized mixed-material packing,
locks, kerf, nearest placement, snapping, group transfers, overflow insertion,
transactional cancellation, corrupt/stale saves, persistence and XLSX sheets.
The test creates `cut-ui.json` in its artifact directory for isolated native QA.

Native QA: select three full-height parts with a rectangle; rotate; cancel and
verify two sheets unchanged; repeat and confirm; verify new sheet 2 precedes
the old marker sheet. Save a named plan, switch to an automatic result and back.
Verify overlap drops, cross-sheet transfers and side-panel layout at different
window sizes. Ctrl selection requires a physical held-key gesture when the UI
automation runtime only offers complete key chords.
