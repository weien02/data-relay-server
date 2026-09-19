# Unity Client

Unity client for the frame-synchronisation networking framework, containing the
Jumping Game demo. It connects over UDP to the dedicated server documented in
the [repository README](../README.md).

Only the `Assets/` folder is stored here. A Unity project shell must be created
around it before the project can be opened — see [First-time setup](#first-time-setup).

## Required Unity version

**Unity 2021.3.45.** Either revision, `f1` or `f2`, works.

This is not a loose recommendation. The prebuilt AssetBundles in
`Assets/StreamingAssets/` carry `2021.3.45f1` in their headers, and the client
loads the player character from one of them at runtime. A different editor
version may refuse to load those bundles, in which case they have to be rebuilt
before the demo will run.

## First-time setup

### 1. Install Unity Hub

Download and install Unity Hub from <https://unity.com/download>.

On first launch it asks you to sign in with a Unity account and activate a
licence. Create an account if you do not have one and choose the **Unity
Personal** licence, which is free and sufficient for this project.

### 2. Install the editor

Unity 2021.3.45 is an older LTS release, so it is not offered in the default
list.

1. In Unity Hub, go to **Installs** → **Install Editor**.
2. Select the **Archive** tab and follow the *download archive* link.
3. On the page that opens, find **Unity 2021.3.45** and press its **Unity Hub**
   button. The browser hands the install back to Hub.
4. When Hub asks which modules to add, **untick everything**, including the
   *Microsoft Visual Studio Community 2019* entry that is ticked by default.
   None of the platform modules are needed: on a Windows host the editor
   already includes Windows standalone build support, which is all the
   standalone client requires. Any existing C# editor can be attached later
   through **Edit → Preferences → External Tools**.

The install is several gigabytes and takes a while.

### 3. Create the project shell

1. In Unity Hub, choose **New project** → **3D (Built-In Render Pipeline)**,
   using the 2021.3.45 editor.
2. Name it, for example, `JumpingGameClient`. Prefer a location **outside
   OneDrive** — OneDrive's sync and Unity's `Library/` folder interfere with
   each other and cause slow, occasionally corrupted imports.
3. Let it finish creating, then **close the editor**.

### 4. Copy the assets in

Copy everything inside this folder's `Assets/` directory into the new project's
`Assets/` directory, merging with what is already there:

```powershell
Copy-Item -Recurse -Force `
  "<path to this repo>\Unity3D Assets\Assets\*" `
  "<path to new project>\Assets\"
```

The `.meta` files must be copied along with the assets. They carry the GUIDs
that the scene and prefab use to reference each other; without them every
reference in the scene breaks.

### 5. Open the project

Reopen the project from Unity Hub. The first import takes several minutes.

If Unity offers to **import TMP Essentials**, decline it. `Assets/TextMesh Pro/`
is already present in this repository and re-importing duplicates it. Should
TextMeshPro instead report missing scripts, open **Window → Package Manager**
and install the **TextMeshPro** package.

Then open the scene `Assets/GameDemo/JumpingGame/JumpingGame.unity`.

## Running the demo

The scene's main menu has three fields — Client ID, Server IP Address, Server
Port — and two buttons. Client IDs identify players and **must be unique**;
giving two players the same ID will not work.

### Option A — self-hosted, entirely inside Unity

Press **Play**, enter a Client ID such as `1`, leave the IP at `127.0.0.1` and
the port at `5000`, then press **Start Server**.

Unity starts a server inside its own process and immediately connects a local
client to it. The dedicated server is not involved. This is the quickest way to
check that the project runs.

### Option B — against the dedicated server

This is the architecture the project is actually about: an authoritative server
process with thin clients.

1. Start the dedicated server, following the
   [repository README](../README.md). Leave it running.
2. Press **Play** in Unity, enter a Client ID such as `1`, confirm the IP is
   `127.0.0.1` and the port matches the server's, then press **Start Client**.

To connect from another machine, replace `127.0.0.1` with the server machine's
IP address and make sure inbound UDP is permitted on that port.

### Running a second player

The Unity editor runs only one instance of the game, so the second player has
to be a standalone build:

1. **File → Build Settings**.
2. Press **Add Open Scenes** so `JumpingGame` is listed under *Scenes In Build*.
3. **Build**, choosing an output folder outside the project directory.
4. Run the resulting `.exe` and press **Start Client**, using a **different
   Client ID** from the editor client.

Both clients must point at the same server address and port.

### Controls

Hold the **left mouse button** to charge a jump and release it to launch. The
longer the charge, the further the jump. Falling off the platforms respawns the
player.

## How the player character is loaded

The player is not instantiated from a prefab in the project. It is loaded at
runtime from the AssetBundle at `Assets/StreamingAssets/jumpinggame`, registered
in `JumpingGame.Awake`:

```csharp
this._unityEntityManager.RegisterEntity(0, "/jumpinggame", "Player");
```

`Assets/GameDemo/JumpingGame/Player.prefab` is assigned to the `jumpinggame`
bundle, so rebuilding the bundles picks up any edits to it.

To rebuild after changing the prefab, or if the prebuilt bundle will not load in
your editor version, use the menu **Asset Bundles → Build Asset Bundles**. It
writes to `Assets/StreamingAssets/` targeting Windows x64.

## Troubleshooting

**`Failed to load entity prefab. assetBundlePath: /jumpinggame, prefabName: Player`**

The AssetBundle could not be read, almost always because the editor version
differs from the 2021.3.45 that built it. Rebuild the bundles through
**Asset Bundles → Build Asset Bundles**, then press Play again.

**Nothing happens when a button is pressed, and the Console reports a `NullReferenceException` from `MainMenu`**

The menu resolves its fields by name and expects a `GameManager` object in the
scene. This usually means the scene was opened with assets partially imported.
Reimport the project folder, or confirm all `.meta` files were copied.

**`InvalidOperationException` mentioning the Input System when pressing the mouse**

The client uses the legacy input API (`Input.GetMouseButton`). If the new Input
System package has been made active, set **Edit → Project Settings → Player →
Active Input Handling** back to **Input Manager (Old)** or **Both**.

**Clients connect but nothing is synchronised**

Framework settings must match between client and server. The client's values are
set in `JumpingGame.ConfigureFramework`; the server's defaults live in
`Framework/Configurations.cs`. Check that they agree.

**The editor is extremely slow to import or repeatedly reimports**

The project is most likely inside a OneDrive-synced folder. Move it outside
OneDrive.
