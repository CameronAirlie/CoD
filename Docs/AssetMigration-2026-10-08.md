# CoD asset migration — 8 October 2026

## What changes when using the project

The project now uses asset format version 3. Keep model sources and their
`.plutometa` sidecars together in version control. A sidecar stores the asset's
identity, import settings and persistent sub-object identities; deleting it can
break references even when the source filename stays the same.

New model imports produce meshes, materials, animations and hierarchy data in
`Library`, rather than creating editable generated copies beside the source.
Treat those imported objects as read-only. Use material remaps or extract an
authored copy when you need independent edits. Source changes trigger reimport;
the previous successful generation stays available if the replacement fails.

Scene and prefab references can use `asset://<id>#<local-id>` instead of a
filename. Move assets with their sidecars, preferably through the editor, to
retain identity. `Library` is disposable and ignored by Git; rebuild imports
before running a source project if that cache is removed.

## Compatibility decisions for this project

Existing editable native meshes, materials and animations remain authored
assets at their original locations. Gameplay continues to use those assets,
preserving edits made after the old imports. The 19 source models import into
the separate virtual namespace `project://ImportedModels/<source-id>/`;
their actual generated payloads live in Library. Small authored material
override files can exist under Assets/ImportedModels.

Existing source IDs and matching legacy sub-object IDs are retained. Import
settings and material remaps are persisted. A missing Soldier source sidecar
was created. The obsolete grass_green/scene.gltf sidecar was quarantined after
the confirmed rename to grass_green.gltf; both active grass and scene IDs stay
unchanged.

194 typed references across 25 scene, prefab and material files were converted
to stable IDs. Dynamic sound-directory strings and script asset paths retain
their compatible path semantics. Export with all assets included until dynamic
script dependencies have an explicit pruning inventory.

The C# project now accepts a PlutoGERoot MSBuild property and defaults to this
machine's engine checkout. ScriptCore and CoD managed assemblies were rebuilt.
The runtime initializes a read-only asset catalog for source projects as well
as loading the catalog supplied with exported games.

## Recovery

The verified pre-conversion copy is:

`D:/PlutoProjects/CoD/.pluto-migration-backups/2026-10-08-1791468004973/Original`

`backup-manifest.json` records SHA-256 values for 5,823 original files.
`publication-journal.json` records changed paths and their before/after hashes.
Retain this recovery directory until satisfied with the migrated project.
It is excluded from scans, cooking and Git, and is outside disposable Library.

To restore journaled project files, close the editor/runtime and run from the
engine checkout:

```powershell
& out/publish-cod-migration.ps1 --rollback
```

The helper acquires the shared import lock, verifies recovery bytes and refuses
to overwrite files changed since the migration checkpoint. Resolve such a
conflict by comparing the current file with Original, rather than forcing a
restore. Library is a disposable cache and is not restored by this operation.
The helper is local tooling in the engine's ignored out directory; retain it
with this recovery copy if moving the backup to another machine. This is a
journaled project migration, not a guarantee of recovery from storage failure.

## Validation and limits

All 19 models imported successfully in both the isolated test copy and the
actual project. The final actual-project cache check reported every model
current; the audit found 825 assets, 1,147 metadata files and zero issues.
Vulkan title and Main gameplay scene benchmark smoke tests loaded and shut down
cleanly at the actual project root. The asset pipeline regression suite passed
all 19 tests after the runtime catalog fix (25.38 seconds).

A full standalone export was created at
`C:/Users/Cameron.Airlie/dev/PlutoGE/out/cod-export-2026-10-08/CoD.exe`.
It includes all eligible runtime assets, including dynamically loaded sounds.
Its 3,575,736,169-byte content pack passed integrity verification for all 1,611
packed files, and the exported game passed its title benchmark smoke test.
The export is local validation output, not a committed distribution artifact.

The managed project builds without warnings. The existing gameplay test suite
has a stale hardcoded hand-binding entity ID (22272) in HandSystemTests.cs;
the same assertion fails against the original scenes. It has not been weakened
to make this migration pass. Runtime smoke tests do not replace interactive
checks of every level, animation, input and gameplay action.
