# Maze Swarm

Unity project for the current Maze Swarm game, including code, scenes, UI, audio, imported models, textures, fonts, project settings and their Unity metadata.

## Open the project

Use Unity **6000.6.2f1**, as pinned in `ProjectSettings/ProjectVersion.txt`, and open this repository root. The main scene is `Assets/Maze/Scenes/Main.unity`; the sandbox is `Assets/Maze/Scenes/Sandbox.unity`.

`Packages/manifest.json` and `Packages/packages-lock.json` retain the Unity registry/built-in dependencies, including URP, uGUI and glTFast. Unity and those packages are required; the generated `Library` directory is not checked in. Keep all tracked `.meta` files when moving assets.

The current game loads its distributable content from `Assets`. It does not require the original author's `/Volumes` asset library or Codex workspace to run. All existing GLB/glTF external image and buffer references were checked against files inside this repository before publication.

## Source artwork and authoring

`Characters`, `Textures/Source`, `Tools`, `Concepts`, `Art Direction` and `Design` retain source artwork, generation recipes, design references and project review captures. Selected editable Engineer, Lavender Spitter, cargo enemy family and cargo prop sources are additionally retained under `Design/SourceAssets`; its manifest records SHA-256 hashes. The imported runtime versions under `Assets` are authoritative for the current game.

The Engineer gait scene retains all seven actions with the original material. Its selected production palette is in `Design/SourceAssets/Engineer/palette`; the final compact GLB is `Assets/Maze/Resources/Models/hero_engineer_chibi.glb`. Production integration contracts and byte-preservation proofs are retained beside the authoring scene.

Authoring scripts and historical receipts retain original source/output paths as provenance. Redirect those paths to an isolated working directory before regenerating assets. They are optional authoring tools, not startup dependencies. Editing the supplied `.blend` scenes or re-exporting GLB assets requires Blender and, for some numeric authoring scripts, Python/NumPy. No Blender or Python process is needed to open or run the imported game in Unity.

`Tools/prep_sfx.py` can optionally remaster cues from separately installed sound libraries. The required mastered clips are already in the repository. The full external commercial sound libraries, Hunyuan model weights, raw inference experiments and duplicate external ZIP packages are not needed by the game and are not copied here. See `Sounds/CREDITS.txt`, `Textures/CREDITS.txt` and asset-specific documentation for provenance.

## Files intentionally kept local

`.gitignore` excludes generated Unity caches, logs, build/Xcode output, local snapshots, phone/save backups, user settings, macOS metadata, Blender autosaves, Python caches and local credentials/signing material. Editable `.blend` files and deliberate design/review captures remain versioned. Exclusions are preserved on disk; publication does not delete them.

The repository uses regular Git blobs. There was no existing Git LFS policy or configured LFS client at publication. All new individual files are below 100 MiB; several original art-source archives are larger than 50 MiB. No history rewrite or LFS migration was performed.

Publishing this snapshot does not launch Unity, build the game, operate a phone or modify saved data. Prior gameplay evidence includes automated QA routes using demo movement and invulnerability; those are not unassisted player victories.
