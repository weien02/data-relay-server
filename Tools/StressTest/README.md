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

### Reconnection episodes

`--kill N` takes N clients off the network at random moments during the load and brings them back,
timing how long each one needs before its view of everyone else is correct again.

| option | meaning |
| --- | --- |
| `--kill N` | kill N clients, once each, at random moments. Off by default. |
| `--kill-mode MODE` | `rejoin` closes the socket and logs in again, like a restarted process. `blackout` keeps the socket, goes silent and discards what arrives, like losing wifi. Default `rejoin`. |
| `--kill-for-min S` / `--kill-for-max S` | range the away time is drawn from (default 1 to 8 seconds). |
| `--kill-not-before S` | earliest kill, in seconds into the load (default 2). |
| `--kill-settle S` | slack left between the last return and the end of load, so a recovery cannot still be running when the run ends (default 3). |
| `--recover-timeout S` | give up on an episode after this long (default 10). |
| `--kill-seed N` | seed for the kill times and away times. One is always chosen and printed, so any run replays exactly. |
| `--server-disconnect-seconds S` | the server's `DisconnectThresholdSeconds`, the silence threshold each away time is compared against. Default 3, which is what the demo server sets. |

**The threshold matters more than anything else here.** The server drops a session after
`DisconnectThresholdSeconds` without a heartbeat — 3 seconds on the demo server. Below that the
server never learns the client went away at all, and the return is pure sync-loss repair. Above it
the session is marked disconnected, the server broadcasts `AskForBackupEntityAuthority`, and only a
fresh login can bring the client back. Episodes are tagged and reported separately on that line.

This used to be configured as a frame count, which meant the real timeout moved with the server's
`--sync-rate`: the same 100 frames was 3.3s at 30 Hz but 1.7s at 60 Hz. It is a duration now, so
the sync rate no longer comes into it and this flag is the only one the harness needs.

A `blackout` that outlasts the threshold **can never recover**, and the harness says so rather than
pretending it timed out. `Session.IsConnected` is only set back to true from `Session.Connect()`,
which is reached only from the login handler, so heartbeats alone never revive a session. The
client stops being broadcast to, never sees another frame number, and a frame-number gap is the
only thing that triggers a repair — it goes permanently deaf and mute while the server believes
nothing is wrong. Use `rejoin` for anything longer than the threshold.

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
frame because the server drops a session that stays silent for `DisconnectThresholdSeconds`.

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

**reconnection** — only present with `--kill`. `back in` is the headline: from the moment the
client came back to the moment its view of every other player was correct again, with the
arbitrary choice of how long it stayed away divided out. `outage` is the same thing measured from
the kill, which is what a player would actually feel. `snapshot round trip` is the narrow claim the
project's report makes — request sent to response received — and it is normally far smaller than
`back in`; the gap between the two is the interesting number, because it is the part the "recovery
is one round trip" claim leaves out.

Distributions are reported, never a bare mean, and `recovered N of M` is checked first. A recovery
that usually takes 30 ms and occasionally never finishes is a different system from one that always
takes 200 ms, and an average cannot tell them apart.

`still stale: player X` on a failed episode names the owners whose state never came back. That is
usually the symptom of the snapshot filter described below rather than packet loss.

### The recovery snapshot is not actually a full snapshot

`SyncEntitiesToSingleClient` is documented as sending the client everything it needs, but
`SyncEntitiesDataInInterval`
([ServerSideEntityManager.cs:142](../../DedicatedServer/DedicatedServer/Framework/Server/ServerSideEntityManager.cs#L142))
filters on `LastModifiedFrameNumber` being inside the client's missed window *before* `syncFullData`
is ever consulted. `syncFullData` only decides whether full or dirty fields are written for an
entity that already passed the filter. So **an entity that stopped changing before the client's
loss began is not resent at all**, and if the client missed its last update it stays wrong forever.

`rejoin` hides this, because it resets the client's frame counter to zero, which makes the filter
pass everything. `blackout` keeps its frame number and so is exposed to it. To see it: run
`--kill-mode blackout` with a mix of away times, so that one client wedges past the threshold and
stops publishing, and watch a second, under-threshold client fail to re-learn that first client's
avatar.

### Watch the server log too

The server reports when its own tick loop cannot keep up:

```
Tick took longer than the sync interval on 99 of the last 114 ticks ...
```

That warning is the clearest signal of the saturation point, and it appears well before the cross
check starts failing. Run with `--sync-rate` on the server and compare against the harness's
`observed sync rate` to see when the server stops holding its configured rate.
