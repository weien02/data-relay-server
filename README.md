# DedicatedServer

Authoritative dedicated server for the frame-synchronisation networking
framework. It is a headless .NET 8 console application that listens on a UDP
port, accepts client logins, and drives the server-side simulation tick.

The Unity client lives in `Unity3D Assets/` and is documented in
[its own README](Unity3D%20Assets/README.md).

## Prerequisites

The **.NET 8 SDK (x64)** is the only requirement. Confirm it is present:

```powershell
dotnet --list-sdks
```

You should see an `8.0.x` entry, for example:

```
8.0.425 [C:\Program Files\dotnet\sdk]
```

If .NET 8 is missing, install it from
<https://dotnet.microsoft.com/download/dotnet/8.0>. Choose the **SDK**, not the
runtime alone — the SDK is needed to compile, and it includes the runtime.

Newer SDKs may also be installed alongside; `global.json` pins the build to the
8.0.x line regardless. See [Why .NET 8](#why-net-8).

## Run the server

From the repository root:

```powershell
dotnet run --project "DedicatedServer\DedicatedServer" -c Release -- 5000
```

This compiles and launches in one step. `5000` is the UDP port to listen on and
may be omitted, in which case the server defaults to port 5000.

On first launch Windows Firewall may ask whether to allow the application
through. Accept it, or clients will not be able to reach the server.

### Expected output

```
process id 7456
sync rate 30 frames/second
[Information][DedicatedServer.Demo.JumpingGame.JumpingGame] begin StartServer
[Information][DedicatedServer.Demo.JumpingGame.JumpingGame] server running on port 5000
[Information][DedicatedServer.Demo.JumpingGame.JumpingGame] end StartServer
```

The process then stays in its tick loop, printing further log lines as clients
connect. The process id is printed on the first line so a profiler can attach
without looking it up, for example `dotnet-counters monitor -p 7456`.

### Overriding the sync rate

The second line of that output reports the rate the run is using, which is 30
above because nothing overrode it. An optional `--sync-rate` changes it for a
single run, which saves editing and rebuilding when sweeping rates during
measurement:

```powershell
dotnet run --project "DedicatedServer\DedicatedServer" -c Release -- 5000 --sync-rate 60
```

That run reports `sync rate 60 frames/second` instead. The port and
`--sync-rate` may appear in either order, and either may be omitted. See
[Sync rate accuracy](#sync-rate-accuracy) for the rates the tick loop can
actually deliver.

### Stop the server

Press **Ctrl+C** in the console. This triggers a graceful shutdown that
releases the listening socket rather than killing the process outright.

## Build and run as separate steps

Useful when taking measurements, so that compilation is not part of the timed
run.

```powershell
# Build once
dotnet build "DedicatedServer\DedicatedServer.sln" -c Release

# Run the produced executable as many times as needed
.\DedicatedServer\DedicatedServer\bin\Release\net8.0\DedicatedServer.exe 5000
```

Use `-c Debug` in place of `-c Release` for a debug build; the output path
changes to `bin\Debug\net8.0\` to match.

Always measure against a **Release** build. Debug builds disable optimisations
and produce misleading performance figures.

## Run from Visual Studio Code

VS Code needs the [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit)
extension, which pulls in the C# extension as a dependency.

1. Open the repository root folder in VS Code.
2. Press **F5**.
3. Choose **Dedicated server (Debug, port 5000)**.

The project builds first, then launches in the integrated terminal so that
Ctrl+C reaches the shutdown handler. Breakpoints in the tick loop and packet
handlers behave normally.

A **Dedicated server (Release, port 5000)** configuration is also provided.
**Ctrl+Shift+B** runs a debug build without launching.

To change the port, edit the `"args": ["5000"]` line in
[.vscode/launch.json](.vscode/launch.json).

## Configuration

Runtime behaviour is set in
[Framework/Configurations.cs](DedicatedServer/DedicatedServer/Framework/Configurations.cs):

| Setting | Class default | Jumping Game demo | Purpose |
| --- | --- | --- | --- |
| `SyncRatePerSecond` | 30 | 30 | State synchronisation frames sent per second |
| `DisconnectThersholdFrameCount` | 1000 | **100** | Silent frames before a client is dropped |
| `EnableAutomaticAuthorityTransfer` | true | not set | Whether entity authority migrates automatically |
| `EnableDirtyOnlySync` | true | true | Send only changed fields rather than full state |
| `EnableParallelWriteTickLogging` | false | false on the server, **true** in the Unity client | Verbose logging for the parallel write tick |
| `ParallelWriteTickWorkerThreadLimit` | -1 | not set | Worker thread cap; -1 means unbounded |

The **Class default** column is what `Configurations` itself constructs. The
**Jumping Game demo** column is what `JumpingGame.ConfigureFramework` then
overrides, and is therefore what actually runs. The two differ, so read the
demo column when interpreting results.

Two consequences worth noting. The disconnect threshold counts sync frames, not
seconds, so the demo's 100 frames is about 3.3 seconds at 30 frames per second
rather than the 33 seconds the class default would give. And
`EnableParallelWriteTickLogging` is deliberately on in the Unity client, since
the parallelisation measurements read that log from the Unity console.

These values must match on the server and the client. They are intended to be
fixed at startup and left alone while the process runs; `StartFrameSync` reads
`SyncRatePerSecond` once, so changing it after the server starts has no effect.

Garbage collection and JIT tiering are pinned explicitly in
[DedicatedServer.csproj](DedicatedServer/DedicatedServer/DedicatedServer.csproj)
so that resource-usage measurements stay reproducible across machines and runs.
Changing them invalidates any previously recorded baseline.

### Sync rate accuracy

The tick loop measures real elapsed time and paces synchronisation frames
through an accumulator, so the send rate holds steady no matter how fast the
loop itself runs. Measured over 10 seconds with no clients connected, a
configured 30 frames per second produced **29.90**.

That shortfall is expected, and it is not drift. `Thread.Sleep` wakes on the
operating system's timer granularity, about 15.6 ms on Windows by default, so a
frame can only be sent on a whole loop iteration and individual gaps cluster
around whole multiples of the iteration time rather than landing on 33.3 ms
exactly. The accumulator carries the remainder forward, so the long-run average
stays correct even though no single gap is.

The loop rate itself is not stable. Iteration counts between roughly 65 and 105
per second were observed on one machine across runs, because other processes can
raise or lower the system timer granularity. This no longer changes the send
rate, which is the point of the accumulator, but it does change the jitter, and
it caps how high a rate the loop can actually deliver.

If the loop cannot keep up with the configured rate, ticks start covering more
than one sync interval and several frames are sent in a single tick. This is
reported once every five seconds rather than per tick:

```
[Warning][...ServerSideFrameSynchronizationController] Tick took longer than the
sync interval on 12 of the last 489 ticks, so those ticks sync more than one
frame. The loop is not running fast enough for SyncRatePerSecond = 60
(sync interval = 0.016666668s).
```

Seeing this means the requested rate is near or past what the loop can deliver
on that machine, and the resulting frame timing will be uneven. At 30 frames per
second it stays silent.

The jitter matters when measuring latency: it shows up in the results and is a
property of the measurement setup, not of the framework. Removing it would mean
either raising the timer resolution, which is a Windows-only call, or
spin-waiting, which burns CPU and would distort the resource-usage figures.
Neither is done here, deliberately.

Figures recorded before synchronisation frames were paced this way are not
comparable, as the send rate then followed the loop speed rather than
`SyncRatePerSecond`.

## Why .NET 8

`global.json` pins the build to the 8.0.x SDK. This is deliberate:

- The project targets `net8.0`, so the toolchain and the target agree.
- Performance figures depend on the runtime's garbage collector and JIT.
  Building against a different major version shifts those numbers and breaks
  comparability with previously recorded results.

Without the pin, the .NET CLI selects the highest installed SDK, which may be a
newer major version.

## Project layout

```
DedicatedServer/DedicatedServer/
├── Program.cs              Entry point, argument parsing, tick loop
├── Framework/
│   ├── Configurations.cs   Shared server/client settings
│   ├── ECS/                Entity registry, data stores, networked values
│   ├── Networking/         Packet format, packet types, UDP transport
│   └── Server/             Game server, rooms, sessions, frame sync
├── GameDemo/               Jumping Game demo simulation
└── Unity/ECS/Data/         Data types shared with the Unity client
```

## Troubleshooting

**`error MSB4242: SDK Resolver Failure` mentioning `'0x00' is an invalid start of a value`**

An installed SDK has corrupted workload manifest files. Confirm `global.json`
is present at the repository root and that `dotnet --version` reports an
`8.0.x` version. If a broken SDK is still being selected, repair it through
Settings → Apps → the relevant .NET SDK entry → Modify → Repair.

**`You must install or update .NET to run this application`**

The .NET 8 runtime is missing. Installing the .NET 8 SDK as described under
[Prerequisites](#prerequisites) resolves this.

**`SocketException (10048): Only one usage of each socket address (protocol/network address/port) is normally permitted`**

Another process already holds the port. Either stop the earlier server
instance, or start this one on a different port by passing a different number.

**Clients cannot connect across a network**

Confirm the server host allows inbound **UDP** on the chosen port, and that
clients are pointing at the server machine's IP address rather than
`127.0.0.1`.
