# Background Cloud Availability Check

On normal production startup, VitanCut checks for the current user's `VitanCut.CloudHealth-<user hash>` Windows Task Scheduler task. A missing, disabled or obsolete definition is registered and started once. An unchanged task is not recreated, so opening the application does not postpone its schedule. Development and UI-review builds do not register tasks or contact the production endpoint.

The task runs every three days as the current Windows user, without elevation or a saved password. The user must be logged in. `StartWhenAvailable` enables catch-up after missed scheduled times, network availability is required, concurrent instances are ignored, and failed runs can retry three times at 30-minute intervals. The task cannot run while the PC is off. Moving an installation updates the executable path when that copy next starts.

## Server Setup Required

Migration `supabase/migrations/2026102702_cloud_health.sql` was applied on 2026-10-27 to production project `oihkaipfyjpoekdzewzn` with user authorization via the Supabase SQL Editor. It adds `vitan_cloud_health()`, a security-invoker SQL function returning only a boolean, executable by `anon` and `authenticated`. It exposes no database contents. The schema cache was reloaded; a live GET with the application's publishable key returned HTTP 200 and `true`. SQL verification confirmed `security_definer = false` and execute permission for both client roles. No user tables were modified.

The scheduled executable runs with `--cloud-health-check`, without creating a splash or main window. It sends one GET request to `/rest/v1/rpc/vitan_cloud_health` with the existing publishable key and exits. It never loads the local database, reads or refreshes user tokens, publishes data, or marks synchronization successful. HTTP errors, missing RPCs, malformed responses and timeouts fail the check. The last result is written to `data/logs/cloud-health.json` under the resolved application data root.

This is an availability check, not synchronization or automatic recovery of a paused project. A three-day request interval does not guarantee protection from inactivity pausing; Supabase explicitly guarantees exemption only for paid plans: https://supabase.com/docs/guides/platform/free-project-pausing.

## Removal

Before deleting a portable installation, run its executable with `--remove-cloud-health-task`. This removes only the current user's task if it points at that executable, and does not open a window. Do not restart that installation afterward unless the task should be registered again.

Installer 1.1.4 in the separate `codex/installer` worktree (`../VitanCut-Installer-Release`) performs equivalent cleanup through Task Scheduler COM before scheduling file deletion. It verifies the per-user task name, executable path and exact health-check argument; disables and stops a running check; and removes only that installation's task. The main application is not started. Missing tasks are allowed for older installations. Cleanup errors or a 30-second timeout abort uninstall before application files and registry registration are removed.

## Verification

Core checks use fake Task Scheduler commands and an isolated HTTP transport. They do not register production tasks or send live cloud requests. SQL checks use PGlite in memory and verify anonymous execution without access to workspace snapshots or elevated privileges.

`scripts/Test-CloudHealthTask.ps1 -Executable <repository build exe> -TaskXml <core-checks cloud-health-validation.xml>` validates the generated XML using Windows Task Scheduler's `TASK_VALIDATE_ONLY` flag, without registering a task. It also runs both headless commands with isolated Development paths and verifies that no window or database is created. These Windows checks passed locally. Installer contract checks pass on both Windows PowerShell 5.1 and PowerShell 7 with scheduler mocks, covering owned-task removal, other-installation preservation, missing tasks and access failures. The production installer build is 414,208 bytes and passes DPI/layout checks. A real installed-program removal was not performed against the user's working installation.
