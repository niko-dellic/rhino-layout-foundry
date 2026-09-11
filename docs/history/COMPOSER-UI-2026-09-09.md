# Composer UI candidate — installed, native acceptance pending

> Historical development snapshot. Status, candidate versions and local evidence paths below reflect this dated session; they are not current release acceptance. See the [current clean-break status](../PRE_BETA_CLEAN_BREAK.md) and [testing and release guide](../TESTING_AND_RELEASE.md).

Implemented shared preview.22 question paging in the composer, answer buttons
with full-text tooltips, retained answer drafts during paging, final submission,
and a collapsed answer summary. Added bounded growing custom-answer and normal
composer inputs. Quiet disclosure headings now share the body inset. Read-only
Markdown wheel events forward to the containing conversation.

Capture fingerprint failures now discard cache and attempt a fresh capture;
full exceptions go to diagnostic tracing. This is a defensive fix, not proof of
the reported null exception's exact origin.

Added an offline scripted provider, Debug provider injection and a read-only
three-question/capture scenario. No paid API requests this pass. Creation,
correction and cancellation fixture coverage still needs expansion.

Candidates: `/private/tmp/foundry-composer22/Debug/net8.0` and
`/private/tmp/foundry-composer22-ai/Debug/net8.0`. Shared packages validated and
both consumers pinned together. Installation subsequently authorized and completed:
Rhino was at its welcome screen with no document open, was fully quit before
replacement, and reopened afterward. SHA-256 comparisons passed for both RHPs,
Core/UI/Extensibility and all shared UI assemblies against the isolated builds.
No API requests were made during installation.

Public tests 447, AI tests 52 pass. Builds zero warnings/errors and repository
diff checks pass. Native scrolling, compact heights and keyboard flow remain
unverified, as do actual capture recovery and Windows/theme/DPI acceptance.
