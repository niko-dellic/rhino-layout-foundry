# Roadmap

1. Qualify the macOS beta candidate against TESTING_AND_RELEASE.md, including safety, physical PDF output and the 200-sheet/1,000-detail fixture.
2. Hand the matching Windows candidate and repeatable checklist to the maintainer for native Windows testing.
3. Approve exact candidate hashes only after applicable platform gates pass; publish separately. No development installation implies publication.
4. After beta feedback, fix measured defects and qualify stable v1.

## Changes from the original milestone roadmap

The beta retains organization, editing, appearance states, built-in title blocks and ordered PDF output. Creation and Canvas capabilities from later milestones are also included.

- Custom title-block definition registration, mapping and relinking are outside the current scope; managed blocks are None/Right/Bottom.
- Live registered sheet/detail sources replace stored template recipes. Shared template-library browsing is deferred; portable packages provide exchange.
- Reusable appearance states/local rules replace the old ordered display-rule model and dedicated command.
- Native layout operations without reliable Rhino Undo disclose that limitation and use compensation/recovery. A universal one-Undo promise does not apply.
- Named PDF presets and an interruptible publishing UI are deferred. Current output uses hierarchy ordering and safe temporary-file replacement; cancellation is checked at native-call boundaries.
- Candidates are hash-verified. The original signed-prerelease promise is not fulfilled by checksums; publisher signing remains a separate unresolved distribution decision, and no signature is claimed.

The original milestone roadmap remains in history/ROADMAP_PRE_V1.md for context. README and the executable release checklist own current capabilities and acceptance gates.
