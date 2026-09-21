# PowerForge Studio visual review

This is the complete concept set for the Avalonia workspace: eight representative page patterns, not a mockup for every route, dialog, loading state, or viewport. The [product map](PowerForgeStudio.ProductMap.md) defines navigation and behavior. These images define visual hierarchy, spacing, typography, color, icons, tree density, and panel composition. All repository names, counts, dates, versions, paths, statuses, and output in the images are illustrative.

| Pattern | Concept | What to carry into the app |
|---|---|---|
| Workspace and files | [01](Assets/PowerForgeStudio/01-workspace.png) | Persistent project/working-copy tree, file list and preview, tabs, inspector, output dock |
| Project overview | [02](Assets/PowerForgeStudio/02-overview.png) | Project identity, detected entrypoints, prerequisites and recent evidence in compact cards |
| Changes and history | [03](Assets/PowerForgeStudio/03-changes-history.png) | Branch context, changed paths, readable diff, explicit stage and commit workflow |
| Build & Run | [04](Assets/PowerForgeStudio/04-build-run.png) | Reviewed source configuration, build-only intent, stages, artifacts and output |
| Releases | [05](Assets/PowerForgeStudio/05-releases.png) | Prepare, sign, destination review, publish, verify and saved receipts |
| Project GitHub | [06](Assets/PowerForgeStudio/06-project-github.png) | Issues, PRs, checks, discussion, changed files and draft-first actions |
| Workspace catalog | [07](Assets/PowerForgeStudio/07-activity.png) | Cross-project activity list and evidence inspector; Automations and Connections reuse the catalog pattern |
| Storage review | [08](Assets/PowerForgeStudio/08-storage.png) | Measured working copies, candidate evidence, guarded review and output |

## Concept screens

### 01 Workspace and files

![Workspace and files concept](Assets/PowerForgeStudio/01-workspace.png)

### 02 Project overview

![Project overview concept](Assets/PowerForgeStudio/02-overview.png)

### 03 Changes and history

![Changes and history concept](Assets/PowerForgeStudio/03-changes-history.png)

### 04 Build and Run

![Build and Run concept](Assets/PowerForgeStudio/04-build-run.png)

### 05 Releases

![Releases concept](Assets/PowerForgeStudio/05-releases.png)

### 06 Project GitHub

![Project GitHub concept](Assets/PowerForgeStudio/06-project-github.png)

### 07 Workspace Activity

![Workspace Activity concept](Assets/PowerForgeStudio/07-activity.png)

### 08 Storage review

![Storage review concept](Assets/PowerForgeStudio/08-storage.png)

The shared visual system is a navy 70 px rail and title band, white workspace, restrained blue selection, thin blue-gray dividers, compact vector icons, a roughly 325 px tree, contextual inspector, and dark output dock. Selected repository, selected working copy, selected file, and selected workspace route need distinct indicators. Keep controls that perform an action visually different from read-only status. Wide and compact layouts use the same design system; the inspector or preview may collapse when space is limited.

## Corrections before implementation

- The concepts show `Schedules` in the rail; the implemented workspace route is **Automations**. The project tab is **Build & Run**, while **Releases** is its separate project tab. Follow the product map for route names.
- The top search and tree filter must describe their actual search scope. A project-only filter must not claim to search commits, issues, or file contents.
- Overview detection, package versions, tool readiness and recent activity come from observed sources. The Overview image does not authorize defaulting these to green or inventing a release state.
- Changes must show staged and unstaged state accurately. Any discard operation needs its own guarded workflow. A commit always uses the selected working copy and explicit user-authored message.
- Build & Run keeps Build only separate from Publish release and Retry destinations. Retry uses verified artifacts where supported; it does not silently rebuild. Artifact types come from the reviewed plan, not from the pictured example.
- The Releases image mixes illustrative package names and claims readiness while its sample output says the build has not started. The app must derive readiness from its durable checkpoints. WinGet and Microsoft Store are distinct destinations. Never display credential values.
- GitHub actions use the captured repository/item and exact PR head. A review or comment opens a draft and explicit confirmation. The picture's PR, checks, and reviewer controls are illustrative, not proof of current provider capability.
- Activity totals and states are observed provider evidence with time and source, not synthesized success claims. Automations and Connections may have unavailable or partial provider states.
- Storage sizes are logical measurements, not guaranteed reclaimable space. Broken references belong in an Inspect/Repair group. Refresh remote, active-use and retained-artifact evidence before any removal.

## Review status

- [x] Eight representative concepts have one coherent shell, tree, colors and icon treatment.
- [x] The 53-page/108-state browser gallery is superseded as an implementation checklist.
- [x] Image-model inaccuracies are called out above; the product map remains the behavior contract.
- [x] A first shared Avalonia styling pass updated the title band, project filter order, typography, chips, inspector path density and output dock; fresh wide and compact Overview, Activity and Storage renders were inspected.
- [x] Storage and Activity evidence rows now pair shared vector state cues with their text labels; Storage summary cards use the same navigation icon system.
- [ ] Compare current Avalonia wide and compact renders for each pattern against this set.
- [ ] Close the remaining shell, density, inspector and output-dock differences in the running application.
- [ ] Capture final native Windows and headless evidence after the last visual change.

The fresh renders show the remaining gap clearly: page headings and data cards are still visually sparse beside the concepts; Storage and Activity need tighter row density and grouping despite their new state cues; the inspector repeats labels; and the compact Storage view needs deliberate scrolling to reach its lower rows. Preserve the accurate source and safety information while tightening those layouts. Do not replace these gaps with image-only decorations or unimplemented controls.
