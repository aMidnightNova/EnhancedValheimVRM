using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EnhancedValheimVRM.Sharing;
using UnityEngine;
using VRM;

namespace EnhancedValheimVRM
{
    public static class VrmController
    {
        // Includes the staging candidate so equipment hooks see its sockets during commit.
        private static readonly Dictionary<Player, VrmInstance> ActiveInstances = new Dictionary<Player, VrmInstance>();
        private static readonly HashSet<Player> Installing = new HashSet<Player>();

        private static readonly HashSet<Player> InitialInstalls = new HashSet<Player>();

        // Captured before the first VRM setup, never recaptured from a resized/reloaded avatar.
        private sealed class VanillaState
        {
            internal bool Disabled, Switching;
            internal float Interaction, Height, Radius;
            internal Vector3 Center, MassCenter, EyePosition;
            internal bool AutomaticMassCenter, KeepAnimatorState;
            internal AnimatorCullingMode Culling;

            internal readonly List<Tuple<Transform, Transform, Vector3, Quaternion, Vector3>> Sockets =
                new List<Tuple<Transform, Transform, Vector3, Quaternion, Vector3>>();

            internal readonly List<Tuple<SkinnedMeshRenderer, bool, bool, bool>> Renderers =
                new List<Tuple<SkinnedMeshRenderer, bool, bool, bool>>();

            internal VanillaState(Player player)
            {
                Interaction = player.m_maxInteractDistance;
                var capsule = player.GetComponent<CapsuleCollider>();
                if (capsule != null)
                {
                    Height = capsule.height;
                    Radius = capsule.radius;
                    Center = capsule.center;
                }

                var body = player.GetComponent<Rigidbody>();
                if (body != null)
                {
                    MassCenter = body.centerOfMass;
                    AutomaticMassCenter = body.automaticCenterOfMass;
                }

                if (player.m_eye != null) EyePosition = player.m_eye.localPosition;
                var animator = player.GetField<Player, Animator>("m_animator");
                if (animator != null)
                {
                    Culling = animator.cullingMode;
                    KeepAnimatorState = animator.keepAnimatorStateOnDisable;
                }

                var equipment = player.GetComponent<VisEquipment>();
                if (equipment != null)
                {
                    foreach (var socket in new[]
                             {
                                 equipment.m_leftHand, equipment.m_rightHand, equipment.m_helmet,
                                 equipment.m_backShield, equipment.m_backMelee, equipment.m_backTwohandedMelee,
                                 equipment.m_backBow, equipment.m_backTool, equipment.m_backAtgeir
                             })
                    {
                        if (socket != null)
                        {
                            Sockets.Add(Tuple.Create(socket,
                                socket.parent,
                                socket.localPosition,
                                socket.localRotation,
                                socket.localScale));
                        }
                    }
                }

                var imported = FindSharingInstance(player)?.GetGameObject();
                foreach (var renderer in player.GetVisual().GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (imported == null || !renderer.transform.IsChildOf(imported.transform))
                    {
                        Renderers.Add(Tuple.Create(renderer,
                            !WasPreHidden(player, renderer) && renderer.forceRenderingOff,
                            renderer.updateWhenOffscreen,
                            renderer.enabled));
                    }
                }
            }

            internal void Restore(Player player)
            {
                foreach (var saved in Sockets)
                {
                    if (saved.Item1 == null || saved.Item2 == null) continue;
                    saved.Item1.SetParent(saved.Item2, false);
                    saved.Item1.SetLocalPositionAndRotation(saved.Item3, saved.Item4);
                    saved.Item1.localScale = saved.Item5;
                }

                var capsule = player.GetComponent<CapsuleCollider>();
                if (capsule != null)
                {
                    capsule.height = Height;
                    capsule.radius = Radius;
                    capsule.center = Center;
                }

                var body = player.GetComponent<Rigidbody>();
                if (body != null)
                {
                    if (AutomaticMassCenter)
                        body.ResetCenterOfMass();
                    else
                        body.centerOfMass = MassCenter;
                }

                player.m_maxInteractDistance = Interaction;
                if (player.m_eye != null) player.m_eye.localPosition = EyePosition;
                var animator = player.GetField<Player, Animator>("m_animator");
                if (animator != null)
                {
                    animator.cullingMode = Culling;
                    animator.keepAnimatorStateOnDisable = KeepAnimatorState;
                }

                foreach (var saved in Renderers)
                {
                    if (saved.Item1 != null)
                    {
                        saved.Item1.forceRenderingOff = saved.Item2;
                        saved.Item1.updateWhenOffscreen = saved.Item3;
                        saved.Item1.enabled = saved.Item4;
                    }
                }
            }
        }

