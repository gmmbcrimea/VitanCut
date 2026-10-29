# Product Editor Performance Checks

Local development-only profiling, 2026-10-29. Production data and cloud services were not modified.

## Changes

- Append a detail row instead of rebuilding all existing editor controls.
- Create material flyouts only when used; reuse catalog options until the database revision, database instance or UI language changes.
- Bound material lists to a scrollable viewport to retain item virtualization.
- Debounce search for 120 ms; flush pending search before keyboard selection.
- Pass outside pointer input through flyout overlays so a single click can dismiss the menu and activate Add or another field.
- Cancel deferred material-focus restoration after newer pointer or keyboard input.
- Reuse row calculations in summaries and formula suggestions; skip unchanged formula labels.
- Clip row contents to their bounds so cursor reveal can skip offscreen subtrees.

## Observations

An isolated stress project contained 120 details and 600 additional materials.

- Add was clicked once while the material flyout was open: the flyout closed and a new row appeared.
- Add handler elapsed time: 26.79 ms in one diagnostic sample (not complete presentation latency).
- Material picker preparation: 24.80 ms average, 69.21 ms maximum across three samples; expansion handler: 1.27 ms in one sample. These measurements exclude subsequent template rendering.
- Reveal refresh before row clipping: 284.71 ms average, 341.50 ms maximum across four retained samples.
- Reveal refresh after row clipping: 7.03 ms average, 14.32 ms maximum across four retained samples. Pointer paths differed; these are diagnostic observations, not a controlled benchmark.
- Before the second pass, initial full editor row creation/layout remained about 1.74 seconds for 120 details.
- 757 core checks passed and the WinUI build succeeded. NuGet vulnerability-feed lookup was unavailable locally (NU1900).

## Remaining Manual Checks

The native automation helper could not reliably target the owned popup window for every input. Complete the formula-menu Add scenario, fast typing, rapid material-to-field transitions, and selection/scrolling through a large expanded material category manually before claiming complete UX verification.

## Second Pass: Virtualized Rows

- Replaced the persistent StackPanel of all editable controls with an ItemsRepeater and StackLayout. All DetailRow models remain available for calculations; only the viewport/cache regions get controls.
- Releasing a row closes its flyouts and removes only its own formula controls from the editor dictionaries. Recreating the view reads the retained row model.
- Automatic edge rows now use observable collection updates rather than adding/removing visual children directly.
- Removed forced full-list layout from RefreshDetails and disabled animated travel through the entire list after Add.
- Preserve toolbar focus when its buttons move into or out of the sticky toolbar. Ensure the requested virtual row is arranged before bringing it into view.
- Added a development-only Add-to-next-Rendering measurement; it includes layout work but is not a physical display latency measurement.
- First virtualized stress run: Add handler 5.42 ms; Add to next Rendering 100.01 ms (one sample). Summary mean 2.81 ms; reveal mean 9.44 ms across three retained samples. Realization timing is separate from the row-source setup measurement.
- Wheel scrolling was exercised after a five-second pause. Full input-to-display latency is not established by the native automation helper.
- The isolated preview is retained under `.test-runtime/virtual-editor-preview`; `Start-Test.cmd` restores the development data-root settings when reopened.

## Follow-Up: Empty Viewport During Continuous Scrolling

User testing found that the virtualized list could show blank space until scrolling paused. The horizontally scrolling inner ScrollViewer was removed from the repeater's ancestor chain. Horizontal table navigation uses a clipped Grid, TranslateTransform and a separate native scrollbar instead. The repeater's vertical cache was increased from one to three viewport lengths. The last build and 757 core checks passed.

Automatic visual checks exercised large downward scrolls (rows 40-55 and 89-103), upward scrolling (rows 16-31), another downward scroll after a five-second pause, and the end of the list (rows 106-120). All captured viewports contained their rows without a persistent empty area. Snapshots after input do not establish the absence of transient blank compositor frames during continuous movement or measure full input-to-display latency. The test card remains open for user verification. Horizontal scrollbar behavior at narrow widths still needs manual verification.

The updated isolated preview is under `.test-runtime/scroll-viewport-preview`. The previous `virtual-editor-preview/Start-Test.cmd` forwards to its launcher to avoid reopening the superseded preview.

## Latency Pass

- Formula suggestion flyouts/lists and detail context menus are created on demand instead of during every row realization.
- Preparing a virtual row updates only its selection border, not all realized row borders.
- Reduced vertical cache from three to half a viewport after removing the nested ScrollViewer. This avoids realizing large batches of editable controls during a jump; captured stress viewports remained populated.
- Reveal creates composition masks only within the light's reach plus a 64-pixel margin. Pointer movement beyond 32 pixels schedules a refresh. Edge geometry and the compositor-driven light position are unchanged.
- Added development-only wheel-handler-to-next-Rendering timing. This starts after input reaches the handler, not at physical wheel movement, and does not prove that the newly scrolled content has reached the display.

Two diagnostic runs used the same 120-detail/600-extra-material fixture. Before the cache/light-region changes: Add handler 14.47 ms, Add-to-Rendering 550.80 ms, row realization mean 17.76 ms (three retained samples), reveal mean 41.59 ms (three retained samples). After: Add handler 5.58 ms, Add-to-Rendering 92.85 ms, row realization mean 2.00 ms, reveal mean 6.43 ms (three retained samples each). These small sequential runs are not controlled benchmarks or percentile estimates.

