# DedicatedServer

Authoritative dedicated server for the frame-synchronisation networking
framework. It is a headless .NET 8 console application that listens on a UDP
port, accepts client logins, and drives the server-side simulation tick.


## Prerequisites

The **.NET 8 SDK** is the only requirement. It runs on Windows, macOS and Linux.
Confirm it is present:

```sh
dotnet --list-sdks
```

You should see an `8.0.x` entry, for example:

```
8.0.425 [C:\Program Files\dotnet\sdk]          # Windows
8.0.425 [/usr/local/share/dotnet/sdk]          # macOS
8.0.425 [/usr/lib/dotnet/sdk]                  # Linux (path varies by distribution)
```

If .NET 8 is missing, install the **SDK**, not the runtime alone. The SDK is
needed to compile, and it includes the runtime. Pick the build that matches your
CPU: **x64** for Intel and AMD, **Arm64** for Apple Silicon and ARM machines.

| Platform | How to install |
| --- | --- |
| Windows | Installer from <https://dotnet.microsoft.com/download/dotnet/8.0> |
| macOS | `.pkg` installer from <https://dotnet.microsoft.com/download/dotnet/8.0>. Then open a new terminal so `dotnet` is on the `PATH` |
| Linux | Your distribution's package (for example `sudo apt install dotnet-sdk-8.0` on Ubuntu), see <https://learn.microsoft.com/dotnet/core/install/linux> |
| Any, without admin rights | `./dotnet-install.sh --channel 8.0` (or `dotnet-install.ps1` on Windows) from <https://dot.net/v1/dotnet-install.sh>, which installs to `~/.dotnet` |