        private static readonly Dictionary<Player, VanillaState> VanillaStates = new Dictionary<Player, VanillaState>();

        private static bool IsDisabled(Player player)
        {
            return player != null && VanillaStates.TryGetValue(player, out var state) && state.Disabled;
        }

        internal static bool InitialLocalAvatarPending
        {
            get
            {
                var player = Player.m_localPlayer;
                return player != null && !player.IsDead() && InitialInstalls.Contains(player);
            }
        }

        public static VrmInstance FindInstance(Player player)
        {
            return IsDisabled(player) ? null : FindSharingInstance(player);
        }

        // Publication/key handling remains available while local collision testing hides the VRM.
        internal static VrmInstance FindSharingInstance(Player player)
        {
            return player != null &&
                ActiveInstances.TryGetValue(player, out var instance)
                    ? instance
                    : null;
        }

        public static void AttachSharedVrm(Player player,
            AvatarBundle bundle,
            Action<bool> completed,
            CancellationToken cancellation,
            string receiveTiming,
            double beforeImportMilliseconds)
        {
            CoroutineHelper.Instance.StartCoroutine(InstallShared(player,
                bundle,
                completed,
                cancellation,
                receiveTiming,
                beforeImportMilliseconds));
        }

        private static IEnumerator InstallShared(Player player,
            AvatarBundle bundle,
            Action<bool> completed,
            CancellationToken cancellation,
            string receiveTiming,
            double beforeImportMilliseconds)
        {
            // A downloaded replacement waits for an existing install instead of throwing
            // away decrypted bytes and performing the receive/decrypt work a second time.
            while (Installing.Contains(player) && player != null && !cancellation.IsCancellationRequested)
                yield return null;
            if (player == null || cancellation.IsCancellationRequested || IsDisabled(player))
            {
                if (bundle != null)
                {
                    if (bundle.Vrm != null)
                        System.Threading.Tasks.Task.Run(() => Array.Clear(bundle.Vrm, 0, bundle.Vrm.Length));
                }

                completed(false);
                yield break;
            }

            Installing.Add(player);
            VrmInstance candidate = null;
            var succeeded = false;
            var previous = FindInstance(player);
            if (!(previous?.IsDisplayed ?? false)) InitialInstalls.Add(player);
            var attachments = new List<Tuple<Transform, Transform, Vector3, Quaternion, Vector3>>();
            var renderers = new List<Tuple<SkinnedMeshRenderer, bool, bool>>();
            CapsuleCollider collider = null;
            Rigidbody body = null;
            float oldHeight = 0, oldRadius = 0, oldInteractDistance = 0;
            Vector3 oldCenter = Vector3.zero, oldMassCenter = Vector3.zero;
            var setupStarted = false;
            try
            {
                if (cancellation.IsCancellationRequested || player == null || player.IsDead() ||
                    (bundle != null && player.GetPlayerID() != bundle.CharacterId))
                    yield break;
                try
                {
                    candidate = bundle == null ? new VrmInstance(player) : new VrmInstance(player, bundle);
                    candidate.SetReceiveTiming(receiveTiming, beforeImportMilliseconds);
                }
                catch (Exception ex)
                {
                    if (bundle != null || !(ex is System.IO.FileNotFoundException))
                        Logger.LogWarning("VRM import failed: " + ex);
                    yield break;
                }

                while ((candidate.IsLoading || (candidate.GetGameObject() != null && !candidate.IsReady)) &&
                       player != null && !cancellation.IsCancellationRequested)
                    yield return null;
                if (cancellation.IsCancellationRequested || player == null || player.IsDead() ||
                    (bundle != null && player.GetPlayerID() != bundle.CharacterId) ||
                    candidate.GetGameObject() == null)
                    yield break;

                var equipment = player.GetComponent<VisEquipment>();
                if (equipment != null)
                {
                    foreach (var point in new[]
                             {
                                 equipment.m_leftHand, equipment.m_rightHand, equipment.m_helmet,
                                 equipment.m_backShield, equipment.m_backMelee, equipment.m_backTwohandedMelee,
                                 equipment.m_backBow, equipment.m_backTool, equipment.m_backAtgeir
                             })
                    {
                        if (point != null)
                        {
                            attachments.Add(Tuple.Create(point,
                                point.parent,
                                point.localPosition,
                                point.localRotation,
                                point.localScale));
                        }
                    }
                }

                foreach (var renderer in player.GetVisual().GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderers.Add(Tuple.Create(renderer,
                        !WasPreHidden(player, renderer) && renderer.forceRenderingOff,
                        renderer.updateWhenOffscreen));
                }

                collider = player.GetComponent<CapsuleCollider>();
                body = player.GetComponent<Rigidbody>();
                if (collider != null)
                {
                    oldHeight = collider.height;
                    oldRadius = collider.radius;
                    oldCenter = collider.center;
                }

                if (body != null) oldMassCenter = body.centerOfMass;
                oldInteractDistance = player.m_maxInteractDistance;
                setupStarted = true;
                ActiveInstances[player] = candidate;
                var setup = VrmSetup(player, candidate);
                try
                {
                    while (player != null && !player.IsDead() && !cancellation.IsCancellationRequested)
                    {
                        bool more;
                        try
                        {
                            more = setup.MoveNext();
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning("VRM setup failed: " + ex);
                            yield break;
                        }

                        if (!more)
                        {
                            succeeded = candidate.GetGameObject()?.GetComponent<VrmAnimator>() != null;
                            break;
                        }

                        // As in the working commit, import/clone is synchronous in
                        // the menu, but setup yields so sibling components finish Awake.
                        yield return setup.Current;
                    }
                }
                finally
                {
                    (setup as IDisposable)?.Dispose();
                }

                if (succeeded)
                {
                    Expected.Remove(player); // The avatar hides the vanilla body from here on.
                    if (player.GetPlayerID() != 0) KnownAvatars.Add(player.GetPlayerID());
                    candidate.ShowModel();
                    var outfit = previous?.GetGameObject()?.GetComponent<OutfitController>()?.CurrentName;
                    if (!string.IsNullOrEmpty(outfit))
                        candidate.GetGameObject().GetComponent<OutfitController>()?.Apply(outfit);
                    previous?.Dispose();
                    OutfitRpc.AvatarReady(player);
                }
            }
            finally
            {
                if (candidate == null && bundle != null)
                {
                    if (bundle.Vrm != null)
                        System.Threading.Tasks.Task.Run(() => Array.Clear(bundle.Vrm, 0, bundle.Vrm.Length));
                }

                if (!succeeded)
                {
                    foreach (var saved in attachments)
                    {
                        if (saved.Item1 == null) continue;
                        saved.Item1.SetParent(saved.Item2, false);
                        saved.Item1.localPosition = saved.Item3;
                        saved.Item1.localRotation = saved.Item4;
                        saved.Item1.localScale = saved.Item5;
                    }

                    if (!ReferenceEquals(player, null))
                    {
                        if (ActiveInstances.TryGetValue(player, out var active) && active == candidate)
                        {
                            if (previous != null)
                                ActiveInstances[player] = previous;
                            else
                                ActiveInstances.Remove(player);
                        }

                        if (player != null && setupStarted)
                        {
                            player.m_maxInteractDistance = oldInteractDistance;
                            if (collider != null)
                            {
                                collider.height = oldHeight;
                                collider.radius = oldRadius;
                                collider.center = oldCenter;
                            }

                            if (body != null) body.centerOfMass = oldMassCenter;
                            foreach (var saved in renderers)
                            {
                                if (saved.Item1 == null) continue;
                                saved.Item1.forceRenderingOff = saved.Item2;
                                saved.Item1.updateWhenOffscreen = saved.Item3;
                            }

                            if (previous != null && player.TryGetComponent<VisEquipment>(out var equipment))
                                PatchVisEquipmentUpdateLodgroup.Apply(equipment, previous);
                            var eye = player.GetComponent<VrmEyeAnimator>();
                            if (eye != null)
                            {
                                if (previous != null)
                                    eye.Setup(player, player.GetField<Player, Animator>("m_animator"), previous);
                                else
                                    UnityEngine.Object.Destroy(eye);
                            }
                        }
                    }

                    candidate?.Dispose();
                }

                InitialInstalls.Remove(player);
                Installing.Remove(player);
                completed(succeeded);
            }
        }

