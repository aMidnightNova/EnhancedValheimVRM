# Changelog

## 1.4.0

### new features

- SpringBoneImmobile and SpringBoneImmobileType, same as Immobile on a physbone, for the whole avatar or one spring group
- SpringBoneMaxAngle, same as the angle limit on a physbone, works on vrm 0.x and 1.0 avatars
- vrm 1.0 avatars exported with spring angle limits now use them
- ShaderBundle=previous brings back the last versions shaders if the new ones give you trouble

### changed

- updated UniVRM to 0.131.2 for the unity 6 version of valheim
- a big avatars collision is never bigger than a normal players, so it fits wherever a player fits
- no longer ships Unity.Burst and Unity.Mathematics, the game has its own
- FastSpringBone and the VRMShaders dlls are gone from UniVRM, manual installs can delete them from valheim_Data/Managed

### fixed

- hair, tail and ear physics missing on vrm 1.0 avatars
- avatars with EnablePlayerFade on disappeared much closer than normal players
- arrows and magic came out at the wrong height on small and big avatars

## 1.3.1

### fixed

- in some cases the name of a previous created character saved to a newly created character casuing settings reload to swap the older character.
- some avatars showed white or missing textures for other players
- some avatars could get stuck walking into half walls

## 1.3.0

### fixed

- some shared avatars failed to load images, and didnt load for other players
- avatar sharing not starting unless ServerHost was set by hand

### new features

- players on a different version than the server get turned away with a popup showing both versions

## 1.2.0

### fixed

- instanes where vrm avatars could fail to load
- some models that had differnt scales could have giant weapons and / or left them unable to move
- some avatars failed with "VRM cache import failed" when a material only had one half of its metallic and occlusion map, they now load with the half that is there
- big frame spike while an avatar loads, mostly on models with lots of blendshapes
- dedicated servers logged a block of HarmonyX errors at startup
- the vrm collider was too small and didn't cover the head
- vrm avatars couldn't step onto things the normal character can
- with player fade off, held weapons vanished when the camera got close
- the forge hammer and hip lantern couldn't find where to attach on vrm avatars
- lanterns hung far below the hand on scaled avatars
- lanterns trailed behind the avatar while walking, running, jumping or rolling

### new features

- face streams: a VMC tracker drives your avatar's face and other players see it through the server
- rigged dual wield weapons like Skoll and Hati, the Berserkir axes and fists can have each hand and forearm piece moved and rotated on its own, see WEAPONS.md
- only the blendshapes an avatar uses get loaded, with a setting and an outfit file section to keep more
- `/vrm mesh <name> on | off`, `/vrm outfit blendshapes`
