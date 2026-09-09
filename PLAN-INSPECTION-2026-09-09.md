# Plan, inspection and attachments candidate

Installed coordinated preview.29 bundles after user confirmed Rhino was closed.
Rhino was fully restarted. All installed DLL/RHP hashes match isolated builds;
both builds had zero warnings/errors. Final suites: 448 public, 57 AI, 4 shared
UI and 15 primitives (524 total), all passed. No paid API requests.

Native tests found that Eto Label measurement with maximum height -1 returned
zero. Preview.29 uses a finite maximum height for Markdown, chat messages and
growing editors, plus actual control bounds. Long native Markdown paragraphs
now expand in the document without inner scrollbars in the offline fixture.
Windows/light-theme and pointer-wheel acceptance are not claimed.

AI changes: readable legacy-layout summary including summed quantities and
detail counts; explicitly labelled optional revision input; complete drawing-set
batches support empty-view placeholder sheets. New model calls omit the legacy
separate-layout tool and instruct staging one complete agreed batch. This does
not grant blanket authority for new out-of-plan work.

Read-only inspect_model_geometry reports measured horizontal planar Brep-face
Z values and extents, bounded to 5000 objects/1000 faces. Hidden objects/layers
and paper-space content are excluded. Meshes/instances are not measured. Values
are candidate surfaces, not automatic finished-floor classifications. Arbitrary
agent camera repositioning is NOT implemented.

Composer supports selecting/removing PNG/JPEG files, up to eight and 10 MB each,
and serializing them with the user message. Native picker opening was verified;
it was cancelled without sending. Clipboard paste, drag/drop and a thumbnail
gallery are NOT implemented. Native selection/send/remove checks remain.

Core/AI regression tests: 448 + 57 passed, including blank batch sheets,
quantity summaries and attachment message delivery. No paid API calls.

Final candidates: /private/tmp/foundry-ui29/Debug/net8.0 and
/private/tmp/foundry-ui29-ai/Debug/net8.0. Native geometry inspection returned
376 horizontal faces from readiness.3dm, without truncation or model mutation.
Fixture /private/tmp/foundry-plan27-check.py uses installed assemblies despite
its historical name. Geometry results: /private/tmp/foundry-geometry27-result.json.
Complete-plan native creation/approval still requires an end-to-end test.
