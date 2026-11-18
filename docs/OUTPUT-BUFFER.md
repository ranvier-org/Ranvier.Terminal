# Streaming output buffer

`OutputBuffer` is single-owner mutable output state, separate from a subprocess transport and
separate from the outer terminal renderer. Background readers should enqueue bounded data for
the owning graph thread; that thread feeds the buffer and publishes snapshots. Feeding the buffer
does not write to the console, execute a command, access a clipboard, open a URL, or change a host
terminal mode.

This is a useful VT subset, not a complete terminal emulator. Commands and limits below are
**from documentation: the implementation in `src/Ranvier.Terminal.Core/OutputBuffer.fs`**. The
executable fixtures are in `tests/Ranvier.Terminal.Tests/OutputBufferChecks.fs`.

## API

```fsharp
let output = OutputBuffer.create width height historyLines
OutputBuffer.feedText "Starting…\n" output
OutputBuffer.feedUtf8 bytes offset count output
let visible = OutputBuffer.snapshot scrollOffset output
let responses = OutputBuffer.drainResponses output
```

`create` requires positive screen dimensions, nonnegative history capacity and a combined screen
and history cell budget that fits the implementation's array addressing. Invalid dimensions or
byte ranges fail explicitly. History capacity is fixed for the buffer's lifetime.

`feedText` consumes literal text. Newline starts a new row at the left edge. Carriage return moves
to the left edge without erasing the row, allowing progress replacement; backspace moves left,
and tab moves to the next fixed tab stop. Escape and other non-layout control characters become
replacement glyphs. A string containing VT sequences is therefore displayed safely rather than
interpreted through this API. Empty input is a no-op.

`feedUtf8` consumes subprocess bytes and interprets the supported VT subset below. UTF8 decoder
state, UTF16 surrogate pairs, control sequences and grapheme continuation survive arbitrary chunk
boundaries. A VT line feed preserves the horizontal cursor unless newline mode is enabled;
ordinary terminal output commonly uses CRLF. Malformed UTF8 uses the runtime decoder's replacement
behavior. This API is suitable for both redirected output and actual PTY output; it does not make
a redirected pipe a PTY.

Do not alternate literal and VT feeds to assemble a control sequence. A nonempty literal feed
flushes incomplete UTF8 and discards any pending VT control before consuming literal text.
It cannot complete a pending escape sequence. Consecutive literal feeds preserve split surrogate
pairs and grapheme continuation.

`finish` marks a drained stream boundary: it flushes incomplete UTF8/surrogates as replacement
characters, discards incomplete control strings and ends the current grapheme continuation.
It is safe to call again. A snapshot alone does not flush incomplete input, so later chunks can
still complete it. Neither feed nor snapshot has the prepared renderer's zero-allocation claim.

## Detached snapshots and history

An `OutputSnapshot` contains a detached `Frame`, cursor coordinates and visibility, title,
revision, retained history count, total scrolled-line count, clamped history offset, alternate
screen flag and bracketed-paste flag. Its cell array does not alias the mutable buffer. Changing
the buffer or resizing it cannot change an earlier snapshot. A consumer can edit its own snapshot
array without changing the buffer; consumers must coordinate sharing between themselves.

Offsets and coordinates are zero-based. An offset of zero follows the live screen; positive
offsets browse retained primary-screen history. The offset is clamped to the current history
count. The reported cursor is hidden while browsing history. Evicted rows cannot be requested.

`TotalScrolledLines` increments when a complete primary-screen scroll moves a row into logical
history, including rows immediately evicted by the configured capacity. It also advances when
history capacity is zero. A widget can add this delta to a nonzero manual offset to keep the same
history content anchored while output arrives, then clamp to retained history. This cannot recover
already evicted content. `clear` resets the counter and advances `Revision`, establishing a new
history-navigation epoch. VT erase-history discards retained rows without resetting the counter.

Only scrolling the complete primary screen adds history. Restricted scrolling regions, inserted
lines and alternate-screen output do not capture unrelated sibling rows. The history ring reuses
evicted row storage; its retained row count never exceeds `historyLines`.

Alternate-screen entry uses a separate screen/cursor/style state. The supported clear-on-entry
variants reset that alternate screen; the preserve variant retains it. Returning to the primary
screen restores its prior contents and cursor state. Alternate snapshots report no browsable
history and a zero scroll offset, while the primary history remains retained for later restoration.
The total primary scrolled-line counter remains available.

`resize width height` crops or pads existing rows without text reflow. Both screens and every
retained history row adopt the new width; wide glyphs clipped at an edge are repaired into blanks.
Cursor positions are clamped, scroll margins reset to the screen, and pending automatic wrapping
is canceled. The top-left existing content is preserved where it fits. This is not an xterm-style
scrollback reflow implementation.

`clear` clears both screens and history, returns to the primary screen, resets cursor/paint/modes,
clears title and pending query replies, and resets decoder/control state. `Revision` advances for
nonempty feeds, finishing, resizing and clearing; it is a change token, not a frame count or clock.

