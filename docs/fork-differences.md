# Fork Differences

This fork is based on:

- `nesrak1/UABEANext` `master` at `b2abfaa`
- `nesrak1/AssetsTools.NET` `main` at `790dae2`

The fork remotes are:

- `pescadordegoiaba/UABEANext`
- `pescadordegoiaba/AssetsTools.NET`

## Main UABEANext Changes

- Preserves AssetBundle compression and block/directory layout when saving modified bundles.
- Adds verbose workspace logging around asset/bundle loading and save decisions.
- Adds Unity component preview/edit support through `UnityComponentPlugin`.
- Adds material import/export and preview support through `MaterialPlugin`.
- Expands mesh import/export and preview support, including OBJ-related helpers.
- Adds Il2Cpp project probing and metadata dump integration for Cpp2IL workflows.
- Improves image preview, inspector, workspace explorer, and plugin loading behavior.
- Adds Linux packaging scripts, Arch packaging metadata, and updated Ubuntu workflow artifacts.

## AssetsTools.NET Changes

- Tracks original bundle compression and block directory placement in bundle instances.
- Distinguishes LZ4 fast and LZ4/HC flags when reading and writing UnityFS bundles.
- Preserves requested compression and directory placement during bundle packing.
- Fixes legacy serialized file metadata for Unity 4.x / format 9, including absent script type tables.
- Preserves legacy `bigIDEnabled` path ID layout for serialized file versions 7 through 13.
- Preserves original serialized file data offset when rewritten metadata still fits.
- Adds regression tests for bundle compression/layout and legacy serialized file metadata.

## Excluded From The Fork

The repository intentionally excludes local game dumps and generated build artifacts, including:

- `DeathRun Portable/`
- `bin/` and `obj/`
- extracted APK/native/game asset files

## Comparison Commands

Use these commands to refresh the inventory against upstream:

```bash
git diff --stat upstream/master
git diff --name-status upstream/master
git -C Libraries/AssetsTools.NET diff --stat upstream/main
git -C Libraries/AssetsTools.NET diff --name-status upstream/main
```
