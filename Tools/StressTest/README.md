# StressTest

A load and correctness harness for the dedicated server. It runs many simulated users against a
running server, has each one publish its own state and observe everyone else's, and then checks
every observer's view against what the owners actually published.

It speaks the wire protocol directly and does **not** reference the framework's own serialiser.
That is deliberate: if the harness reused `Packet` and `EntityDataStoreBase`, a serialisation bug
would cancel itself out on both sides and the test would pass anyway.

## Running it

```
# terminal 1
cd DedicatedServer/DedicatedServer
dotnet run -c Release -- 5000

# terminal 2
cd Tools/StressTest
dotnet run -c Release -- --clients 40 --duration 30
```

`--help` lists every option. The ones that matter most:

| option | meaning |
| --- | --- |
| `--clients N` | number of simulated users. The server's `PlayerID` is a byte, so 255 is the ceiling. |
| `--duration S` | seconds of load once everyone has spawned. |
| `--quiesce S` | seconds where clients hold still but stay connected, so the last updates can land before the cross check. |
| `--publish-every N` | publish on every Nth sync frame instead of every one, to vary upstream load independently of client count. |
| `--join-late N` | hold N clients back and join them mid-run, to test what a client learns about a world already in progress. |
| `--json PATH` | write the whole report as JSON as well, for charting across runs. |

Exit code is 0 on pass, 1 on a failed cross check, 2 on a setup problem.

### Restart the server between runs

The server never forgets a session. `SessionManager.GetSessionID` matches a login by client id, so
reusing client ids against a server that is still up resumes the previous run's sessions rather
than creating new ones. The harness picks a fresh `--client-id-base` each run to avoid this, but
player ids are still allocated from a byte and never reclaimed, so a long-lived server eventually
runs out. Restart it between runs and the numbers stay comparable.

## What each client does

Each simulated user owns one avatar (registry id 0, the JumpingGame player) with
`Full_LocalPlayer` authority, and mirrors the real Unity client's cadence: it publishes state in
response to `SyncFrameBegin` rather than on a clock of its own, and it heartbeats on every sync
frame because the server drops a session that misses `DisconnectThersholdFrameCount` of them.

The published state is a pure function of `(playerID, sequenceNumber)`, built only from values that
are exactly representable as 32 bit floats. That is what allows the verifier to compare with `==`
rather than a tolerance, so a mismatch is evidence of a real fault and never of float drift. Two
things are encoded into the state itself:

- the sequence number, in `AvatarJumpStateData.jumpPower`, so any observed sample says which of the
  owner's updates it came from;
- the owner's player id, in the position, so a sample attributed to the wrong entity can be spotted
  from its contents alone.

`TransformData` and `AvatarJumpStateData` always change together, so they are always dirty together
and always travel in the same packet. A view holding a transform and a jump power from different
updates is therefore a real fault, not an artefact of dirty-only sync.

`AvatarRespawnData` is written once and never again. It is the probe for values a client can never
catch up on, because dirty-only sync broadcasts a value only in the frame it changes.

## Reading the report

**staleness while under load** — how far behind each sample was when it arrived, in updates, not
milliseconds. One update is one sync frame, so at 30 frames/s a lag of 2 is about 66 ms. A mean
under 1 is the floor: the owner's counter is read without synchronising against its own send, so
roughly half of all samples look one behind even when nothing is wrong.

**faults seen live** — checked as samples arrive. `impossible states` means a value combination the
owner never published. `misattributed states` means another player's state arrived under this
entity. `out of order updates` means an older update was applied after a newer one. All three
should be zero; any non-zero value is a genuine bug worth chasing.

**cross check after quiesce** — the main result. The load has stopped and the last updates have had
seconds to arrive, so there is no legitimate reason for a view to still be behind. `stale forever`
counts observers permanently stuck on an old update. That is the failure mode the transport makes
possible: with dirty-only sync the server resends nothing, and the only repair path in the
framework is triggered by a gap in *sync frame numbers*, never by a lost *data* packet. So a
dropped `SyncEntityData` datagram is silently permanent while `SyncFrameBegin` keeps arriving in
sequence.

**snapshots on join** — expect one per client. A joining client's frame counter starts at zero, so
its first `SyncFrameBegin` from a running server reads as a gap, which triggers a sync-lost request
and pulls a full snapshot. This is load-bearing: without it a joining client would never learn that
the players already present exist, because entity creation is broadcast only in the frame the
entity is created. **mid-run repairs** should be zero; each one is a full resend that also heals any
earlier loss for that client, so a non-zero count means the divergence figures are a lower bound.

### Watch the server log too

The server reports when its own tick loop cannot keep up:

```
Tick took longer than the sync interval on 99 of the last 114 ticks ...
```

That warning is the clearest signal of the saturation point, and it appears well before the cross
check starts failing. Run with `--sync-rate` on the server and compare against the harness's
`observed sync rate` to see when the server stops holding its configured rate.
