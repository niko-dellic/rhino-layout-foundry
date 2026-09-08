# Companion workspace hosting

Create-menu invocation contexts expose `showWorkspace` as an
`Action<string, Eto.Forms.Control>`. A companion supplies a title and an Eto view;
the host supplies Back to layouts inside the companion workspace only. The normal
layout views have no companion navigation banner; reopen through the create menu. Reopening
the same view preserves it. Hiding a workspace does not dispose its session.
Companions must bind sessions to a document and dispose them when that document
closes. Do not cancel a run merely because the view receives UnLoad.

The `automationDispatch` delegate is bound to the document active when its context
was created. It rejects requests when another document is active. Mutation plans
still validate their document identity, source revision and single-use approval
token at apply time. Workspace hosting grants no mutation authority on its own.

`showWorkspaceWithToolbar` is an optional
`Action<string, Eto.Forms.Control, Eto.Forms.Control>` accepting a title, workspace
body and companion toolbar control. The host retains the Layout Foundry brand
header and places compact Back navigation beside companion actions in a fixed
toolbar. Form fields belong in the scrolling body, not this toolbar.

The normal pane and workspace share one content slot, including fullscreen mode;
two hidden expanding StackLayout children must not be used because AppKit can
reserve space for both. The AI companion remains an optional separate package.