Large injected 1800-unit scroll batches measured 517.45 ms and 482.44 ms respectively; these include work across a batch and cannot isolate first-wheel latency. Three separate 120-unit steps after five-second pauses in the final preview measured 61.71, 7.36 and 0.22 ms to the next Rendering callback. This does not establish physical input-to-display latency or eliminate the need for manual continuous-scroll checks.

The final build succeeded; 757 core checks passed during this pass. NuGet vulnerability-feed access remained unavailable (NU1900). Formula popup typing/Tab verification remains manual because the helper reports the main-window field as focused while the owned editor field is visually focused. The isolated preview is left open and can be reopened with `.test-runtime/latency-preview2/Start-Test.cmd`; production data and cloud were not used.

The latency goal remains open pending user verification, especially continuous scrolling, formulas, material changes, and repeated Add on a real large product. Narrow-width horizontal scrolling also remains a manual regression check.

## Bounded Detached Row Cache

Closed the previous LatencyPreview and LatencyDiagnostics windows and prepared one EditorCachePreview under `.test-runtime/editor-cache-preview`. A maximum of 24 detached row views is retained for reuse by the same DetailRow model. Their popups close when detached; evicted views release formula dictionaries and visual references. Full source refreshes and removed generated edge rows invalidate cached views. This is not cross-model control recycling: first visits still construct editable controls, which remains an optimization opportunity.

Stress checks: down 1200 units, back 600 units, a 120-unit step after five seconds, then Add. Captured viewports were populated. Retained row reuse samples were 0.39/0.42/0.42 ms. Add-to-Rendering was 117.90 ms, so this pass does not demonstrate improvement in Add over the previous 92.85 ms sample. In the isolated Kassa product, scrolling mixed units/formulas/edge rows and one-click Add with the material picker open succeeded; Add-to-Rendering was 66.97 ms. These are small diagnostic samples, not physical display latency or controlled benchmarks. 759 core checks passed and WinUI publication succeeded.

The Kassa check exposed a partially obscured new last row after Add. The subsequent build removes the forced synchronous UpdateLayout and adds a low-priority clipped-bottom correction after BringIntoView, cancelled by newer pointer/key/wheel input. This last correction has built successfully but still requires rendered verification. The previous two preview processes were closed; the new test launcher uses only isolated Development data.

## Unexpected Exit Investigation

The user reported that LatencyPreview2 disappeared without being closed manually. No matching Application Error/.NET Runtime/WER entry was found in the inspected Windows event window, and the isolated data root contained no errors.log. This does not prove a clean exit; the cause is unresolved.

Added development-profile-only lifecycle records (fixed stage, UTC timestamp, PID, exit code, exception type/HRESULT; no exception messages or product names). Records cover application start/exit request, managed/UI exceptions, main-window closing/closed and product-window opened/closed. Diagnostic build: `.test-runtime/latency-diagnostics/Start-Test.cmd`; logs: its isolated `data/logs/ui-lifecycle.jsonl`. 759 core checks passed, including disabled diagnostic no-write behavior and omission of private error text. WinUI publication succeeded.

The diagnostic process remained running through a bounded 120-second observation, then adding the 121st row, scrolling upward by 3000 units and opening/closing the on-demand row context menu. No spontaneous exit reproduced in these checks. Native termination can bypass the internal logger. A proposed external PowerShell watcher was blocked by the existing execution policy and was not enabled or bypassed. The diagnostic card is left open pending the user's reproduction steps. Process checks must run in the same host context as the elevated test launch; sandbox-only enumeration cannot establish that a host process has exited.

## Cross-Model Row Recycling

Supersedes the bounded detached same-model cache above. Native repeater containers retain their complete control subtree and bind it to different DetailRow models. Unbinding clears model-to-view references, closes popups, invalidates pending image/focus work and releases formula bindings. Formula flyouts remain associated with reusable TextBoxes; event handlers resolve the current binding. Display-only binding events skip unchanged values and do not recalculate the product.

On the isolated 120-detail/600-extra-material fixture, 28 row views were created and reused 468 times. ProductSummary ran twice (opening and one actual formula edit), not on scrolling. After saving, a stable-ID comparison confirmed only detail 050's length expression changed to `= sqrt(40000) + 25`; the other compared names, sizes, quantities, materials and rotation values were unchanged. Retained reuse samples were 1.33/1.29/1.22 ms. These are small diagnostic samples, not physical input-to-display measurements. Add 121 and a wheel step after a five-second pause showed populated viewports. Native mixed-unit/material/menu/focus regressions still require coverage; do not treat historical observations above as validation of every subsequent build.

## Row Selection and Background Focus

Focus entering a realized detail row selects its current model, including keyboard navigation. Empty-space taps move focus out of text/number inputs to a neutral target and select the clicked row when present. The target becomes focusable only for this transfer and leaves the tab sequence when focus leaves it. Native controls keep their default pointer/focus behavior. Selection updates only the previous and new row borders, without a product recalculation. Release build and 760 core checks passed; these core checks do not simulate native focus routing. Rendered checks caught ScrollViewer.Focus redirecting focus to the first input and native pointer focus overriding an early transfer; the final implementation uses a neutral button target after a completed tap. Verified on the final preview: clicking detail 002's length selects row 002; clicking the empty top padding of row 004 removes input focus and selects row 004 without changing the scroll offset. The single isolated preview remains open for manual checks, especially keyboard/flyout interactions.
