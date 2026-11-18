---
title: Subprocess output
description: Display a bounded stream, browse its history and connect a child terminal without blocking input.
---

## Start with a read-only stream

An `OutputBuffer` holds the mutable terminal screen and bounded scrollback. An `OutputView` displays a detached snapshot. Build both on the graph thread during setup. This helper returns a node to mount and a function to call from a graph-owned update.

```fsharp
let logPane width height historyLines =
    let buffer = OutputBuffer.create width height historyLines
    let offset = createSignal 0
    let snapshot = createSignal (OutputBuffer.snapshot 0 buffer)
    createEffect (fun () ->
        snapshot.Value <- OutputBuffer.snapshot offset.Value buffer)

    let append line =
        OutputBuffer.feedText (line + "\r\n") buffer
        snapshot.Value <- OutputBuffer.snapshot offset.Peek buffer

    OutputView.create [ Css.border Rounded ] snapshot offset, append
```

At offset zero the pane follows the tail. Page Up or the mouse wheel browses history. The offset effect publishes the requested slice. While the reader is browsing, the widget adjusts the offset as lines arrive. Let the widget make that adjustment once. To adapt to resize, read `OutputView.tryViewportSize node` after layout and resize the buffer to the content dimensions.

Use `feedText` for literal text. Use `feedUtf8` for chunks from a terminal stream, where a UTF-8 character or escape sequence can span reads. Call `OutputBuffer.finish` after the final chunk to handle trailing partial input, then publish the last snapshot.

## Choose the child's transport

`Subprocess.command` takes an executable and an argument list. The default redirected transport exposes stdout and stderr as separate byte streams. For a child that needs terminal input and resize, probe for a supported pseudoterminal before starting it.

```fsharp
let startChild executable arguments width height cancellation =
    let options =
        { Subprocess.command executable arguments with
            Mode =
                Subprocess.tryPseudoTerminal width height
                |> Option.defaultValue Redirected }
    Subprocess.start options cancellation
```

Intel macOS uses the native launcher included in the host package. On Apple Silicon the capability probe returns `None` for the subprocess PTY; redirected I/O and the interactive terminal host remain available.

The fallback is appropriate when the child also supports pipes. An application requiring a controlling terminal can handle `None` before launch. Keep independent decoders for redirected stdout and stderr. A pseudoterminal produces one combined stream. Feed its bytes to the buffer and return any `OutputBuffer.drainResponses` bytes to the child so terminal queries receive an answer.

## Connect readiness to the host

Call `SubprocessOutput.bind maxBytes readSession receive ended` during owned setup. Read the active session signal in `readSession`. Call the returned pump once in each host update. A ready session wakes the graph and the pump delivers a bounded batch, leaving the host an opportunity to handle input and paint between batches.

Process the final bytes before observing `session.Exit`. An undrained full queue applies backpressure and can delay normal exit. Replacing the session signal detaches the previous reader. The application owns the child and must call `session.Close()` and await the returned task during shutdown.

## Forward input to an interactive child

Mount `OutputView.interactive` in the page. In its input callback, encode the event with `TerminalKeys.encode snapshot.Peek.BracketedPaste event`. Queue nonempty results for `session.Write`, keeping each byte array intact until its write completes. Bound the queue and observe write failures.

In the Showcase **Subprocess** page, choose **Start**, focus the pane and send a line. Tab and plain navigation keys go to the child. Shift+Page Up browses outer scrollback and Ctrl+Tab returns focus to the outer application. See `examples/Utilities/Model.fs` in the checkout for a working send queue, resize handling, reader pump and shutdown sequence.
