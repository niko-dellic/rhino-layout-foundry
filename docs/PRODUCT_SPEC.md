# Product scope for v1

README owns current capabilities and limitations. TESTING_AND_RELEASE owns acceptance evidence. This document defines the scope being prepared for launch.

## Organization and navigation

Foundry presents Rhino layouts through List, Thumbnail, and Canvas views in one panel. Folders organize sheets and appearance states; details remain owned by their Rhino page. Selection, notes, ordering, filtering, and hierarchy operations use stable native or Foundry identities. Canvas camera and interaction state remain session-only.

## Creation and templates

Create layouts from built-in arrangements or live registered sheet/detail sources. One checkbox registers a source; folders have no template role. Each creation specification owns quantity, paper dimensions/units, per-detail named views, display modes, appearance assignments, and a built-in title-block choice. Snapshots derive recipes from current source geometry. There is no stored recipe library, capture dialog, or live capability-link system.

## Appearance and title blocks

Reusable appearance states and local rules govern per-viewport layer visibility and object display overrides. Title blocks are None, Right, or Bottom. Project information supplies shared content; revisions belong to individual sheets. Foundry identifies only its generated blocks as managed. Custom instance selection, definition recipes, field mappings, and project-level default revisions are outside v1. Ordinary page blocks remain supported as native geometry in package transport.

## Persistence and exchange

Document schema 16 and package format 6 are the only supported formats. Historical projects/packages have no compatibility or conversion requirement. Invalid and unsupported metadata remain protected against destructive writes, with recoverable original envelopes preserved. Required collections and structural integrity are validated before use. Tags, tag filters, and tag naming tokens are removed.

PDF export follows selected hierarchy order. Packages retain native page geometry and dependency checksums, conflict decisions, import compensation, cancellation, and recovery packages.

## Automation and release quality

Automation remains experimental, additive/assignment-oriented, and dependent on trusted companion approval. It uses canonical creation specifications and no compatibility aliases. Core, Rhino, UI, and Extensibility retain their current responsibilities.

Windows and macOS are public-release targets. Native save/reopen, cancellation, Undo policies, recovery, equivalent metric/imperial output, keyboard/theme/high-DPI behavior, and the 200-sheet/1,000-detail fixture must pass on the exact distribution candidates. Historical checks do not certify changed binaries. See TESTING_AND_RELEASE for executable gates.