        private static readonly HashSet<Player> Reloading = new HashSet<Player>();

        // ---- Auto reload -----------------------------------------------------------------
        // Checks the local settings and outfit files once a second and reloads whichever one
        // changed, so offsets can be tuned with the game running. Off by default; ends with
        // the player object.
        private static Coroutine _autoReload;
        private static Player _autoReloadPlayer;

        internal static string SetAutoReload(Player player, bool enabled)
        {
            if (_autoReload != null)
            {
                CoroutineHelper.Instance.StopCoroutine(_autoReload);
                _autoReload = null;
                _autoReloadPlayer = null;
            }

            if (!enabled) return "Settings auto reload off.";
            if (player == null || FindSharingInstance(player) == null) return "Load a character with a VRM first.";
            _autoReloadPlayer = player;
            _autoReload = CoroutineHelper.Instance.StartCoroutine(AutoReload(player));
            return "Settings auto reload on: the settings and outfit files are checked every second.";
        }

        private static IEnumerator AutoReload(Player player)
        {
            var wait = new WaitForSeconds(1f);
            DateTime settingsStamp = DateTime.MinValue, outfitStamp = DateTime.MinValue;
            var primed = false;
            while (player != null && player == Player.m_localPlayer)
            {
                var instance = FindSharingInstance(player);
                if (instance != null && instance.IsDisplayed && !instance.IsCorpse && !Reloading.Contains(player))
                {
                    var settingsPath = instance.GetSettings()?.GetSettingsFilePath();
                    var vrmPath = instance.GetVrmFilePath();
                    var outfitPath = string.IsNullOrEmpty(vrmPath)
                        ? null
                        : System.IO.Path.Combine(Constants.Vrm.Dir,
                            "outfits_" + System.IO.Path.GetFileNameWithoutExtension(vrmPath).ToLowerInvariant() +
                            ".txt");
                    var settingsNow = Stamp(settingsPath);
                    var outfitNow = Stamp(outfitPath);
                    if (!primed)
                    {
                        settingsStamp = settingsNow;
                        outfitStamp = outfitNow;
                        primed = true;
                    }
                    else if (settingsNow != settingsStamp)
                    {
                        settingsStamp = settingsNow;
                        outfitStamp = outfitNow;
                        ReloadPlayer(player); // A settings reload also re-reads the outfit file.
                    }
                    else if (outfitNow != outfitStamp)
                    {
                        outfitStamp = outfitNow;
                        instance.GetGameObject()?.GetComponent<OutfitController>()?.Reload();
                    }
                }

                yield return wait;
            }

            _autoReload = null;
            _autoReloadPlayer = null;
        }

