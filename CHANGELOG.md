# Changelog

## 1.1.2

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
- `/vrm mesh <name> on | off`, `/vrm outfit blendshapes`, `/vrm dev reload [player]` and `/vrm dev capsule show | hide`