Newer SDKs may also be installed alongside; `global.json` pins the build to the
8.0.x line regardless. See [Why .NET 8](#why-net-8).

## Run the server

From the repository root, in any shell (PowerShell, cmd, bash, zsh):

```sh
dotnet run --project DedicatedServer/DedicatedServer -c Release -- 5000
```

This compiles and launches in one step. `5000` is the UDP port to listen on and
may be omitted, in which case the server defaults to port 5000. Forward slashes
work on every platform, including Windows.

The first time the server listens on the network, the operating system's
firewall may get involved:

- **Windows**: Windows Firewall asks whether to allow the application through. Accept it.
- **macOS**: if the macOS firewall is on, it asks whether to accept incoming network connections. Allow it.
- **Linux**: there is usually no prompt. If a firewall is active, open the port yourself, for example
  `sudo ufw allow 5000/udp` or `sudo firewall-cmd --add-port=5000/udp`.

Without this, clients on other machines cannot reach the server. Connections
from the same machine (`127.0.0.1`) are not affected.

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

```sh
dotnet run --project DedicatedServer/DedicatedServer -c Release -- 5000 --sync-rate 60
```

That run reports `sync rate 60 frames/second` instead. The port and
`--sync-rate` may appear in either order, and either may be omitted. See
[Sync rate accuracy](#sync-rate-accuracy) for the rates the tick loop can
actually deliver.

### Stop the server

Press **Ctrl+C** in the terminal, on any platform. This triggers a graceful
shutdown that releases the listening socket rather than killing the process
outright.

On macOS and Linux, a server started in the background from a script (with `&`)
may ignore Ctrl+C and `kill -INT`, because non-interactive shells start
background jobs with that signal ignored. Stop such a server with
`kill <pid>` instead, using the process id it prints on startup.

## Build and run as separate steps

Useful when taking measurements, so that compilation is not part of the timed
run.

```sh
# Build once
dotnet build DedicatedServer/DedicatedServer.sln -c Release

# Run the build as many times as needed (same command on every platform)
dotnet DedicatedServer/DedicatedServer/bin/Release/net8.0/DedicatedServer.dll 5000
```

The build also produces a native launcher you can run directly:

| Platform | Command |
| --- | --- |
| Windows | `.\DedicatedServer\DedicatedServer\bin\Release\net8.0\DedicatedServer.exe 5000` |
| macOS / Linux | `./DedicatedServer/DedicatedServer/bin/Release/net8.0/DedicatedServer 5000` |

Use `-c Debug` in place of `-c Release` for a debug build; the output path
changes to `bin/Debug/net8.0/` to match.

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
**Ctrl+Shift+B** (**Cmd+Shift+B** on macOS) runs a debug build without
launching. The launch configurations start the `.dll` through `dotnet`, so they
work unchanged on every platform.

To change the port, edit the `"args": ["5000"]` line in
[.vscode/launch.json](.vscode/launch.json).

## Configuration

Runtime behaviour is set in
[Framework/Configurations.cs](DedicatedServer/DedicatedServer/Framework/Configurations.cs):

| Setting | Class default | Jumping Game demo | Purpose |
| --- | --- | --- | --- |
| `SyncRatePerSecond` | 30 | 30 | State synchronisation frames sent per second |
| `DisconnectThresholdSeconds` | 30 | **3** | Silence before a client is dropped. Converted to a frame count against `SyncRatePerSecond` when it is checked, so it means the same duration at every sync rate |
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
operating system's timer granularity, so a frame can only be sent on a whole
loop iteration and individual gaps cluster around whole multiples of the
iteration time rather than landing on 33.3 ms exactly. The accumulator carries
the remainder forward, so the long-run average stays correct even though no
single gap is.

How coarse that granularity is depends on the platform:

| Platform | Default sleep granularity | What the server does about it |
| --- | --- | --- |
| Windows | About 15.6 ms | Requests 1 ms resolution for its own process (`timeBeginPeriod`) at startup and releases it on exit |
| macOS | About 1 ms | Nothing extra needed |
| Linux | About 1 ms or finer | Nothing extra needed |

On every platform the loop also stops sleeping 1.5 ms before a sync frame is due
and spin-waits for the rest, since even a 1 ms sleep can overshoot. Between
frames it sleeps in 4 ms steps so incoming packets are still read promptly.

The 29.90 figure above and the loop-rate observations below were recorded on
Windows. On macOS the stress test measured **30.0** frames per second at the
default rate with 40 clients connected.

The loop rate itself is not stable on Windows. Iteration counts between roughly
65 and 110 per second were observed on one machine across runs, because other
processes can raise or lower the system timer granularity. The accumulator absorbs this: the
average send rate stays correct regardless. Measured over 10 seconds with no
clients connected, every configured rate from 5 to 500 came out within one frame
of its target, the missing frame being a boundary effect of the measurement
rather than drift.

What the loop rate does govern is smoothness, not average rate. A sync frame can
only be sent from a loop iteration, so once the configured rate exceeds the loop
rate the accumulator catches up by sending several frames from a single
iteration. At 500 frames per second against a loop running 80 times per second
the average was still 499.25, but those frames left in bursts of roughly six
rather than evenly spaced. Rates at or below about 50 per second stayed smooth on
this machine; treat anything higher as delivering the right average with uneven
spacing.

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
property of the measurement setup, not of the framework. The short spin-wait
before each frame reduces it at the cost of a little CPU, which shows up in
resource-usage figures. Expect the jitter to differ between Windows, macOS and
Linux, and compare latency figures only between runs on the same platform.

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
`8.0.x` version. If a broken SDK is still being selected, repair or reinstall it:

- **Windows**: Settings → Apps → the relevant .NET SDK entry → Modify → Repair.
- **macOS**: run the .NET 8 SDK `.pkg` installer again.
- **Linux**: reinstall the package, for example `sudo apt install --reinstall dotnet-sdk-8.0`.

**`dotnet: command not found`** (macOS / Linux)

The SDK is installed but the terminal hasn't picked up the `PATH` change. Open a
new terminal. If it still fails, the macOS installer puts `dotnet` in
`/usr/local/share/dotnet`, and the install script puts it in `~/.dotnet`; add
that directory to your `PATH`.

**`You must install or update .NET to run this application`**

The .NET 8 runtime is missing. Installing the .NET 8 SDK as described under
[Prerequisites](#prerequisites) resolves this.

**Port already in use**

The error text depends on the platform:

- Windows: `SocketException (10048): Only one usage of each socket address ... is normally permitted`
- macOS: `SocketException (48): Address already in use`
- Linux: `SocketException (98): Address already in use`

Another process already holds the port, often an earlier server that is still
running. Find it with `netstat -ano | findstr :5000` on Windows, or
`lsof -nP -iUDP:5000` on macOS and Linux. Either stop it, or start this server
on a different port by passing a different number.

**Clients cannot connect across a network**

Confirm the server host's firewall allows inbound **UDP** on the chosen port
(see [Run the server](#run-the-server) for each platform), and that clients are
pointing at the server machine's IP address rather than `127.0.0.1`.