        private static DateTime Stamp(string path)
        {
            try
            {
                return !string.IsNullOrEmpty(path) && System.IO.File.Exists(path)
                    ? System.IO.File.GetLastWriteTimeUtc(path)
                    : DateTime.MinValue;
            }
            catch (System.IO.IOException)
            {
                return DateTime.MinValue;
            }
        }

        public static void ReloadPlayer(Player player)
        {
            if (player == null || player.IsDead() || IsDisabled(player) || !Reloading.Add(player)) return;
            var replacingAvatar = FindInstance(player) != null;
            CoroutineHelper.Instance.StartCoroutine(InstallShared(player,
                null,
                success =>
                {
                    Reloading.Remove(player);
                    if (success)
                        FileTransferController.RefreshUpload();
                    else if (replacingAvatar)
                        Logger.LogWarning("VRM reload failed; previous avatar and settings retained.");
                },
                default,
                null,
                0));
        }

        public static void AttachVrmToPlayer(Player player)
        {
            if (FindInstance(player) == null) ReloadPlayer(player);
        }

        public static void DetachVrmFromPlayer(Player player)
        {
            InitialInstalls.Remove(player);
            VanillaStates.Remove(player);
            Reloading.Remove(player);
            Expected.Remove(player);
            DeadCorpses.Remove(player);
            if (ActiveInstances.TryGetValue(player, out var shared))
            {
                ActiveInstances.Remove(player);
                // A remote player that died keeps its model for a moment: the corpse may not
                // have arrived over the network yet.
                if (player != Player.m_localPlayer && shared.IsDisplayed && !shared.IsCorpse &&
                    shared.GetGameObject() != null && (shared.DeathSeen || player.IsDead()))
                    KeepForCorpse(shared, player.transform.position, CorpseSignature.Read(player));
                else
                    shared.Dispose();
            }
        }

        // ---- Corpses ---------------------------------------------------------------------
        // The game hands a ragdoll only to the client that owns the dying character. On every
        // other client the ragdoll appears through the network and the dead player object is
        // destroyed, in either order, so the model is claimed by the nearest player ragdoll.
        private sealed class Orphan
        {
            internal VrmInstance Instance;
            internal Vector3 Position;
            internal CorpseSignature Signature;
            internal float DiedAt;
        }

        // What the game copies from a dying player onto its ragdoll so the corpse looks like
        // them: body model, skin and hair colours, worn items. Read from either object's ZDO.
        private struct CorpseSignature
        {
            private int _model, _beard, _hair, _helmet, _chest, _legs;
            private Vector3 _skin, _hairColor;
            internal bool Known;

