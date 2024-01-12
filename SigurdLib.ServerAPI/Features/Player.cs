using GameNetcodeStuff;
using Sigurd.ServerAPI.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace Sigurd.ServerAPI.Features
{
    /// <summary>
    /// Encapsulates a <see cref="PlayerControllerB"/> for earier interacting.
    /// </summary>
    public class Player : NetworkBehaviour
    {
        internal static GameObject PlayerNetworkPrefab { get; set; }

        /// <summary>
        /// Gets a dictionary containing all <see cref="Player"/>s. Even inactive ones. When on a client, this may not contain all inactive players as they will not yet have been linked to a player controller.
        /// </summary>
        public static Dictionary<PlayerControllerB, Player> Dictionary { get; } = new Dictionary<PlayerControllerB, Player>();

        /// <summary>
        /// Gets a list containing all <see cref="Player"/>s. Even inactive ones. When on a client, this may not contain all inactive players as they will not yet have been linked to a player controller.
        /// </summary>
        public static IReadOnlyCollection<Player> List => Dictionary.Values;

        /// <summary>
        /// Gets a list containing only the currently active <see cref="Player"/>s, dead or alive.
        /// </summary>
        /// TODO: `.Where` is bad. Potentially add and remove from this list as needed with a patch.
        public static IReadOnlyCollection<Player> ActiveList => List.Where(p => p.IsActive).ToList();

        /// <summary>
        /// Gets the local <see cref="Player"/>.
        /// </summary>
        public static Player LocalPlayer { get; internal set; }

        /// <summary>
        /// Gets the host <see cref="Player"/>.
        /// </summary>
        public static Player HostPlayer { get; internal set; }

        /// <summary>
        /// Gets the encapsulated <see cref="PlayerControllerB"/>.
        /// </summary>
        public PlayerControllerB PlayerController { get; internal set; }

        /// <summary>
        /// Gets a <see cref="List{T}"/> of <see cref="Tip"/>s that will show to the player.
        /// </summary>
        public List<Tip> TipQueue { get; internal set; } = new List<Tip>();

        internal Tip CurrentTip { get; set; }

        internal int NextTipId = int.MinValue;

        internal ClientRpcParams SendToMeParams { get; set; }

        internal NetworkVariable<ulong> NetworkClientId { get; } = new NetworkVariable<ulong>(ulong.MaxValue);

        /// <summary>
        /// Gets the <see cref="Player"/>'s client id.
        /// </summary>
        public ulong ClientId => PlayerController.actualClientId;

        /// <summary>
        /// Gets the <see cref="Player"/>'s player object id. This should be used when accessing allPlayerScripts, or any other array that's index correlates to a player.
        /// </summary>
        public int PlayerObjectId => StartOfRound.Instance.ClientPlayerList[ClientId];

        /// <summary>
        /// Gets the <see cref="Player"/>'s steam id.
        /// </summary>
        public ulong SteamId => PlayerController.playerSteamId;

        /// <summary>
        /// Gets whether or not the <see cref="Player"/> is the host.
        /// </summary>
        public new bool IsHost => PlayerController.gameObject == PlayerController.playersManager.allPlayerObjects[0];

        /// <summary>
        /// Gets whether or not the <see cref="Player"/> is the current local player.
        /// </summary>
        public new bool IsLocalPlayer => PlayerController == StartOfRound.Instance.localPlayerController;

        /// <summary>
        /// Gets whether or not the <see cref="Player"/> has a connected user.
        /// </summary>
        public bool IsActive => IsControlled || IsDead;

        /// <summary>
        /// Gets whether or not the <see cref="Player"/> is currently being controlled.
        /// Lethal Company creates PlayerControllers ahead of time, so all of them always exist.
        /// </summary>
        public bool IsControlled => PlayerController.isPlayerControlled;

        /// <summary>
        /// Gets whether or not the <see cref="Player"/> is currently dead.
        /// Due to the way the PlayerController works, this is false if there is not an active user connected to the controller.
        /// </summary>
        public bool IsDead => PlayerController.isPlayerDead;

        /// <summary>
        /// Gets or sets the <see cref="Player"/>'s username.
        /// </summary>
        public string Username
        {
            get
            {
                return PlayerController.playerUsername;
            }
            set
            {
                PlayerController.playerUsername = value;
                PlayerController.usernameBillboardText.text = value;
                int index = StartOfRound.Instance.mapScreen.radarTargets.FindIndex(t => t.transform == PlayerController.transform);

                if (index != -1)
                {
                    StartOfRound.Instance.mapScreen.radarTargets[index].name = value;

                    if (StartOfRound.Instance.mapScreen.targetTransformIndex == index)
                        StartOfRound.Instance.mapScreenPlayerName.text = "MONITORING: " + value;
                }

                LocalPlayer.PlayerController.quickMenuManager.playerListSlots[PlayerObjectId].usernameHeader.text = value;

                if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)
                {
                    SetPlayerUsernameClientRpc(value);
                }
                else
                {
                    SetPlayerUsernameServerRpc(value);
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetPlayerUsernameServerRpc(string name, ServerRpcParams @params = default)
        {
            if (@params.Receive.SenderClientId == ClientId)
            {
                SetPlayerUsernameClientRpc(name);
            }
        }

        [ClientRpc]
        private void SetPlayerUsernameClientRpc(string name)
        {
            PlayerController.playerUsername = name;
            PlayerController.usernameBillboardText.text = name;
            int index = StartOfRound.Instance.mapScreen.radarTargets.FindIndex(t => t.transform == PlayerController.transform);

            if (index != -1)
            {
                StartOfRound.Instance.mapScreen.radarTargets[index].name = name;

                if (StartOfRound.Instance.mapScreen.targetTransformIndex == index)
                    StartOfRound.Instance.mapScreenPlayerName.text = "MONITORING: " + name;
            }

            LocalPlayer.PlayerController.quickMenuManager.playerListSlots[PlayerObjectId].usernameHeader.text = name;
        }

        /// <summary>
        /// Gets or sets the <see cref="Player"/>'s sprint meter.
        /// </summary>
        /// <exception cref="NoAuthorityException">Thrown when attempting to set position from the client.</exception>
        public float SprintMeter
        {
            get
            {
                return PlayerController.sprintMeter;
            }
            set
            {
                if (!(NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost))
                {
                    throw new NoAuthorityException("Tried to set sprint meter on client.");
                }

                PlayerController.sprintMeter = value;
                SetSprintMeterClientRpc(value);
            }
        }

        [ClientRpc]
        private void SetSprintMeterClientRpc(float value)
        {
            if (!NetworkManager.Singleton.IsClient) return;

            PlayerController.sprintMeter = value;
        }

        /// <summary>
        /// Gets or sets the <see cref="Player"/>'s position.
        /// If you set a <see cref="Features.Player"/>'s position out of bounds, they will be teleported back to a safe location next to the ship or entrance/exit to a dungeon.
        /// </summary>
        /// <exception cref="NoAuthorityException">Thrown when attempting to set position from the client.</exception>
        public Vector3 Position
        {
            get
            {
                return PlayerController.transform.position;
            }
            set
            {
                if (!(NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost))
                {
                    throw new NoAuthorityException("Tried to set position on client.");
                }

                PlayerController.transform.position = value;
                PlayerController.serverPlayerPosition = value;

                TeleportPlayerClientRpc(value);
            }
        }

        // UpdatePlayerPositionClientRpc doesn't actually set the player's position, so we need a custom rpc to do so.
        [ClientRpc]
        private void TeleportPlayerClientRpc(Vector3 position)
        {
            if (!NetworkManager.Singleton.IsClient) return;

            PlayerController.TeleportPlayer(position);

            bool inShip = PlayerController.playersManager.shipBounds.bounds.Contains(position);

            if (IsLocalPlayer) PlayerController.UpdatePlayerPositionServerRpc(position, inShip, inShip, PlayerController.isExhausted, PlayerController.thisController.isGrounded);
        }

        /// <summary>
        /// Gets or sets the <see cref="Player"/>'s euler angles.
        /// </summary>
        /// <exception cref="NoAuthorityException">Thrown when attempting to update euler angles from a client that isn't the local client, or the host.</exception>
        public Vector3 EulerAngles
        {
            get
            {
                return PlayerController.transform.eulerAngles;
            }
            set
            {
                if (!(IsLocalPlayer || NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer))
                {
                    throw new NoAuthorityException("Tried to update euler angles from other client.");
                }

                PlayerController.transform.eulerAngles = value;

                // Only the local client or the host can set rotation, but if we are the host, we need to sync that to everyone.
                if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
                {
                    PlayerController.UpdatePlayerRotationFullClientRpc(value);
                }
            }
        }

        /// <summary>
        /// Gets or sets the <see cref="Player"/>'s rotation. Quaternions can't gimbal lock, but they are harder to understand.
        /// Use <see cref="Player.EulerAngles"/> if you don't know what you're doing.
        /// </summary>
        /// <exception cref="NoAuthorityException">Thrown when attempting to update rotation from a client that isn't the local client, or the host.</exception>
        public Quaternion Rotation
        {
            get
            {
                return PlayerController.transform.rotation;
            }
            set
            {
                if (!(IsLocalPlayer || NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer))
                {
                    throw new NoAuthorityException("Tried to update rotation from other client.");
                }

                PlayerController.transform.rotation = value;
                PlayerController.transform.eulerAngles = value.eulerAngles;

                // Only the local client or the host can set rotation, but if we are the host, we need to sync that to everyone.
                if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
                {
                    PlayerController.UpdatePlayerRotationFullClientRpc(value.eulerAngles);
                }
            }
        }

        /// <summary>
        /// Gets or sets the <see cref="Player"/>'s health.
        /// </summary>
        /// <exception cref="NoAuthorityException">Thrown when attempting to set health from the client.</exception>
        public int Health
        {
            get
            {
                return PlayerController.health;
            }
            set
            {
                if (!(NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost))
                {
                    throw new NoAuthorityException("Tried to set health on client.");
                }

                PlayerController.health = value;

                SetHealthClientRpc(value);
            }
        }

        [ClientRpc]
        private void SetHealthClientRpc(int health)
        {
            if (!NetworkManager.Singleton.IsClient) return;

            int oldHealth = PlayerController.health;

            PlayerController.health = health;

            if (PlayerController.IsOwner) HUDManager.Instance.UpdateHealthUI(health, health < oldHealth);

            if (health <= 0 && !PlayerController.isPlayerDead && PlayerController.AllowPlayerDeath())
            {
                PlayerController.KillPlayer(default, true, CauseOfDeath.Unknown, 0);
            }
        }

        /// <summary>
        /// Gets whether or not the <see cref="Player"/> is in the factory.
        /// </summary>
        public bool IsInFactory => PlayerController.isInsideFactory;

        /// <summary>
        /// Gets the <see cref="Player"/>'s currently held item.
        /// </summary>
        public Item HeldItem
        {
            get
            {
                if (PlayerController.currentlyHeldObjectServer == null) return null;

                return Item.Dictionary[PlayerController.currentlyHeldObjectServer];
            }
        }

        /// <summary>
        /// Gets whether or not the <see cref="Player"/> is holding an item.
        /// </summary>
        public bool IsHoldingItem => HeldItem != null;

        /// <summary>
        /// Gets whether or not the <see cref="Player"/> has free hands, meaning their currently held item is not two handed.
        /// </summary>
        public bool HasFreeHands => !IsHoldingItem || !HeldItem.IsTwoHanded;

        /// <summary>
        /// Gets the <see cref="Player"/>'s <see cref="PlayerInventory"/>.
        /// </summary>
        public PlayerInventory Inventory { get; private set; }

        /// <summary>
        /// Gets or sets the <see cref="Player"/>'s carry weight.
        /// </summary>
        public float CarryWeight
        {
            get
            {
                return PlayerController.carryWeight;
            }
            set
            {
                PlayerController.carryWeight = value;
            }
        }

        /// <summary>
        /// Hurts the <see cref="Player"/>.
        /// </summary>
        /// <param name="damage">The amount of health to take from the <see cref="Player"/>.</param>
        /// <param name="causeOfDeath">The cause of death to show on the end screen.</param>
        /// <param name="bodyVelocity">he velocity to launch the ragdoll at, if killed.</param>
        /// <param name="overrideOneShotProtection">Whether or not to override one shot protection.</param>
        /// <param name="deathAnimation">Which death animation to use.</param>
        /// <param name="fallDamage">Whether or not this should be considered fall damage.</param>
        /// <param name="hasSFX">Whether or not this damage has sfx.</param>
        /// <exception cref="NoAuthorityException">Thrown when attempting to hurt a <see cref="Player"/> that isn't the local <see cref="Player"/>'s, if not the host.</exception>
        public void Hurt(int damage, CauseOfDeath causeOfDeath = CauseOfDeath.Unknown, Vector3 bodyVelocity = default, bool overrideOneShotProtection = false, int deathAnimation = 0, bool fallDamage = false, bool hasSFX = true)
        {
            if (overrideOneShotProtection && Health - damage <= 0)
            {
                Kill(bodyVelocity, true, causeOfDeath, deathAnimation);
                return;
            }

            if (IsLocalPlayer)
            {
                PlayerController.DamagePlayer(damage, hasSFX, true, causeOfDeath, deathAnimation, fallDamage, bodyVelocity);
            }
            else
            {
                if (!(NetworkManager.IsServer || NetworkManager.IsHost))
                {
                    throw new NoAuthorityException("Tried to kill player from other client.");
                }

                PlayerController.DamagePlayerClientRpc(damage, Health - damage);
            }
        }

        /// <summary>
        /// Kills the <see cref="Player"/>.
        /// </summary>
        /// <param name="bodyVelocity">The velocity to launch the ragdoll at, if spawned.</param>
        /// <param name="spawnBody">Whether or not to spawn a ragdoll.</param>
        /// <param name="causeOfDeath">The cause of death to show on the end screen.</param>
        /// <param name="deathAnimation">Which death animation to use.</param>
        /// <exception cref="NoAuthorityException">Thrown when attempting to kill a <see cref="Player"/> that isn't the local <see cref="Player"/>'s, if not the host.</exception>
        public void Kill(Vector3 bodyVelocity = default, bool spawnBody = true, CauseOfDeath causeOfDeath = CauseOfDeath.Unknown, int deathAnimation = 0)
        {
            if (IsLocalPlayer)
            {
                PlayerController.KillPlayer(bodyVelocity, spawnBody, causeOfDeath, deathAnimation);
            }
            else
            {
                if (!(NetworkManager.IsServer || NetworkManager.IsHost))
                {
                    throw new NoAuthorityException("Tried to kill player from other client.");
                }

                PlayerController.KillPlayerClientRpc((int)ClientId, spawnBody, bodyVelocity, (int)causeOfDeath, deathAnimation);
            }
        }

        /// <summary>
        /// Sets the <see cref="Player"/>'s username locally and doesn't attempt to network it.
        /// </summary>
        /// <param name="name">The new name.</param>
        public void SetPlayerUsernameLocally(string name)
        {
            PlayerController.playerUsername = name;
            PlayerController.usernameBillboardText.text = name;
        }

        /// <summary>
        /// Queues a <see cref="Tip"/> to show to the <see cref="Player"/>.
        /// </summary>
        /// <param name="header">The <see cref="Tip"/>'s header</param>
        /// <param name="message">The <see cref="Tip"/>'s message.</param>
        /// <param name="duration">The <see cref="Tip"/>'s duration.</param>
        /// <param name="priority">The priority of the <see cref="Tip"/>. Higher means will show sooner. Goes to the end of the priority list.</param>
        /// <param name="isWarning">Whether or not this <see cref="Tip"/> is a warning.</param>
        /// <param name="useSave">Whether or not to save <see langword="true"/> to the <paramref name="prefsKey"/>. Useful for showing one time only tips.</param>
        /// <param name="prefsKey">The key to save as when <paramref name="useSave"/> is set to <see langword="true" /></param>
        public void QueueTip(string header, string message, float duration = 5f, int priority = 0, bool isWarning = false, bool useSave = false, string prefsKey = "LC_Tip1")
        {
            if (!IsLocalPlayer)
            {
                if (!(NetworkManager.IsServer || NetworkManager.IsHost))
                {
                    throw new NoAuthorityException("Tried to show tips to other clients from client.");
                }

                QueueTipClientRpc(header, message, duration, priority, isWarning, useSave, prefsKey, SendToMeParams);

                return;
            }

            QueueTipInternal(header, message, duration, priority, isWarning, useSave, prefsKey);
        }

        [ClientRpc]
        private void QueueTipClientRpc(string header, string message, float duration, int priority, bool isWarning, bool useSave, string prefsKey, ClientRpcParams clientRpcParams = default)
        {
            QueueTipInternal(header, message, duration, priority, isWarning, useSave, prefsKey);
        }

        internal void QueueTipInternal(string header, string message, float duration, int priority, bool isWarning, bool useSave, string prefsKey)
        {
            Tip tip = new Tip(header, message, duration, priority, isWarning, useSave, prefsKey, NextTipId++);

            if (TipQueue.Count == 0)
            {
                TipQueue.Add(tip);
                return;
            }

            if (TipQueue[TipQueue.Count - 1].CompareTo(tip) <= 0)
            {
                TipQueue.Add(tip);
                return;
            }

            if (TipQueue[0].CompareTo(tip) >= 0)
            {
                TipQueue.Insert(0, tip);
                return;
            }

            int index = TipQueue.BinarySearch(tip);

            if (index < 0) index = ~index;

            TipQueue.Insert(index, tip);
        }

        /// <summary>
        /// Shows a <see cref="Tip"/> to the <see cref="Player"/>, bypassing the queue.
        /// </summary>
        /// <param name="header">The <see cref="Tip"/>'s header</param>
        /// <param name="message">The <see cref="Tip"/>'s message.</param>
        /// <param name="duration">The <see cref="Tip"/>'s duration.</param>
        /// <param name="isWarning">Whether or not this <see cref="Tip"/> is a warning.</param>
        /// <param name="useSave">Whether or not to save <see langword="true"/> to the <paramref name="prefsKey"/>. Useful for showing one time only tips.</param>
        /// <param name="prefsKey">The key to save as when <paramref name="useSave"/> is set to <see langword="true" /></param>
        public void ShowTip(string header, string message, float duration = 5f, bool isWarning = false, bool useSave = false, string prefsKey = "LC_Tip1")
        {
            if (!IsLocalPlayer)
            {
                if (!(NetworkManager.IsServer || NetworkManager.IsHost))
                {
                    throw new NoAuthorityException("Tried to show tips to other clients from client.");
                }

                ShowTipClientRpc(header, message, duration, isWarning, useSave, prefsKey, SendToMeParams);

                return;
            }

            ShowTipInternal(header, message, duration, isWarning, useSave, prefsKey);
        }

        [ClientRpc]
        private void ShowTipClientRpc(string header, string message, float duration, bool isWarning, bool useSave, string prefsKey, ClientRpcParams clientRpcParams = default)
        {
            ShowTipInternal(header, message, duration, isWarning, useSave, prefsKey);
        }

        private void ShowTipInternal(string header, string message, float duration, bool isWarning, bool useSave, string prefsKey)
        {
            Tip tip = new Tip(header, message, duration, int.MaxValue, isWarning, useSave, prefsKey, NextTipId++);

            // if there is a tip with >= 1.5 seconds left, queue it back up
            if (CurrentTip != null && CurrentTip.TimeLeft >= 1.5f)
            {
                // Ensures the current tip will continue afterwards
                CurrentTip.TipId = int.MinValue;
                TipQueue.Insert(0, CurrentTip);
            }

            CurrentTip = tip;

            HUDManager.Instance.tipsPanelAnimator.speed = 1;
            HUDManager.Instance.tipsPanelAnimator.ResetTrigger("TriggerHint");

            DisplayTip(CurrentTip.Header, CurrentTip.Message, isWarning, useSave, prefsKey);
        }

        internal static void DisplayTip(string headerText, string bodyText, bool isWarning = false, bool useSave = false, string prefsKey = "LC_Tip1")
        {
            if (!HUDManager.Instance.CanTipDisplay(isWarning, useSave, prefsKey))
            {
                return;
            }
            if (useSave)
            {
                if (HUDManager.Instance.tipsPanelCoroutine != null)
                {
                    HUDManager.Instance.StopCoroutine(HUDManager.Instance.tipsPanelCoroutine);
                }
                HUDManager.Instance.tipsPanelCoroutine = HUDManager.Instance.StartCoroutine(HUDManager.Instance.TipsPanelTimer(prefsKey));
            }
            HUDManager.Instance.tipsPanelHeader.text = headerText;
            HUDManager.Instance.tipsPanelBody.text = bodyText;
            if (isWarning)
            {
                HUDManager.Instance.tipsPanelAnimator.SetTrigger("TriggerWarning");
                RoundManager.PlayRandomClip(HUDManager.Instance.UIAudio, HUDManager.Instance.warningSFX, false, 1f, 0);
                return;
            }
            HUDManager.Instance.tipsPanelAnimator.SetTrigger("TriggerHint");
            RoundManager.PlayRandomClip(HUDManager.Instance.UIAudio, HUDManager.Instance.tipsSFX, false, 1f, 0);
        }

        #region Unity related things
        private void Start()
        {
            if (!(NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost))
            {
                PlayerController = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(c => c.actualClientId == NetworkClientId.Value);
            }

            if (PlayerController != null)
            {
                if (IsLocalPlayer) LocalPlayer = this;
                if (IsHost) HostPlayer = this;

                if (!Dictionary.ContainsKey(PlayerController)) Dictionary.Add(PlayerController, this);

                SendToMeParams = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new ulong[] { ClientId }
                    }
                };
            }

            Inventory = GetComponent<PlayerInventory>();

            NetworkClientId.OnValueChanged += clientIdChanged;
        }

        private void Update()
        {
            if (!IsLocalPlayer) return;

            if (CurrentTip != null)
            {
                // Prevent the panel from automatically disappearing after 5 seconds
                if (HUDManager.Instance.tipsPanelAnimator.speed > 0 &&
                    HUDManager.Instance.tipsPanelAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.9f)
                {
                    HUDManager.Instance.tipsPanelAnimator.speed = 0;
                }

                CurrentTip.TimeLeft -= Time.deltaTime;

                if (CurrentTip.TimeLeft <= 0)
                {
                    HUDManager.Instance.tipsPanelAnimator.speed = 1;
                    HUDManager.Instance.tipsPanelAnimator.ResetTrigger("TriggerHint");
                    CurrentTip = null;
                }
            }

            if (CurrentTip == null && TipQueue.Count > 0)
            {
                CurrentTip = TipQueue[0];
                TipQueue.RemoveAt(0);

                DisplayTip(CurrentTip.Header, CurrentTip.Message, CurrentTip.IsWarning, CurrentTip.UseSave, CurrentTip.PreferenceKey);
            }
        }

        /// <summary>
        /// For internal use only. Do not use.
        /// </summary>
        public override void OnDestroy()
        {
            NetworkClientId.OnValueChanged -= clientIdChanged;
            base.OnDestroy();
        }
        #endregion

        #region Network variable handlers
        private void clientIdChanged(ulong oldId, ulong newId)
        {
            PlayerController = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(c => c.actualClientId == newId);

            if (PlayerController != null)
            {
                if (IsLocalPlayer) LocalPlayer = this;
                if (IsHost) HostPlayer = this;

                if (!Dictionary.ContainsKey(PlayerController)) Dictionary.Add(PlayerController, this);

                SendToMeParams = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new ulong[] { ClientId }
                    }
                };
            }
        }
        #endregion

        #region Player getters
        /// <summary>
        /// Gets or adds a <see cref="Features.Player"/> from a <see cref="PlayerControllerB"/>.
        /// </summary>
        /// <param name="playerController">The player's <see cref="PlayerControllerB"/>.</param>
        /// <returns>A <see cref="Player"/>.</returns>
        public static Player GetOrAdd(PlayerControllerB playerController)
        {
            if (playerController == null) return null;

            if (Dictionary.TryGetValue(playerController, out Player player)) return player;

            foreach (Player p in FindObjectsOfType<Player>())
            {
                if (p.NetworkClientId.Value == playerController.actualClientId)
                {
                    Dictionary.Add(playerController, p);
                    return p;
                }
            }

            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                GameObject go = Instantiate(PlayerNetworkPrefab, Vector3.zero, default);
                go.SetActive(true);
                Player p = go.GetComponent<Player>();
                p.PlayerController = playerController;
                go.GetComponent<NetworkObject>().Spawn(false);

                return p;
            }

            return null;
        }

        /// <summary>
        /// Gets a <see cref="Features.Player"/> from a <see cref="PlayerControllerB"/>.
        /// </summary>
        /// <param name="playerController">The player's <see cref="PlayerControllerB"/>.</param>
        /// <returns>A <see cref="Player"/> or <see langword="null"/> if not found.</returns>
        public static Player Get(PlayerControllerB playerController)
        {
            if (Dictionary.TryGetValue(playerController, out Player player)) return player;

            return null;
        }

        /// <summary>
        /// Tries to get a <see cref="Features.Player"/> from a <see cref="PlayerControllerB"/>.
        /// </summary>
        /// <param name="playerController">The player's <see cref="PlayerControllerB"/>.</param>
        /// <param name="player">Outputs a <see cref="Player"/>, or <see langword="null"/> if not found.</param>
        /// <returns><see langword="true"/> if a <see cref="Features.Player"/> is found, <see langword="false"/> otherwise.</returns>
        public static bool TryGet(PlayerControllerB playerController, out Player player)
        {
            return Dictionary.TryGetValue(playerController, out player);
        }

        /// <summary>
        /// Gets a <see cref="Features.Player"/> from a <see cref="Features.Player"/>'s client id.
        /// </summary>
        /// <param name="clientId">The player's client id.</param>
        /// <returns>A <see cref="Player"/> or <see langword="null"/> if not found.</returns>
        public static Player Get(ulong clientId)
        {
            return List.FirstOrDefault(p => p.ClientId == clientId);
        }

        /// <summary>
        /// Tries to get a <see cref="Features.Player"/> from a <see cref="Features.Player"/>'s client id.
        /// </summary>
        /// <param name="clientId">The player's client id.</param>
        /// <param name="player">Outputs a <see cref="Player"/>, or <see langword="null"/> if not found.</param>
        /// <returns><see langword="true"/> if a <see cref="Features.Player"/> is found, <see langword="false"/> otherwise.</returns>
        public static bool TryGet(ulong clientId, out Player player)
        {
            return (player = Get(clientId)) != null;
        }
        #endregion

        #region Event replication
        internal void CallHurtingOnOtherClients(int damage, bool hasSFX, CauseOfDeath causeOfDeath,
            int deathAnimation, bool fallDamage, Vector3 force)
        {
            CallHurtingOnOtherClientsServerRpc(damage, hasSFX, (int)causeOfDeath, deathAnimation, fallDamage, force);
        }

        [ServerRpc(RequireOwnership = false)]
        private void CallHurtingOnOtherClientsServerRpc(int damage, bool hasSFX, int causeOfDeath,
            int deathAnimation, bool fallDamage, Vector3 force, ServerRpcParams serverRpcParams = default)
        {
            if (serverRpcParams.Receive.SenderClientId != ClientId) return;

            CallHurtingOnOtherClientsClientRpc(damage, hasSFX, causeOfDeath, deathAnimation, fallDamage, force);
        }

        [ClientRpc]
        private void CallHurtingOnOtherClientsClientRpc(int damage, bool hasSFX, int causeOfDeath,
            int deathAnimation, bool fallDamage, Vector3 force)
        {
            if (IsLocalPlayer) return;

            Events.Handlers.Player.OnHurting(new Events.EventArgs.Player.HurtingEventArgs(this, damage, hasSFX,
                (CauseOfDeath)causeOfDeath, deathAnimation, fallDamage, force));
        }

        internal void CallHurtOnOtherClients(int damage, bool hasSFX, CauseOfDeath causeOfDeath,
            int deathAnimation, bool fallDamage, Vector3 force)
        {
            CallHurtOnOtherClientsServerRpc(damage, hasSFX, (int)causeOfDeath, deathAnimation, fallDamage, force);
        }

        [ServerRpc(RequireOwnership = false)]
        private void CallHurtOnOtherClientsServerRpc(int damage, bool hasSFX, int causeOfDeath,
            int deathAnimation, bool fallDamage, Vector3 force, ServerRpcParams serverRpcParams = default)
        {
            if (serverRpcParams.Receive.SenderClientId != ClientId) return;

            CallHurtOnOtherClientsClientRpc(damage, hasSFX, causeOfDeath, deathAnimation, fallDamage, force);
        }

        [ClientRpc]
        private void CallHurtOnOtherClientsClientRpc(int damage, bool hasSFX, int causeOfDeath,
            int deathAnimation, bool fallDamage, Vector3 force)
        {
            if (IsLocalPlayer) return;

            Events.Handlers.Player.OnHurt(new Events.EventArgs.Player.HurtEventArgs(this, damage, hasSFX,
                (CauseOfDeath)causeOfDeath, deathAnimation, fallDamage, force));
        }

        internal void CallDroppingItemOnOtherClients(Item item, bool placeObject, Vector3 targetPosition,
            int floorYRotation, NetworkObject parentObjectTo, bool matchRotationOfParent, bool droppedInShip)
        {
            CallDroppingItemOnOtherClientsServerRpc(item.NetworkObjectId, placeObject, targetPosition, floorYRotation, parentObjectTo != null, parentObjectTo != null ? parentObjectTo.NetworkObjectId : 0, matchRotationOfParent, droppedInShip);
        }

        [ServerRpc(RequireOwnership = false)]
        private void CallDroppingItemOnOtherClientsServerRpc(ulong itemNetworkId, bool placeObject, Vector3 targetPosition,
            int floorYRotation, bool hasParent, ulong parentObjectToId, bool matchRotationOfParent, bool droppedInShip,
            ServerRpcParams serverRpcParams = default)
        {
            if (serverRpcParams.Receive.SenderClientId != ClientId) return;

            CallDroppingItemOnOtherClientsClientRpc(itemNetworkId, placeObject, targetPosition, floorYRotation, hasParent, parentObjectToId, matchRotationOfParent, droppedInShip);
        }

        [ClientRpc]
        private void CallDroppingItemOnOtherClientsClientRpc(ulong itemNetworkId, bool placeObject, Vector3 targetPosition,
            int floorYRotation, bool hasParent, ulong parentObjectToId, bool matchRotationOfParent, bool droppedInShip)
        {
            if (IsLocalPlayer) return;

            Events.Handlers.Player.OnDroppingItem(new Events.EventArgs.Player.DroppingItemEventArgs(this, Item.Get(itemNetworkId), placeObject, targetPosition, floorYRotation, hasParent ? NetworkManager.Singleton.SpawnManager.SpawnedObjects[parentObjectToId] : null, matchRotationOfParent, droppedInShip));
        }

        internal void CallDroppedItemOnOtherClients(Item item, bool placeObject, Vector3 targetPosition,
            int floorYRotation, NetworkObject parentObjectTo, bool matchRotationOfParent, bool droppedInShip)
        {
            CallDroppedItemOnOtherClientsServerRpc(item.NetworkObjectId, placeObject, targetPosition, floorYRotation, parentObjectTo != null, parentObjectTo != null ? parentObjectTo.NetworkObjectId : 0, matchRotationOfParent, droppedInShip);
        }

        [ServerRpc(RequireOwnership = false)]
        private void CallDroppedItemOnOtherClientsServerRpc(ulong itemNetworkId, bool placeObject, Vector3 targetPosition,
            int floorYRotation, bool hasParent, ulong parentObjectToId, bool matchRotationOfParent, bool droppedInShip,
            ServerRpcParams serverRpcParams = default)
        {
            if (serverRpcParams.Receive.SenderClientId != ClientId) return;

            CallDroppedItemOnOtherClientsClientRpc(itemNetworkId, placeObject, targetPosition, floorYRotation, hasParent, parentObjectToId, matchRotationOfParent, droppedInShip);
        }

        [ClientRpc]
        private void CallDroppedItemOnOtherClientsClientRpc(ulong itemNetworkId, bool placeObject, Vector3 targetPosition,
            int floorYRotation, bool hasParent, ulong parentObjectToId, bool matchRotationOfParent, bool droppedInShip)
        {
            if (IsLocalPlayer) return;

#pragma warning disable CS8604 // Possible null reference argument for parameter.
            Events.Handlers.Player.OnDroppedItem(new Events.EventArgs.Player.DroppedItemEventArgs(this, Item.Get(itemNetworkId), placeObject, targetPosition, floorYRotation, hasParent ? NetworkManager.Singleton.SpawnManager.SpawnedObjects[parentObjectToId] : null, matchRotationOfParent, droppedInShip));
#pragma warning restore
        }
        #endregion

        /// <summary>
        /// Encapsulates a <see cref="Player"/>'s inventory to provide useful tools to it.
        /// </summary>
        public class PlayerInventory : NetworkBehaviour
        {
            /// <summary>
            /// Gets the <see cref="Player"/> that this <see cref="PlayerInventory"/> belongs to.
            /// </summary>
            public Player Player { get; private set; }

            /// <summary>
            /// Gets the <see cref="Player"/>'s items in order.
            /// </summary>
            /// TODO: I'm not sure how feasible it is to get this to work in any other way, but I hate this.
            public Item[] Items => Player.PlayerController.ItemSlots.Select(i => i != null ? Item.Dictionary[i] : null).ToArray();

            /// <summary>
            /// Gets the <see cref="Player"/>'s current item slot.
            /// </summary>
            public int CurrentSlot
            {
                get
                {
                    return Player.PlayerController.currentItemSlot;
                }
                set
                {
                    if (Player.IsLocalPlayer) SetSlotServerRpc(value);
                    else if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer) SetSlotClientRpc(value);
                }
            }

            [ServerRpc(RequireOwnership = false)]
            private void SetSlotServerRpc(int slot, ServerRpcParams serverRpcParams = default)
            {
                if (serverRpcParams.Receive.SenderClientId != Player.ClientId) return;

                SetSlotClientRpc(slot);
            }

            [ClientRpc]
            private void SetSlotClientRpc(int slot)
            {
                Player.PlayerController.SwitchToItemSlot(slot);
            }

            /// <summary>
            /// Gets the first empty item slot, or -1 if there are none available.
            /// </summary>
            /// <returns></returns>
            public int GetFirstEmptySlot()
            {
                return Player.PlayerController.FirstEmptyItemSlot();
            }

            /// <summary>
            /// Tries to get the first empty item slot.
            /// </summary>
            /// <param name="slot">Outputs the empty item slot.</param>
            /// <returns><see langword="true"/> if there's an available slot, <see langword="false"/> otherwise.</returns>
            public bool TryGetFirstEmptySlot(out int slot)
            {
                slot = Player.PlayerController.FirstEmptyItemSlot();
                return slot != -1;
            }

            /// <summary>
            /// Tries to add an <see cref="Item"/> to the inventory in the first available slot.
            /// </summary>
            /// <param name="item">The item to try to add.</param>
            /// <param name="switchTo">Whether or not to switch to this item after adding.</param>
            /// <returns><see langword="true"/> if added <see langword="false"/> otherwise.</returns>
            /// <exception cref="NoAuthorityException">Thrown when trying to add item from the client.</exception>
            public bool TryAddItem(Item item, bool switchTo = true)
            {
                if (!(NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost))
                {
                    throw new NoAuthorityException("Tried to add item from client.");
                }

                if (TryGetFirstEmptySlot(out int slot))
                {
                    if (item.IsTwoHanded && !Player.HasFreeHands)
                    {
                        return false;
                    }

                    if (item.IsHeld)
                    {
                        item.RemoveFromHolder();
                    }

                    item.NetworkObject.ChangeOwnership(Player.ClientId);

                    if (item.IsTwoHanded)
                    {
                        SetSlotAndItemClientRpc(slot, item.NetworkObjectId);
                    }
                    else
                    {
                        if (switchTo && Player.HasFreeHands)
                        {
                            SetSlotAndItemClientRpc(slot, item.NetworkObjectId);
                        }
                        else
                        {
                            if (Player.PlayerController.currentItemSlot == slot)
                                SetSlotAndItemClientRpc(slot, item.NetworkObjectId);
                            else
                                SetItemInSlotClientRpc(slot, item.NetworkObjectId);
                        }
                    }

                    return true;
                }

                return false;
            }

            /// <summary>
            /// Tries to add an <see cref="Item"/> to the inventory in a specific slot.
            /// </summary>
            /// <param name="item">The item to try to add.</param>
            /// <param name="slot">The slot to try to add the item to.</param>
            /// <param name="switchTo">Whether or not to switch to this item after adding.</param>
            /// <returns><see langword="true"/> if added <see langword="false"/> otherwise.</returns>
            /// <exception cref="NoAuthorityException">Thrown when trying to add item from the client.</exception>
            public bool TryAddItemToSlot(Item item, int slot, bool switchTo = true)
            {
                if (!(NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost))
                {
                    throw new NoAuthorityException("Tried to add item from client.");
                }

                if (slot < Player.PlayerController.ItemSlots.Length && Player.PlayerController.ItemSlots[slot] == null)
                {
                    if (item.IsTwoHanded && !Player.HasFreeHands)
                    {
                        return false;
                    }

                    if (item.IsHeld)
                    {
                        item.RemoveFromHolder();
                    }

                    item.NetworkObject.ChangeOwnership(Player.ClientId);

                    if (item.IsTwoHanded)
                    {
                        SetSlotAndItemClientRpc(slot, item.NetworkObjectId);
                    }
                    else
                    {
                        if (switchTo && Player.HasFreeHands)
                        {
                            SetSlotAndItemClientRpc(slot, item.NetworkObjectId);
                        }
                        else
                        {
                            if (Player.PlayerController.currentItemSlot == slot)
                                SetSlotAndItemClientRpc(slot, item.NetworkObjectId);
                            else
                                SetItemInSlotClientRpc(slot, item.NetworkObjectId);
                        }
                    }

                    return true;
                }

                return false;
            }

            [ClientRpc]
            private void SetItemInSlotClientRpc(int slot, ulong itemId)
            {
                Item item = Item.List.FirstOrDefault(i => i.NetworkObjectId == itemId);

                if (item != null)
                {
                    if (Player.IsLocalPlayer)
                    {
                        HUDManager.Instance.itemSlotIcons[slot].sprite = item.ItemProperties.itemIcon;
                        HUDManager.Instance.itemSlotIcons[slot].enabled = true;
                    }

                    item.GrabbableObject.EnablePhysics(false);
                    item.GrabbableObject.EnableItemMeshes(false);
                    item.GrabbableObject.playerHeldBy = Player.PlayerController;
                    item.GrabbableObject.hasHitGround = false;
                    item.GrabbableObject.isInFactory = Player.IsInFactory;

                    Player.CarryWeight += Mathf.Clamp(item.ItemProperties.weight - 1f, 0f, 10f);

                    if (!Player.IsLocalPlayer)
                    {
                        item.GrabbableObject.parentObject = Player.PlayerController.serverItemHolder;
                    }
                    else
                    {
                        item.GrabbableObject.parentObject = Player.PlayerController.localItemHolder;
                    }

                    Player.PlayerController.ItemSlots[slot] = item.GrabbableObject;
                }
            }

            [ClientRpc]
            private void SetSlotAndItemClientRpc(int slot, ulong itemId)
            {
                Item item = Item.List.FirstOrDefault(i => i.NetworkObjectId == itemId);

                if (item != null)
                {
                    Player.PlayerController.SwitchToItemSlot(slot, item.GrabbableObject);

                    item.GrabbableObject.EnablePhysics(false);
                    item.GrabbableObject.isHeld = true;
                    item.GrabbableObject.hasHitGround = false;
                    item.GrabbableObject.isInFactory = Player.IsInFactory;

                    Player.PlayerController.twoHanded = item.ItemProperties.twoHanded;
                    Player.PlayerController.twoHandedAnimation = item.ItemProperties.twoHandedAnimation;
                    Player.PlayerController.isHoldingObject = true;
                    Player.CarryWeight += Mathf.Clamp(item.ItemProperties.weight - 1f, 0f, 10f);

                    if (!Player.IsLocalPlayer)
                    {
                        item.GrabbableObject.parentObject = Player.PlayerController.serverItemHolder;
                    }
                    else
                    {
                        item.GrabbableObject.parentObject = Player.PlayerController.localItemHolder;
                    }
                }
            }

            /// <summary>
            /// Removes an <see cref="Item"/> from the <see cref="Player"/>'s inventory at the current slot.
            /// </summary>
            /// <param name="slot"></param>
            public void RemoveItem(int slot)
            {
                if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer) RemoveItemClientRpc(slot);
                else RemoveItemServerRpc(slot);
            }

            [ServerRpc(RequireOwnership = false)]
            private void RemoveItemServerRpc(int slot, ServerRpcParams serverRpcParams = default)
            {
                if (serverRpcParams.Receive.SenderClientId != Player.ClientId) return;

                RemoveItemClientRpc(slot);
            }

            [ClientRpc]
            private void RemoveItemClientRpc(int slot)
            {
                if (slot != -1)
                {
                    bool currentlyHeldOut = slot == Player.Inventory.CurrentSlot;
                    Item item = Items[slot];

                    if (item == null) return;

                    GrabbableObject grabbable = item.GrabbableObject;

                    if (Player.IsLocalPlayer)
                    {
                        HUDManager.Instance.itemSlotIcons[slot].enabled = false;

                        if (item.IsTwoHanded) HUDManager.Instance.holdingTwoHandedItem.enabled = false;
                    }

                    if (currentlyHeldOut)
                    {
                        if (Player.IsLocalPlayer)
                        {
                            grabbable.DiscardItemOnClient();
                        }
                        else
                        {
                            grabbable.DiscardItem();
                        }

                        Player.PlayerController.currentlyHeldObject = null;
                        Player.PlayerController.currentlyHeldObjectServer = null;
                        Player.PlayerController.isHoldingObject = false;

                        if (item.IsTwoHanded)
                        {
                            Player.PlayerController.twoHanded = false;
                            Player.PlayerController.twoHandedAnimation = false;
                        }
                    }

                    grabbable.heldByPlayerOnServer = false;
                    grabbable.parentObject = null;
                    item.EnablePhysics(false);
                    item.EnableMeshes(true);
                    item.Scale = item.GrabbableObject.originalScale;

                    if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
                    {
                        item.Position = Vector3.zero;
                    }

                    grabbable.isHeld = false;
                    grabbable.isPocketed = false;

                    Player.CarryWeight -= Mathf.Clamp(item.ItemProperties.weight - 1f, 0f, 10f);

                    Player.PlayerController.ItemSlots[slot] = null;
                }
            }

            /// <summary>
            /// Removes an <see cref="Item"/> from the inventory. This should be called on all clients from a client rpc.
            /// </summary>
            /// <param name="item">The <see cref="Item"/> to remove.</param>
            public void RemoveItem(Item item)
            {
                RemoveItem(Array.IndexOf(Player.PlayerController.ItemSlots, item.GrabbableObject));
            }

            /// <summary>
            /// Removes all <see cref="Item"/>s from the <see cref="Player"/>'s inventory.
            /// </summary>
            public void RemoveAllItems()
            {
                for (int i = 0; i < Player.PlayerController.ItemSlots.Length; i++)
                {
                    RemoveItem(i);
                }
            }

            private void Awake()
            {
                Player = GetComponent<Player>();
            }
        }
    }
}
