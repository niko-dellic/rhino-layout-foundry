# Question interaction updates

## preview.25 — installed and native-tested

Suggested-answer release now stores the answer and advances immediately. The
last answer submits the complete set once, after the native input callback
returns. Suggested options never populate the custom editor; custom drafts are
stored separately. Enter or the contextual “Use custom answer” action commits
a custom answer. Shift+Enter remains available for a newline. Shared buttons
retain their transient pressed highlight without a persistent selection fill.

Both coordinated builds passed with zero warnings/errors. All 519 regression
tests passed. After user approval, Rhino was fully quit, both bundles installed,
all RHP/Core/UI/shared assembly SHA-256 hashes verified, and Rhino restarted.
The native offline fixture passed suggested-answer auto-advance, back navigation
with the custom field empty, and automatic final submission/disposal without a
crash. A second run passed custom-answer Enter progression and final submission;
its recorded result was `Custom roof answer | Monochrome (Recommended)`.
These checks exercise the installed control, not a paid model round trip.
Candidates: `/private/tmp/foundry-ui25/Debug/net8.0` and
`/private/tmp/foundry-ui25-ai/Debug/net8.0`. No API requests were made.

## preview.24 — previously installed and verified

Installed coordinated public/AI bundles, restarted Rhino, and verified SHA-256
matches for RHP, Core/UI/Extensibility and shared UI assemblies. No paid requests.

The 21:44 crash report shows an unhandled managed exception on the native mouse
event path. Code inspection found question controls synchronously disposed from
their own click callback. Navigation/page rebuilding and final submission now
run after that callback returns; editor closures capture the originating page
index. The crash report alone does not identify the precise managed throw site.

Native offline test, final installed build: clicked both option answers and
Submit, disposed the component on submission, and Rhino remained running.
Previous candidate also passed backward/forward answer retention. Visually
checked left-aligned question/answer rows, right-hand navigation with centered
counter, and the empty `Say something else...` placeholder. Test window closed;
no model geometry changes. Full AI-session completion, Windows, light theme and
keyboard-only acceptance are not claimed.

Initial images previously disappeared after the first provider response, even
when it only requested a tool. They now survive the entire tool sequence until
assessment. Regression test confirms that behavior. Exact image-and-title
duplicates are reported as reuse rather than another preview gallery. This
does not prohibit genuinely changed captures or claim that all provider-driven
recapture has been eliminated; no paid-provider replay was run this pass.

Tests: 447 public, 53 AI, 4 shared UI and 15 primitives passed (519 total).
Both builds had zero warnings/errors; package validation and diff checks passed.
Native fixture: `/private/tmp/foundry-question23-check.py` (loads installed build,
despite its historical filename). Result: `/private/tmp/foundry-question23-result.txt`.