            internal static CorpseSignature Read(Component owner)
            {
                var signature = new CorpseSignature();
                var zdo = owner != null ? owner.GetComponent<ZNetView>()?.GetZDO() : null;
                if (zdo == null) return signature;
                signature._model = zdo.GetInt(ZDOVars.s_modelIndex, -1);
                signature._skin = zdo.GetVec3(ZDOVars.s_skinColor, Vector3.zero);
                signature._hairColor = zdo.GetVec3(ZDOVars.s_hairColor, Vector3.zero);
                signature._beard = zdo.GetInt(ZDOVars.s_beardItem, 0);
                signature._hair = zdo.GetInt(ZDOVars.s_hairItem, 0);
                signature._helmet = zdo.GetInt(ZDOVars.s_helmetItem, 0);
                signature._chest = zdo.GetInt(ZDOVars.s_chestItem, 0);
                signature._legs = zdo.GetInt(ZDOVars.s_legItem, 0);
                signature.Known = signature._model >= 0 &&
                    (signature._skin != Vector3.zero || signature._hair != 0 || signature._beard != 0);
                return signature;
            }

            internal bool Matches(CorpseSignature other)
            {
                return Known && other.Known && _model == other._model &&
                    _skin == other._skin && _hairColor == other._hairColor && _beard == other._beard &&
                    _hair == other._hair &&
                    _helmet == other._helmet && _chest == other._chest && _legs == other._legs;
            }
        }

        private static readonly List<Orphan> Orphans = new List<Orphan>();
        private const float CorpseClaimRadius = 4f;

        // The game keeps a dead player object alive until the owner respawns, and the killing
        // blow's pushback keeps moving it because motion control stops at death. The name tag
        // is drawn at that object's head point, so for a dead player it follows the corpse instead.
        private static readonly Dictionary<Player, VrmInstance> DeadCorpses = new Dictionary<Player, VrmInstance>();

        internal static VrmInstance FindCorpseFor(Player player)
        {
            return player != null && DeadCorpses.TryGetValue(player, out var corpse) && corpse.GetGameObject() != null
                ? corpse
                : null;
        }

        internal static bool TransferToRagdoll(VrmInstance vrm, Ragdoll ragdoll)
        {
            var model = vrm?.GetGameObject();
            if (model == null || ragdoll == null || vrm.IsCorpse) return false;
            var avatar = vrm.GetVrmGoAnimator();
            if (avatar == null) return false;
            var animation = model.GetComponent<VrmAnimator>();
            var transfer = model.AddComponent<VrmRagdoll>();
            if (!transfer.Setup(ragdoll, avatar, animation?.Offsets))
            {
                UnityEngine.Object.Destroy(transfer);
                Logger.LogWarning("Cannot map corpse hips; keeping Valheim's ragdoll visible.");
                return false;
            }

            foreach (var renderer in ragdoll.GetComponentsInChildren<SkinnedMeshRenderer>())
                renderer.forceRenderingOff = true;
            if (animation != null)
            {
                animation.enabled = false;
                UnityEngine.Object.Destroy(animation);
            }

            // Preserve the current world pose and let the game's corpse lifetime own
            // the model and its imported resources, independently of respawn.
            model.transform.SetParent(ragdoll.transform, true);
            vrm.ShowModel();
            vrm.IsCorpse = true;
            return true;
        }

        internal static void OnRagdollAppeared(Ragdoll ragdoll)
        {
            // Only player corpses carry equipment visuals.
            if (ragdoll == null || ragdoll.GetComponentInChildren<VisEquipment>(true) == null) return;
            CoroutineHelper.Instance.StartCoroutine(ClaimRagdoll(ragdoll));
        }

        private static IEnumerator ClaimRagdoll(Ragdoll ragdoll)
        {
            // The same network update can also destroy the dead player object; let it settle.
            yield return null;
            if (ragdoll == null) yield break;
            var position = ragdoll.transform.position;
            var signature = CorpseSignature.Read(ragdoll);
            // Candidates are recently dead players near the corpse. The equipment/colour
            // signature the game copied onto the ragdoll decides between them; distance only
            // breaks ties or stands in when the ragdoll's data has not arrived yet.
            var radius = CorpseClaimRadius * CorpseClaimRadius;
            Player bestPlayer = null;
            Orphan bestOrphan = null;
            var bestScore = float.MaxValue;
            foreach (var pair in ActiveInstances)
            {
                var player = pair.Key;
                if (player == null || player == Player.m_localPlayer || pair.Value.IsCorpse || !pair.Value.IsDisplayed)
                    continue;
                if (!(pair.Value.DeathSeen || player.IsDead())) continue;
                var score = Score((player.transform.position - position).sqrMagnitude,
                    radius,
                    signature,
                    CorpseSignature.Read(player));
                if (score < bestScore)
                {
                    bestScore = score;
                    bestPlayer = player;
                    bestOrphan = null;
                }
            }

            foreach (var candidate in Orphans)
            {
                var score = Score((candidate.Position - position).sqrMagnitude,
                    radius,
                    signature,
                    candidate.Signature);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestOrphan = candidate;
                    bestPlayer = null;
                }
            }

