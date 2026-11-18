#r "../../src/Ranvier.Terminal.Core/bin/Release/net10.0/Ranvier.Terminal.Core.dll"
open System
open System.Threading
open System.Threading.Tasks
open Ranvier.Terminal
let r = Renderer.create 40 4 16384 { Colors=TrueColor; SynchronizedOutput=false }
let sink = { Write=(fun _ _ _ -> ()); Flush=(fun () -> ()) }
Renderer.present sink r
Frame.put (Frame.bounds r.Back) 3 2 "X" 1 Paint.empty r.Back
let entered = new ManualResetEventSlim(false)
let release = new ManualResetEventSlim(false)
let mutable writes = 0
let originalBuffer = r.Bytes
let slow = { Write=(fun _ _ _ -> Interlocked.Increment(&writes) |> ignore; entered.Set(); if not (release.Wait(5000)) then failwith "release timeout"); Flush=(fun () -> ()) }
let work = Task.Run(fun () -> Renderer.present slow r)
if not (entered.Wait(5000)) then failwith "writer entry timeout"
Thread.Sleep 120
if work.IsCompleted || writes <> 1 || r.Front.Cells[83].Glyph <> " " || not (Object.ReferenceEquals(originalBuffer,r.Bytes)) then failwith "queued, committed, or buffer replaced before acceptance"
release.Set()
work.GetAwaiter().GetResult()
if r.Front.Cells[83].Glyph <> "X" then failwith "commit missing after acceptance"
Frame.put (Frame.bounds r.Back) 3 2 "Y" 1 Paint.empty r.Back
try Renderer.present { slow with Flush=(fun () -> raise (IO.IOException "delayed flush failure")) } r with :? IO.IOException -> ()
if r.Valid || r.Front.Cells[83].Glyph <> "X" then failwith "failed flush committed"
Renderer.present sink r
if r.Front.Cells[83].Glyph <> "Y" then failwith "retry failed"
printfn "measured here: bounded synchronous slow writer PASS: held 120ms, one write outstanding, same buffer, no front commit before release; flush failure retained old front, full retry committed"
