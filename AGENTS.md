# Ranvier.Terminal

Keep the public design native F#: records, mutable records, discriminated unions and functions.
Do not add handwritten interfaces or runtime packages beyond Ranvier and the F#/.NET runtime
without discussing the constraint with the user. Terminal access uses System APIs and OS ABI calls.

Read README.md and VERIFICATION.md before changing behavior or making performance claims.
Keep setup, text preparation, reactive callbacks, input and resize outside the allocation claim.
Changes to layout, painting or output must pass the executable allocation gate on changing frames.
Never remove a failed allocation run from the evidence or repeatedly retry until it passes.

Validate through scripts/verify.ps1. On Windows, native input/output or session changes also require
scripts/native-smoke.ps1. Native test processes must remain hidden. Do not launch a visible demo
unless the user asks. Do not describe Linux or macOS native execution as verified without running it.

Commit the front framebuffer only after successful output. Preserve wide-cell pairing, sanitize
prepared text, bound parser storage, and restore terminal modes and encoding on every managed exit.
Keep mutable tree operations on the owning Ranvier graph thread and dispose owned bindings.

Do not replace Ranvier ownership, effects or bindings with a parallel reactive implementation.
Do not publish packages or create remote repositories merely to complete local verification.