            if (bestPlayer != null)
            {
                if (Settings.LogLoadTiming)
                {
                    Logger.Log(FileTransferController.PlayerLabel(bestPlayer) +
                        ": corpse claimed while the player object still exists");
                }

                TransferToRagdoll(ActiveInstances[bestPlayer], ragdoll);
            }
            else if (bestOrphan != null)
            {
                Orphans.Remove(bestOrphan);
                if (Settings.LogLoadTiming)
                {
                    Logger.Log("Corpse at " + position.ToString("F1") + " claimed " +
                        (Time.realtimeSinceStartup - bestOrphan.DiedAt).ToString("F1") + "s after death, " +
                        Mathf.Sqrt((bestOrphan.Position - position).sqrMagnitude).ToString("F1") +
                        "m from where the player fell");
                }

                if (!TransferToRagdoll(bestOrphan.Instance, ragdoll)) bestOrphan.Instance.Dispose();
            }
        }

        // Lower is better; MaxValue rules a candidate out.
        private static float Score(float squaredDistance,
            float squaredRadius,
            CorpseSignature ragdoll,
            CorpseSignature player)
        {
            if (squaredDistance > squaredRadius) return float.MaxValue;
            if (ragdoll.Known && player.Known) return ragdoll.Matches(player) ? squaredDistance : float.MaxValue;
            return squaredDistance + squaredRadius; // Unknown signature: distance alone, ranked below any match.
        }

        internal static bool ParkDeadRemote(Player player, VrmInstance instance)
        {
            if (player == null || instance == null || instance.IsCorpse || !instance.IsDisplayed ||
                instance.GetGameObject() == null)
                return false;
            if (!ActiveInstances.TryGetValue(player, out var active) || !ReferenceEquals(active, instance))
                return false;
            ActiveInstances.Remove(player);
            DeadCorpses[player] = instance;
            KeepForCorpse(instance, player.transform.position, CorpseSignature.Read(player));
            if (Settings.LogLoadTiming)
            {
                Logger.Log(FileTransferController.PlayerLabel(player) + ": died at " +
                    player.transform.position.ToString("F1") + "; model frozen until the corpse arrives");
                CoroutineHelper.Instance.StartCoroutine(TraceDeadObject(player));
            }

            return true;
        }

        // Diagnostic: follows a dead remote player object until the game destroys it, so a
        // moving name tag can be tied to that object rather than to the corpse or the model.
        private static IEnumerator TraceDeadObject(Player player)
        {
            var label = FileTransferController.PlayerLabel(player);
            var last = player.transform.position;
            var start = Time.realtimeSinceStartup;
            while (player != null)
            {
                var now = player.transform.position;
                if ((now - last).sqrMagnitude > 0.25f)
                {
                    var body = player.GetComponent<Rigidbody>();
                    Logger.Log(label + " dead object moved to " + now.ToString("F1") + " at +" +
                        (Time.realtimeSinceStartup - start).ToString("F2") + "s" +
                        (body != null
                            ? "; velocity " + body.velocity.magnitude.ToString("F1") + " m/s, kinematic=" +
                            body.isKinematic
                            : "") +
                        "; dead=" + player.IsDead());
                    last = now;
                }

                yield return null;
            }

            Logger.Log(label + " dead object destroyed at +" + (Time.realtimeSinceStartup - start).ToString("F2") +
                "s; last position " + last.ToString("F1"));
        }

        private static void KeepForCorpse(VrmInstance instance, Vector3 position, CorpseSignature signature)
        {
            var model = instance.GetGameObject();
            model.transform.SetParent(null, true);
            var animation = model.GetComponent<VrmAnimator>();
            if (animation != null) animation.enabled = false; // Keep the last pose until the corpse claims it.
            var orphan = new Orphan
            {
                Instance = instance, Position = position, Signature = signature, DiedAt = Time.realtimeSinceStartup
            };
            Orphans.Add(orphan);
            CoroutineHelper.Instance.StartCoroutine(ExpireOrphan(orphan));
        }

        private static IEnumerator ExpireOrphan(Orphan orphan)
        {
            yield return new WaitForSeconds(5f);
            if (!Orphans.Remove(orphan)) yield break;
            if (Settings.LogLoadTiming) Logger.Log("No corpse arrived for a dead player within 5s; model removed");
            orphan.Instance.Dispose();
        }

        // ---- Expected avatars ------------------------------------------------------------
        // Once a character's avatar has been shown on this client, its vanilla body is never
        // shown again: a respawned or rejoined player object is hidden from the moment it
        // appears until the avatar is installed. A character's very first load is not hidden.
        private static readonly HashSet<long> KnownAvatars = new HashSet<long>();

        private static readonly Dictionary<Player, List<SkinnedMeshRenderer>> Expected =
            new Dictionary<Player, List<SkinnedMeshRenderer>>();

