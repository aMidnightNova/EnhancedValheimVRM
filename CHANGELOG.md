# Changelog

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
