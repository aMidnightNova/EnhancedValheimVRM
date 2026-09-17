# EnhancedValheimVRM

**This mod requires [BepInEx](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) to be installed.**

This mod is actively developed and maintained. If you need help, want to request a feature, or found a bug; Head on over to the [discord](https://discord.gg/q3wuVMCvXE).

**dont copy the settings from ValheimVRM over. Start from settings_Example.txt.example and only add back what you actually need. offsets work differently now and most avatars should need a lot less tuning.**

### Notes
If the model has MToon shaders you should not have ssao on, this is true even if you do not have MToon stuff enabled in the settings. You can set AttemptTextureFix to true, it will convert shaders to standard at game runtime.

### How to install
install it with r2modman (or the thunderstore app), it pulls in BepInEx for you.

### First time install
- run the game once. the mod makes a folder called EnhancedValheimVRM inside your valheim install folder, next to valheim.exe, and puts the example files in it.
- copy settings_Example.txt.example and name it after your character, E.G. settings_Midnight Nova.txt
- drop your vrm in the same folder with the same name, E.G. Midnight Nova.vrm

### Settings File
The name of the character in the game needs to correspond to a VRM and settings file like so.

**Character**: Midnight Nova \
**Settings File**: settings_Midnight Nova.txt \
**VRM**: Midnight Nova.vrm


### Default Settings and avatar for people you do not have custom stuff for.

**Settings File**: settings____Default.txt \
**VRM**: ___Default.vrm

**NOTE:** settings____Default.txt has 4 underscores, and ___Default.vrm has 3.

### Whats in the settings file
settings_Example.txt.example has everything with comments, but the short version.
- ModelScale, ModelBrightness, UseMToonShader, AttemptTextureFix, KeepAllBlendShapes, EnablePlayerFade, FixCameraHeight \
  the model stuff. AttemptTextureFix converts the textures to the games shader so it gets lit like everything else (and ssao works). \
  ShaderForTextureFix picks the game shader it converts to, player (default, the normal character shader) or creature (the animal shader). \
  TextureFixEmission (0 to 1) makes the avatar glow a bit with its own colors so it does not go black in shadow. only works with creature. 0.15 default.
- RightHandItemPos / LeftHandItemPos / RightHandBackItemPos / LeftHandBackItemPos and the matching *Rot ones \
  nudge where weapons sit. Pos is in meters along the sockets axes and scales with the avatar height, Rot is euler degrees.
- weapon lines, see the next section.
- SittingOnChairOffset \
  seated offset: X right, Y up, Z forward. Scales with avatar height.
- HelmetVisible, HelmetScale, HelmetOffset \
  vanilla helmet on or off and where it sits.
- ChestVisible, ShouldersVisible, UtilityVisible, LegsVisible \
  show the vanilla armor on top of the vrm. off by default because it usually looks wrong.
- SpringBoneStiffness, SpringBoneGravityPower \
  multipliers on what the vrm author set. 1.0 = leave it alone.
- InteractionDistanceScale \
  how far you can reach, small avatars might want a bit more.
- AllowShare \
  false means other people do not get your avatar. you still get theirs.

### Moving specific weapons
The Pos/Rot lines above move everything in a slot. If only one kind of weapon sits wrong you add a line for it, as many lines as you want. \
The number is <left/right, up/down, forward/back> in meters (0.02 = 2cm), Rot is degrees.

all bows on your back, move up 2cm
```
BowPos=<0, 0.02, 0>
```
all bows on your back, tilt 15 degrees
```
BowRot=<0, 0, 15>
```
just one bow. put the items name in front of the number. this one wins over the plain Bow line for that bow.
```
BowPos=BowDraugrFang,<0, 0.03, 0>
```
weapon while you are holding it. put Hand in the key.
```
SwordHandPos=<0, -0.01, 0>
KnifeHandRot=<0, 10, 0>
AxeHandPos=AxeBlackMetal,<0.01, 0, 0>
```
rigged weapons like Skoll and Hati, the Berserkir axes and the fists ignore the lines above. each piece gets its own line, the item name then LeftHand, RightHand, LeftForeArm or RightForeArm, then Pos or Rot. Rot spins the piece where its held, Pos moves it along the hand socket like other weapons. only the Knucklechains have forearm pieces.
```
KnifeSkollAndHatiLeftHandPos=<0, 0.01, 0>
KnifeSkollAndHatiRightHandRot=<0, 0, 15>
FistGoldLeftForeArmRot=<0, 10, 0>
```
the words you can use in front of Pos/Rot/HandPos/HandRot:
```
Bow Crossbow Sword Knife Club Axe Spear Polearm Staff Shield Tool Pickaxe Torch Fist
```
Mace works for Club, Atgeir for Polearm, Hammer for Tool, Dagger for Knife. \
Item names are in [WEAPONS.md](WEAPONS.md), sorted by class, together with the rigged weapons that get their own lines below. \
Turn on `/vrm settings auto on`, save the file, and the weapon moves while you look at it.

Sizing is automatic, it measures the avatar head to feet and shoulder to shoulder and sizes the collider off that. \
`/vrm settings reload` in chat reloads the file while in game. if the reload fails you keep the old avatar. \
`/vrm settings auto on` watches the settings and outfit files and reloads whichever one you save, every second, so you can tune offsets live. `/vrm settings auto off` stops it.
Model/interaction scale must be > 0, brightness and spring values can not be negative, anything that doesnt parse fails the reload.

### Outfits
Optional. Lets you turn meshes on and off and set blendshapes, and switch between sets in game. \
The file is outfits_<vrm filename in lowercase>.txt next to the vrm. so Midnight.vrm -> outfits_midnight.txt \
outfits_Example.txt.example shows the format.
- each [Section] is an outfit, name it whatever. exactly one needs Default=True, that is the one you spawn with.
- mesh:<renderer name>=True/False
- blendshape:<renderer name>:<blendshape name>=0 to 100
- names have to match the vrm exactly, spaces and caps included. Missing ones get logged, they dont break the avatar.
- a [Blendshapes] section at the bottom lists blendshapes to keep loaded, one per line.

Only blendshapes the vrm's expressions or the outfit file use get loaded, the rest are left empty so models with hundreds of them load faster and cause less of a frame spike. \
KeepAllBlendShapes=true in the settings file loads all of them.

`/vrm outfit generate` writes a starting file with every mesh in it if you dont have one. it will not overwrite an existing file. \
`/vrm outfit blendshapes` adds a [Blendshapes] section listing every blendshape the avatar has, commented out. remove the # on the ones you want kept.

Chat commands (F5 console works too, without the slash)
```
/vrm outfit list
/vrm outfit next
/vrm outfit set <name>
/vrm outfit reload
/vrm outfit generate
/vrm outfit blendshapes
/vrm mesh <name> on | off
/vrm toggle <mesh>
/vrm blend <blendshape> <0-100>
/vrm settings reload
/vrm settings auto on | off
```
toggle and blend are quick overrides, they dont touch the file and reset when you pick an outfit. \
Outfit changes show up for other players right away, they only need to have your avatar already.

### Avatar sharing
Other players get your avatar automatically, but ONLY on a dedicated server running this mod. Player hosted worlds dont share, local avatars still work there.
- install the same release on the dedicated server and on every client.
- the server needs a TCP port open. set it in the servers config under [Sharing] ServerPort (default 6067) and open that port on the box / in your hosting panel. \
  clients get the port from the server automatically, the ServerPort in a clients config is ignored.
- face streams use the same port number on UDP. open 6067 as UDP too, or set [Sharing] FacePort to a different port number. TCP alone is not enough for faces. \
  valheim itself uses the game port and the one above it on UDP, so dont put the sharing port right next to the game port.
- if your server is only reachable through steam relay, set ServerHost in the clients config to the servers real address.
- the first time somebody joins with a big avatar it takes a while. 6 Mbps upload default, change UploadMbps in the config if your connection can do more (max 100). \
  the server has its own per upload limit, DedicatedUploadMbps (default 60, hard max 100), and tells clients about it. you upload at whichever of the two is lower.

Config keys that matter (BepInEx/config/com.rawrtastic.plugins.enhancedvalheimvrm.cfg)
```
[General]  EnableVrmSharing = true      turn sharing off entirely on this machine
[Sharing]  UploadMbps = 6               your upload rate, 1 to 100
[Sharing]  DedicatedUploadMbps = 60     server only, per upload, 1 to 100. clients use the lower of this and their UploadMbps
[Sharing]  ServerPort = 6067            server only
[Sharing]  EnableServer = true          server only
[Sharing]  DedicatedDownloadMbps = 25   server only, per download
[Sharing]  DedicatedDownloadSlots = 4   server only, downloads at once
[Sharing]  MaxBundleMiB = 384           server only, biggest avatar it will accept
[Sharing]  FacePort = 0                 server only, UDP port for faces, 0 means ServerPort on UDP
[Face]     Enabled = false              listen for VMC and send your face
[Face]     ReceiveFaceStreams = true    show other players faces
[Face]     VmcPort = 39539              where your tracker sends VMC, this pc only
[Face]     SendRate = 30                face frames per second you send, 15 to 60
[Face]     JitterBufferMs = 20          how late a face packet may be and still show, later ones are dropped. raise on a bad connection.
```


### Face streams

Face streams let a tracker drive your avatars expressions in real time. You see them locally, and other players can see them too. Only the face is affected; head movement still comes from the game.

- Run a VMC compatible tracker. Send its output to `127.0.0.1` using `VmcPort`(127.0.0.1:39539).
- Set `[Face] Enabled = true` in your config. Your avatar will start following the tracker in game, in single player, and on dedicated servers that have the port open.
- Other players can see your expressions when playing through a dedicated server with its UDP face port open. Avatar sharing has to be enabled for this to work.
- Your avatar needs the 52 ARKit blendshapes, with matching names. Lipsync also supports the 14 visemes (`PP`, `FF`, `TH`, `DD`, `kk`, `CH`, `SS`, `nn`, `RR`, `aa`, `E`, `ih`, `oh`, and `ou`), presets such as `blink`, and `happy` are left alone, missing blendshapes are skipped.
- Set `ReceiveFaceStreams = false` if you do not want to receive or process other players' expressions.
- `[General] LogLevel = Debug` in the config logs every step of the face stream if it is not showing up.

Downloaded avatars are cached in EnhancedValheimVRM/Shared, the server keeps them in EnhancedValheimVRM/Server. Delete those to force a redownload.
If you put someones vrm and settings file in your folder yourself, that is used instead of the shared one.

### Usefull Info
- If you have a shader compile error you probably need to use the old shader bundle. \
  the newer current bundle should work, but JIC ive included the old one still\
  Its in General settings. ShaderBundle=<old,current>. Note that this will affect all models.
- if the mod says sharing is unavailable because the server did not answer, the server and clients are on different versions. update all of them and restart.