        internal static bool HasKnownAvatar(long characterId)
        {
            return characterId != 0 && KnownAvatars.Contains(characterId);
        }

        internal static void ExpectSharedAvatar(Player player)
        {
            if (player == null || player == Player.m_localPlayer || Expected.ContainsKey(player) ||
                FindSharingInstance(player) != null)
                return;
            var visual = player.GetVisual();
            if (visual == null) return;
            var hidden = new List<SkinnedMeshRenderer>();
            foreach (var renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!renderer.forceRenderingOff)
                {
                    renderer.forceRenderingOff = true;
                    hidden.Add(renderer);
                }
            }

            Expected[player] = hidden;
        }

        // Only when the avatar is definitely not coming (the server refused it or the owner
        // withdrew it) does the vanilla body come back.
        internal static void RevealVanilla(Player player)
        {
            if (player == null || !Expected.TryGetValue(player, out var hidden)) return;
            Expected.Remove(player);
            if (FindSharingInstance(player)?.IsDisplayed == true) return;
            foreach (var renderer in hidden)
            {
                if (renderer != null) renderer.forceRenderingOff = false;
            }
        }

        private static bool WasPreHidden(Player player, SkinnedMeshRenderer renderer)
        {
            return Expected.TryGetValue(player, out var hidden) && hidden.Contains(renderer);
        }

        internal static string SetLocalEnabled(bool enabled)
        {
            var player = Player.m_localPlayer;
            if (player == null || player.IsDead()) return "Enter the world with a living character first.";
            if (Installing.Contains(player) || Reloading.Contains(player))
                return "Wait for the current avatar setup to finish.";
            var avatar = FindSharingInstance(player);
            if (avatar == null || !avatar.IsDisplayed || avatar.IsCorpse ||
                !VanillaStates.TryGetValue(player, out var baseline))
                return "Load your VRM avatar first.";
            if (baseline.Switching) return "The VRM switch is already in progress.";
            if (baseline.Disabled == !enabled)
                return enabled ? "Local VRM is already on." : "Local VRM is already off.";
            if (!enabled)
            {
                DisableLocal(player, avatar, baseline);
                return
                    "Local VRM off: vanilla visuals, equipment, collider and camera restored. /vrm dev on restores the VRM.";
            }

            baseline.Switching = true;
            CoroutineHelper.Instance.StartCoroutine(EnableLocal(player, avatar, baseline));
            return "Restoring your local VRM from its retained model.";
        }

        private static void DisableLocal(Player player, VrmInstance avatar, VanillaState baseline)
        {
            baseline.Disabled = true; // All VRM-dependent game patches now take their vanilla path.
            var eye = player.GetComponent<VrmEyeAnimator>();
            if (eye != null) eye.enabled = false;
            player.GetComponent<BoneGizmos>()?.SetVisible(false);
            baseline.Restore(player); // Move equipment sockets back before hiding the avatar hierarchy.
            avatar.GetGameObject()?.SetActive(false);
            if (player.TryGetComponent<VisEquipment>(out var equipment))
            {
                // These cached objects were hidden by VRM setup; force vanilla to recreate them too.
                HarmonyLib.AccessTools.Field(typeof(VisEquipment), "m_currentHairItemHash")
                    ?.SetValue(equipment, int.MinValue);
                HarmonyLib.AccessTools.Field(typeof(VisEquipment), "m_currentBeardItemHash")
                    ?.SetValue(equipment, int.MinValue);
                PatchVisEquipmentUpdateLodgroup.RecreateEquipment(equipment);
            }
        }

        private static IEnumerator EnableLocal(Player player, VrmInstance avatar, VanillaState baseline)
        {
            var restored = false;
            var setup = VrmSetup(player, avatar);
            baseline.Disabled = false;
            avatar.GetGameObject().GetComponent<OutfitController>()?.SetStaging(true);
            try
            {
                while (player != null && !player.IsDead())
                {
                    bool more;
                    try
                    {
                        more = setup.MoveNext();
                    }
                    catch (Exception error)
                    {
                        Logger.LogError("Cannot restore local VRM: " + error.Message);
                        yield break;
                    }

                    if (!more)
                    {
                        avatar.GetGameObject().GetComponent<OutfitController>()?.SetStaging(false);
                        restored = true;
                        Logger.Log("Local VRM on: restored VRM collider, equipment and camera settings.");
                        break;
                    }

                    yield return setup.Current;
                }
            }
            finally
            {
                (setup as IDisposable)?.Dispose();
                if (!restored && player != null && !avatar.IsCorpse && avatar.GetGameObject() != null)
                    DisableLocal(player, avatar, baseline);
                baseline.Switching = false;
            }
        }

        private static IEnumerator VrmSetup(Player player, VrmInstance vrmInstance)
        {
            while (vrmInstance.IsLoading && player != null) yield return null;
            if (player == null || vrmInstance.GetGameObject() == null) yield break;
            var settings = vrmInstance.GetSettings();
            var setupClock = Settings.LogLoadTiming ? System.Diagnostics.Stopwatch.StartNew() : null;

            var vrmGo = vrmInstance.GetGameObject();

            if (vrmGo == null)
            {
                Logger.LogError("VrmGo Is Null VrmSetup");
                yield break;
            }

            if (!VanillaStates.TryGetValue(player, out var baseline))
                VanillaStates[player] = baseline = new VanillaState(player);
            vrmGo.SetActive(true);
            player.m_maxInteractDistance = baseline.Interaction * settings.InteractionDistanceScale;

            var rigidBody = player.GetComponent<Rigidbody>();
            var collider = player.GetComponent<CapsuleCollider>();

            if (collider != null)
            {
                collider.height = settings.VrmHeight;
                collider.radius = settings.VrmRadius;
                collider.center = new Vector3(0, settings.VrmHeight / 2, 0);
            }
            else
                Logger.LogError("CapsuleCollider component is missing on the player object.");

            if (rigidBody != null)
            {
                if (collider != null)
                    rigidBody.centerOfMass = collider.center;
                else
                    Logger.LogError("Cannot set Rigidbody centerOfMass because CapsuleCollider is missing.");
            }
            else
            {
                if (!player.IsInStartMenu()) Logger.LogError("Rigidbody component is missing on the player object.");
            }

            yield return null;

            if (player.TryGetField<Player, Animator>("m_animator", out var playerAnimator))
            {
                playerAnimator.keepAnimatorStateOnDisable = true;
                playerAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                yield return null;

                if (vrmGo == null)
                {
                    Logger.LogError("VrmGo Is Null VrmSetup 2");
                    yield break;
                }

                var vrmAnimator = vrmGo.GetComponent<VrmAnimator>();

                if (vrmAnimator == null)
                {
                    vrmAnimator = vrmGo.AddComponent<VrmAnimator>();
                    vrmAnimator.Setup(player, playerAnimator, vrmInstance);
                }
                else
                    vrmAnimator.Setup(player, playerAnimator, vrmInstance);

                yield return null;

                if (settings.FixCameraHeight)
                {
                    var vrmEyeController = player.gameObject.GetComponent<VrmEyeAnimator>();
                    if (vrmEyeController == null)
                        player.gameObject.AddComponent<VrmEyeAnimator>().Setup(player, playerAnimator, vrmInstance);
                    else
                    {
                        vrmEyeController.Setup(player, playerAnimator, vrmInstance);
                        vrmEyeController.enabled = true;
                    }
                }
            }
            else
                Logger.LogError("playerAnimator Not found.");

            if (settings.UseMToonShader && !settings.AttemptTextureFix)
            {
                var vrmMToonControler = vrmGo.GetComponent<VrmMToonFix>();

                if (vrmMToonControler == null)
                    vrmGo.AddComponent<VrmMToonFix>().Setup(vrmGo);
                else
                    vrmMToonControler.Setup(vrmGo);
            }

            yield return null;

            var beforeSprings = setupClock?.Elapsed.TotalMilliseconds ?? 0;
            var springs = vrmGo.GetComponent<SpringSettingsReference>() ??
                vrmGo.AddComponent<SpringSettingsReference>();
            springs.Apply(settings);
            var beforeEquipment = setupClock?.Elapsed.TotalMilliseconds ?? 0;
            // The equipment patch owns hiding the vanilla body as part of this commit.
            if (player.TryGetComponent<VisEquipment>(out var equipment))
                PatchVisEquipmentUpdateLodgroup.RecreateEquipment(equipment);

            var beforeGizmos = setupClock?.Elapsed.TotalMilliseconds ?? 0;
            if (Settings.ShowSocketGizmos)
            {
                if (!player.TryGetComponent<BoneGizmos>(out var boneGizmos))
                    boneGizmos = player.gameObject.AddComponent<BoneGizmos>();
                boneGizmos.Setup(player, vrmInstance, true, true);
                boneGizmos.SetVisible(true);
            }

            if (setupClock != null)
            {
                vrmInstance.SetSetupTiming(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "setup: pre-springs/frames={0:F0}ms, springs={1:F0}ms, equipment={2:F0}ms, gizmos={3:F0}ms",
                    beforeSprings,
                    beforeEquipment - beforeSprings,
                    beforeGizmos - beforeEquipment,
                    setupClock.Elapsed.TotalMilliseconds - beforeGizmos));
            }
        }
    }
}