## Unicode and storage limits

The width policy is the same practical policy used by `TextRun.defaultWidth`. It does not promise
agreement with every emulator/font Unicode version. Combining marks, emoji ZWJ sequences and
regional-indicator pairs can extend the most recently painted grapheme across feeds. A wide glyph
uses a leading cell and continuation cell; overwriting, insertion, deletion, scrolling and resize
repair pair boundaries. A wide glyph that cannot fit even an empty screen row becomes a replacement
glyph so the cursor can make progress.

**From documentation: `OutputBuffer.fs` constants:** a retained grapheme is limited to 256 UTF16
code units. Further extension becomes a replacement grapheme rather than accumulating an unlimited
combining string. Parser control storage is limited to 512 UTF16 code units. An overlong sequence
is consumed through its terminator without applying its command. Titles are limited to 256 UTF16
code units and stripped of controls. The UTF8 decoder uses a reusable 256-character staging array.
Pending terminal query replies are limited to 4,096 bytes; replies that do not fit are dropped
whole. CSI input is limited to 32 parameters, each capped at 32,767; unsupported or malformed
parameter syntax is ignored.

The buffer owns bounded primary/alternate cell arrays and a fixed-capacity history ring. Resize
also resizes history row storage; old wide rows are not retained behind a narrow viewport.
Detached snapshots allocate their own frames. Applications retaining every snapshot indefinitely
can still grow their own memory use; the buffer cannot bound consumer-owned objects.

## Implemented VT subset

The names and parameter conventions follow the familiar VT/ANSI families described by
[Microsoft's console VT reference](https://learn.microsoft.com/en-us/windows/console/console-virtual-terminal-sequences).
The table describes this implementation, not a claim that every listed operation is identical
across external emulators.

| Family | Supported forms |
|---|---|
| Basic movement | CR, LF, VT, FF, BS, fixed tabs; ESC D/E/M index, next line and reverse index |
| Cursor positioning | CSI A/B/C/D/E/F/G/H/f/d/a/e and horizontal absolute backtick; CSI I/Z fixed tab movement |
| Saved cursor | ESC 7/8 and CSI s/u; cursor, paint, origin, autowrap and deferred-wrap state are saved |
| Erase | CSI J display modes 0/1/2, mode 3 history erase; CSI K line modes 0/1/2; CSI X erase characters |
| Modification | CSI @ insert characters, P delete characters, L/M insert/delete lines within margins |
| Scrolling | CSI S/T within the current scrolling region; CSI r sets vertical margins; whole rows retain wide pairing |
| Repeat | CSI b repeats the preceding graphic grapheme, with current paint |
| Rendition | SGR reset; bold, dim, italic, underline, reverse, strike and their resets; basic/bright indexed colors; semicolon-form indexed and RGB extended colors; default foreground/background |
| Standard modes | Insert mode and newline mode |
| Private modes | Origin, autowrap, cursor visibility, bracketed paste; alternate screen modes 47/1047/1049; cursor save/restore mode 1048 |
| Title | OSC 0 and 2, terminated by BEL or ST; title remains metadata and is not forwarded to the outer terminal |
| Reset | ESC c resets the buffer/screen state without losing subsequent bytes already being decoded |
| Queries | Normal status/cursor reports, private cursor report, primary/secondary device attributes and character-window size query |

The CSI/OSC/DCS string introducers also recognize their decoded C1 equivalents. Unsupported DCS,
APC and PM strings are discarded through ST. Unsupported OSC commands, including clipboard and
hyperlink metadata, are consumed without side effects. BEL has no audible effect. Cancellation
controls terminate incomplete sequences. Unsupported charset selection, mouse-reporting modes,
cursor shape/blinking settings, graphics/image protocols, hyperlinks, palette mutation, horizontal
margins, colon-form SGR extensions and emulator-specific queries are not implemented. Arbitrary
full-screen applications can rely on additional behavior and are not guaranteed compatible.

## Query replies and transport

Query replies are stored in the bounded response buffer. `drainResponses` returns a detached byte
array and empties that queue. A PTY integration may send it to the subprocess input transport.
Never write it to the outer terminal. Device-attribute replies are minimal compatibility identifiers,
not a declaration of complete emulator compatibility. Unknown requests receive no reply.

**Measured here:** source-loaded fixtures first failed against the no-op API stub for progress
replacement, then passed after implementation. Additional regression coverage caught deferred
wrapping being canceled by SGR and alternate-screen commands before the correction. Current
fixtures compare every split of mixed Unicode/VT input, literal surrogate/grapheme splits,
independent ASCII streaming behavior, bounded history and replies, detached snapshots, long and
malformed controls, style changes, alternate restoration, scoped scrolling and randomized wide-cell
invariants. These checks are not evidence of universal external terminal compatibility.
