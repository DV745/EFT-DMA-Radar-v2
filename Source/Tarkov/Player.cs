﻿using eft_dma_radar.Source.Tarkov;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Text.RegularExpressions;

namespace eft_dma_radar
{
    /// <summary>
    /// Class containing Game Player Data.
    /// </summary>
    public class Player
    {
        private static Dictionary<string, int> _groups = new(StringComparer.OrdinalIgnoreCase);
        private GearManager _gearManager;

        #region PlayerProperties
        /// <summary>
        /// Player is a PMC Operator.
        /// </summary>
        public bool IsPMC { get; set; }
        /// <summary>
        /// Player is a Local PMC Operator.
        /// </summary>
        public bool IsLocalPlayer { get; set; }
        /// <summary>
        /// Player is Alive/Not Dead.
        /// </summary>
        public volatile bool IsAlive = true;
        /// <summary>
        /// Player is Active (has not exfil'd).
        /// </summary>
        public volatile bool IsActive = true;
        /// <summary>
        /// Account UUID for Human Controlled Players.
        /// </summary>
        public string AccountID { get; set; }
        public string ProfileID { get; set; }
        /// <summary>
        /// Player name.
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// Player Level (Based on experience).
        /// </summary>
        public int Level { get; private set; } = 0;
        /// <summary>
        /// Player's Kill/Death Average
        /// </summary>
        public float KDA { get; private set; } = -1f;
        /// <summary>
        /// Player's total hours
        /// </summary>
        public float Hours { get; private set; } = -1f;
        /// <summary>
        /// Group that the player belongs to.
        /// </summary>
        public int GroupID { get; set; } = -1;
        /// <summary>
        /// Type of player unit.
        /// </summary>
        public PlayerType Type { get; set; }
        /// <summary>
        /// Player's current health (sum of all 7 body parts).
        /// </summary>
        public int Health { get; private set; } = -1;

        public ulong HealthController { get; set; }

        public ulong InventoryController { get; set; }

        public ulong InventorySlots { get; set; }

        public ulong PlayerBody { get; set; }

        /// <summary>
        /// Player's Unity Position in Local Game World.
        /// </summary>
        public Vector3 Position
        {
            get => this.Bones.TryGetValue(PlayerBones.HumanHead, out var bone)
                   ? bone.Position
                   : Vector3.Zero;
        }
        /// <summary>
        /// Cached 'Zoomed Position' on the Radar GUI. Used for mouseover events.
        /// </summary>
        public Vector2 ZoomedPosition { get; set; } = new();
        /// <summary>
        /// Player's Rotation (direction/pitch) in Local Game World.
        /// 90 degree offset ~already~ applied to account for 2D-Map orientation.
        /// </summary>
        public Vector2 Rotation { get; private set; } = new Vector2(0, 0); // 64 bits will be atomic
        /// <summary>
        /// Key = Slot Name, Value = Item 'Long Name' in Slot
        /// </summary>
        public List<GearManager.Gear> Gear
        {
            get => this._gearManager is not null ? this._gearManager.GearItems : null;
            set
            {
                this._gearManager.GearItems = value;
            }
        }

        public GearManager GearManager => this._gearManager;
        /// <summary>
        /// If 'true', Player object is no longer in the RegisteredPlayers list.
        /// Will be checked if dead/exfil'd on next loop.
        /// </summary>
        public bool LastUpdate { get; set; } = false;
        /// <summary>
        /// Consecutive number of errors that this Player object has 'errored out' while updating.
        /// </summary>
        public int ErrorCount { get; set; } = 0;
        public bool isOfflinePlayer { get; set; } = false;
        public int PlayerSide { get; set; }
        public int PlayerRole { get; set; }
        public bool HasRequiredGear { get; set; } = false;

        private readonly ConcurrentDictionary<PlayerBones, Bone> _bones = new();

        public ConcurrentDictionary<PlayerBones, Bone> Bones
        {
            get => this._bones;
        }
        #endregion

        #region Getters
        public static List<PlayerBones> RequiredBones { get; } = new List<PlayerBones>
        {
            PlayerBones.HumanHead,
            PlayerBones.HumanSpine3,
            PlayerBones.HumanLPalm,
            PlayerBones.HumanRPalm,
            PlayerBones.HumanPelvis,
            PlayerBones.HumanLFoot,
            PlayerBones.HumanRFoot,
            PlayerBones.HumanLForearm1,
            PlayerBones.HumanRForearm1,
            PlayerBones.HumanLCalf,
            PlayerBones.HumanRCalf
        };

        /// <summary>
        /// Contains 'Acct UUIDs' of tracked players for the Key, and the 'Reason' for the Value.
        /// </summary>
        private static Watchlist _watchlistManager
        {
            get => Program.Config.Watchlist;
        }
        /// <summary>
        /// Player is human-controlled.
        /// </summary>
        public bool IsHuman
        {
            get => (
                this.Type is PlayerType.LocalPlayer ||
                this.Type is PlayerType.Teammate ||
                this.Type is PlayerType.PMC ||
                this.Type is PlayerType.Special ||
                this.Type is PlayerType.PlayerScav ||
                this.Type is PlayerType.BEAR ||
                this.Type is PlayerType.USEC);
        }
        /// <summary>
        /// Player is human-controlled and Active/Alive.
        /// </summary>
        public bool IsHumanActive
        {
            get => (
                this.Type is PlayerType.LocalPlayer ||
                this.Type is PlayerType.Teammate ||
                this.Type is PlayerType.PMC ||
                this.Type is PlayerType.Special ||
                this.Type is PlayerType.PlayerScav ||
                this.Type is PlayerType.BEAR ||
                this.Type is PlayerType.USEC) && IsActive && IsAlive;
        }
        /// <summary>
        /// Player is human-controlled & Hostile.
        /// </summary>
        public bool IsHumanHostile
        {
            get => (
                this.Type is PlayerType.PMC ||
                this.Type is PlayerType.Special ||
                this.Type is PlayerType.PlayerScav ||
                this.Type is PlayerType.BEAR ||
                this.Type is PlayerType.USEC);
        }
        /// <summary>
        /// Player is human-controlled, hostile, and Active/Alive.
        /// </summary>
        public bool IsHumanHostileActive
        {
            get => (
                this.Type is PlayerType.BEAR ||
                this.Type is PlayerType.USEC ||
                this.Type is PlayerType.Special ||
                this.Type is PlayerType.PlayerScav) && this.IsActive && this.IsAlive;
        }
        /// <summary>
        /// Player is AI & boss, rogue, raider etc.
        /// </summary>
        public bool IsBossRaider
        {
            get => (
                this.Type is PlayerType.Raider ||
                this.Type is PlayerType.BossFollower ||
                this.Type is PlayerType.BossGuard ||
                this.Type is PlayerType.Rogue ||
                this.Type is PlayerType.Cultist ||
                this.Type is PlayerType.Boss);
        }

        /// <summary>
        /// Player is rogue, raider etc.
        /// </summary>
        public bool IsRogueRaider
        {
            get => (
                this.Type is PlayerType.Raider ||
                this.Type is PlayerType.BossFollower ||
                this.Type is PlayerType.BossGuard ||
                this.Type is PlayerType.Rogue ||
                this.Type is PlayerType.Cultist);
        }

        /// <summary>
        /// Player is rogue, raider etc.
        /// </summary>
        public bool IsEventAI
        {
            get => (
                this.Type is PlayerType.FollowerOfMorana ||
                this.Type is PlayerType.Zombie);
        }

        /// <summary>
        /// Player is AI/human-controlled and Active/Alive.
        /// </summary>
        public bool IsHostileActive
        {
            get => (
                this.Type is PlayerType.PMC ||
                this.Type is PlayerType.BEAR ||
                this.Type is PlayerType.USEC ||
                this.Type is PlayerType.Special ||
                this.Type is PlayerType.PlayerScav ||
                this.Type is PlayerType.Scav ||
                this.Type is PlayerType.Raider ||
                this.Type is PlayerType.BossFollower ||
                this.Type is PlayerType.BossGuard ||
                this.Type is PlayerType.Rogue ||
                this.Type is PlayerType.OfflineScav ||
                this.Type is PlayerType.Cultist ||
                this.Type is PlayerType.Zombie ||
                this.Type is PlayerType.Boss) && this.IsActive && this.IsAlive;
        }
        /// <summary>
        /// Player is friendly to LocalPlayer (including LocalPlayer) and Active/Alive.
        /// </summary>
        public bool IsFriendlyActive
        {
            get => ((
                this.Type is PlayerType.LocalPlayer ||
                this.Type is PlayerType.Teammate) && this.IsActive && this.IsAlive);
        }

        public bool IsZombie
        {
            get => this.Type is PlayerType.Zombie;
        }

        /// <summary>
        /// Player has exfil'd/left the raid.
        /// </summary>
        public bool HasExfild
        {
            get => !this.IsActive && this.IsAlive;
        }
        /// <summary>
        /// Gets value of player.
        /// </summary>
        /// 
        public int Value
        {
            get => this._gearManager is not null ? this._gearManager.Value : 0;
        }
        /// <summary>
        /// EFT.Player Address
        /// </summary>
        public ulong Base { get; }
        /// <summary>
        /// EFT.Profile Address
        /// </summary>
        public ulong Profile { get; }
        /// <summary>
        /// PlayerInfo Address (GClass1044)
        /// </summary>
        public ulong Info { get; set; }

        /// <summary>
        /// Health Entries for each Body Part.
        /// </summary>
        public ulong[] HealthEntries { get; set; }
        public ulong MovementContext { get; set; }
        public ulong CorpsePtr
        {
            get => this.Base + Offsets.Player.Corpse;
        }

        public int MarkedDeadCount { get; set; } = 0;
        public string Tag { get; set; } = string.Empty;

        public string HealthStatus => this.Health switch
        {
            100 => "Healthy",
            >= 75 => "Moderate",
            >= 45 => "Poor",
            >= 20 => "Critical",
            _ => "n/a"
        };

        public bool HasThermal => _gearManager.HasThermal;
        public bool HasNVG => _gearManager.HasNVG;

        public GearManager.Gear ItemInHands { get; set; }
        #endregion

        #region Constructor
        /// <summary>
        /// Player Constructor.
        /// </summary>
        public Player(ulong playerBase, ulong playerProfile, string profileID, Vector3? pos = null, string baseClassName = null)
        {
            if (string.IsNullOrEmpty(baseClassName))
                throw new Exception("BaseClass is not set!");

            var isOfflinePlayer = string.Equals(baseClassName, "ClientPlayer") || string.Equals(baseClassName, "LocalPlayer") || string.Equals(baseClassName, "HideoutPlayer");
            var isOnlinePlayer = string.Equals(baseClassName, "ObservedPlayerView");

            if (!isOfflinePlayer && !isOnlinePlayer)
                throw new Exception("Player is not of type OfflinePlayer or OnlinePlayer");

            Debug.WriteLine("Player Constructor: Initialization started.");

            this.Base = playerBase;
            this.Profile = playerProfile;
            this.ProfileID = profileID;

            var scatterReadMap = new ScatterReadMap(1);

            if (isOfflinePlayer)
            {
                this.SetupOfflineScatterReads(scatterReadMap);
                this.ProcessOfflinePlayerScatterReadResults(scatterReadMap);
            }
            else if (isOnlinePlayer)
            {
                this.Info = playerBase;
                this.SetupOnlineScatterReads(scatterReadMap);
                this.ProcessOnlinePlayerScatterReadResults(scatterReadMap);
            }
        }
        #endregion

        #region Setters
        /// <summary>
        /// Set player health.
        /// </summary>
        public bool SetHealth(int eTagStatus)
        {
            try
            {
                this.Health = eTagStatus switch
                {
                    1024 => 100,
                    2048 => 75,
                    4096 => 45,
                    8192 => 20,
                    _ => 100,
                };
                return true;
            }
            catch (Exception ex)
            {
                Program.Log($"ERROR getting Player '{this.Name}' Health: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Set player rotation (Direction/Pitch)
        /// </summary>
        public bool SetRotation(object obj)
        {
            try
            {
                if (obj is not Vector2 rotation)
                    throw new ArgumentException("Rotation data must be of type Vector2.", nameof(obj));

                rotation.X = (rotation.X - 90 + 360) % 360;
                rotation.Y = (rotation.Y) % 360;

                this.Rotation = rotation;
                return true;
            }
            catch (Exception ex)
            {
                Program.Log($"ERROR getting Player '{this.Name}' Rotation: {ex}");
                return false;
            }
        }

        public void UpdateItemInHands()
        {
            this.ItemInHands = this.GearManager.ActiveWeapon;
        }

        public void CheckForRequiredGear()
        {
            if (this.Gear.Count < 1)
                return;

            var found = false;
            var loot = Memory.Loot;
            var requiredQuestItems = QuestManager.RequiredItems;

            foreach (var gearItem in this.Gear)
            {
                var parentItem = gearItem.Item.ID;

                if (requiredQuestItems.Contains(parentItem) ||
                    gearItem.Item.Loot.Any(x => requiredQuestItems.Contains(x.ID)) ||
                    (loot is not null && loot.RequiredFilterItems is not null && (loot.RequiredFilterItems.ContainsKey(parentItem) ||
                                      gearItem.Item.Loot.Any(x => loot.RequiredFilterItems.ContainsKey(x.ID)))))
                {
                    found = true;
                    break;
                }
            }

            this.HasRequiredGear = found;
        }
        #endregion

        #region Methods
        /// <summary>
        /// Returns PlayerType based on isAI & playuerSide
        /// </summary>
        private PlayerType GetOnlinePlayerType()
        {
            var isAI = this.AccountID == "0";

            if (!isAI)
            {
                return this.PlayerSide switch
                {
                    1 => PlayerType.USEC,
                    2 => PlayerType.BEAR,
                    _ => PlayerType.PlayerScav,
                };
            }
            else
            {
                if (string.IsNullOrEmpty(this.Name))
                    return PlayerType.Scav;

                var inFaction = Program.AIFactionManager.IsInFaction(this.Name, out var playerType);

                if (!inFaction && Memory.IsPvEMode)
                {
                    var dogtagSlot = this.Gear.FirstOrDefault(x => x.Slot.Key == "Dogtag");

                    if (dogtagSlot.Item is not null)
                        playerType = (dogtagSlot.Item.Short == "BEAR" ? PlayerType.BEAR : PlayerType.USEC);
                }
                else if (!inFaction && this.Name.Equals("???", StringComparison.OrdinalIgnoreCase))
                {
                    playerType = PlayerType.Zombie;
                }

                return playerType;
            }
        }

        private PlayerType GetOfflinePlayerType(bool isAI)
        {
            if (!isAI)
            {
                return PlayerType.LocalPlayer;
            }
            else
            {
                if (this.Name.Contains("(BTR)"))
                {
                    return PlayerType.Boss;
                }
                else if (this.PlayerRole == 51 || this.PlayerRole == 52)
                {
                    return (this.PlayerRole == 51 ? PlayerType.BEAR : PlayerType.USEC);
                }
                else if (Program.AIFactionManager.IsInFaction(this.Name, out var playerType))
                {
                    return playerType;
                }
                else if (this.Name == "???")
                {
                    return PlayerType.Zombie;
                }
                else
                {
                    return PlayerType.Scav; // default to scav
                }
            }
        }

        private void SetupOfflineScatterReads(ScatterReadMap scatterReadMap)
        {
            var round1 = scatterReadMap.AddRound();
            var round2 = scatterReadMap.AddRound();
            var round3 = scatterReadMap.AddRound();
            var round4 = scatterReadMap.AddRound();
            var round5 = scatterReadMap.AddRound();
            var round6 = scatterReadMap.AddRound();

            var info = round1.AddEntry<ulong>(0, 0, this.Profile, null, Offsets.Profile.PlayerInfo);
            var inventoryController = round1.AddEntry<ulong>(0, 1, this.Base, null, Offsets.Player.InventoryController);
            var playerBody = round1.AddEntry<ulong>(0, 2, this.Base, null, Offsets.Player.PlayerBody);
            var movementContext = round1.AddEntry<ulong>(0, 3, this.Base, null, Offsets.Player.MovementContext);
            var healthController = round1.AddEntry<ulong>(0, 4, this.Base, null, Offsets.Player.HealthController);

            var name = round2.AddEntry<ulong>(0, 5, info, null, Offsets.PlayerInfo.Nickname);
            var inventory = round2.AddEntry<ulong>(0, 6, inventoryController, null, Offsets.InventoryController.Inventory);
            var registrationDate = round2.AddEntry<int>(0, 7, info, null, Offsets.PlayerInfo.RegistrationDate);
            var groupID = round2.AddEntry<ulong>(0, 8, info, null, Offsets.PlayerInfo.GroupId);
            var botSettings = round2.AddEntry<ulong>(0, 9, info, null, Offsets.PlayerInfo.Settings);

            var equipment = round3.AddEntry<ulong>(0, 10, inventory, null, Offsets.Inventory.Equipment);
            var role = round3.AddEntry<int>(0, 11, botSettings, null, Offsets.PlayerSettings.Role);

            var inventorySlots = round4.AddEntry<ulong>(0, 12, equipment, null, Offsets.Equipment.Slots);

            scatterReadMap.Execute();
        }

        private void ProcessOfflinePlayerScatterReadResults(ScatterReadMap scatterReadMap)
        {
            if (!scatterReadMap.Results[0][0].TryGetResult<ulong>(out var info))
                return;
            if (!scatterReadMap.Results[0][1].TryGetResult<ulong>(out var inventoryController))
                return;
            if (!scatterReadMap.Results[0][2].TryGetResult<ulong>(out var playerBody))
                return;
            if (!scatterReadMap.Results[0][3].TryGetResult<ulong>(out var movementContext))
                return;
            if (!scatterReadMap.Results[0][4].TryGetResult<ulong>(out var healthController))
                return;
            if (!scatterReadMap.Results[0][5].TryGetResult<ulong>(out var name))
                return;
            if (!scatterReadMap.Results[0][8].TryGetResult<ulong>(out var groupID))
                return;
            if (!scatterReadMap.Results[0][11].TryGetResult<int>(out var role))
                return;
            if (!scatterReadMap.Results[0][12].TryGetResult<ulong>(out var inventorySlots))
                return;

            this.Info = info;
            this.PlayerRole = role;
            this.HealthController = healthController;
            this.InitializePlayerProperties(movementContext, inventoryController, inventorySlots, playerBody, name, groupID);

            if (scatterReadMap.Results[0][7].TryGetResult<int>(out var registrationDate))
            {
                var isAI = registrationDate == 0;

                this.IsLocalPlayer = !isAI;
                this.isOfflinePlayer = true;
                this.Type = this.GetOfflinePlayerType(isAI);
                this.IsPMC = (this.Type == PlayerType.BEAR || this.Type == PlayerType.USEC || !isAI);

                this.FinishAlloc();
            }
        }

        private void SetupOnlineScatterReads(ScatterReadMap scatterReadMap)
        {
            var round1 = scatterReadMap.AddRound();
            var round2 = scatterReadMap.AddRound();
            var round3 = scatterReadMap.AddRound();
            var round4 = scatterReadMap.AddRound();
            var round5 = scatterReadMap.AddRound();
            var round6 = scatterReadMap.AddRound();

            var movementContextPtr1 = round1.AddEntry<ulong>(0, 0, this.Info, null, Offsets.ObservedPlayerView.To_MovementContext[0]);
            var inventoryControllerPtr1 = round1.AddEntry<ulong>(0, 1, this.Info, null, Offsets.ObservedPlayerView.To_InventoryController[0]);
            var healthControllerPtr1 = round1.AddEntry<ulong>(0, 2, this.Info, null, Offsets.ObservedPlayerView.To_HealthController[0]);
            var accountID = round1.AddEntry<ulong>(0, 3, this.Info, null, Offsets.ObservedPlayerView.AccountID);
            var playerSide = round1.AddEntry<int>(0, 4, this.Info, null, Offsets.ObservedPlayerView.PlayerSide);
            var groupID = round1.AddEntry<ulong>(0, 5, this.Info, null, Offsets.ObservedPlayerView.GroupID);
            var playerBody = round1.AddEntry<ulong>(0, 6, this.Info, null, Offsets.ObservedPlayerView.PlayerBody);
            var memberCategory = round1.AddEntry<int>(0, 7, this.Info, null, Offsets.PlayerInfo.MemberCategory);
            var voiceStr = round1.AddEntry<ulong>(0, 8, this.Info, null, Offsets.ObservedPlayerView.VoiceName);

            var movementContextPtr2 = round2.AddEntry<ulong>(0, 9, movementContextPtr1, null, Offsets.ObservedPlayerView.To_MovementContext[1]);
            var inventoryController = round2.AddEntry<ulong>(0, 10, inventoryControllerPtr1, null, Offsets.ObservedPlayerView.To_InventoryController[1]);
            var healthController = round2.AddEntry<ulong>(0, 11, healthControllerPtr1, null, Offsets.ObservedPlayerView.To_HealthController[1]);

            var movementContext = round3.AddEntry<ulong>(0, 12, movementContextPtr2, null, Offsets.ObservedPlayerView.To_MovementContext[2]);
            var inventory = round3.AddEntry<ulong>(0, 13, inventoryController, null, Offsets.InventoryController.Inventory);

            var equipment = round4.AddEntry<ulong>(0, 14, inventory, null, Offsets.Inventory.Equipment);

            var inventorySlots = round5.AddEntry<ulong>(0, 15, equipment, null, Offsets.Equipment.Slots);

            scatterReadMap.Execute();
        }

        private void ProcessOnlinePlayerScatterReadResults(ScatterReadMap scatterReadMap)
        {
            if (!scatterReadMap.Results[0][3].TryGetResult<ulong>(out var accountID))
                return;
            if (!scatterReadMap.Results[0][4].TryGetResult<int>(out var playerSide))
                return;
            if (!scatterReadMap.Results[0][5].TryGetResult<ulong>(out var groupID))
                return;
            if (!scatterReadMap.Results[0][6].TryGetResult<ulong>(out var playerBody))
                return;
            if (!scatterReadMap.Results[0][7].TryGetResult<int>(out var memberCategory))
                return;
            if (!scatterReadMap.Results[0][8].TryGetResult<ulong>(out var voiceName))
                return;
            if (!scatterReadMap.Results[0][10].TryGetResult<ulong>(out var inventoryController))
                return;
            if (!scatterReadMap.Results[0][11].TryGetResult<ulong>(out var healthController))
                return;
            if (!scatterReadMap.Results[0][12].TryGetResult<ulong>(out var movementContext))
                return;
            if (!scatterReadMap.Results[0][15].TryGetResult<ulong>(out var inventorySlots))
                return;

            this.InitializePlayerProperties(movementContext, inventoryController, inventorySlots, playerBody, 0, groupID, playerSide);

            this.IsLocalPlayer = false;
            this.HealthController = healthController;
            this.AccountID = Memory.ReadUnityString(accountID);

            this.Type = this.GetOnlinePlayerType();
            this.IsPMC = (this.Type == PlayerType.BEAR || this.Type == PlayerType.USEC);

            if (this.IsHuman)
            {
                this.Name = "Human";
                Task.Run(async () =>
                {
                    try
                    {
                        var stats = await PlayerProfileAPI.GetPlayerStatsAsync(this.AccountID);
                        this.Name = stats.Nickname;
                        this.Level = stats.Level;
                        this.KDA = stats.KDRatio;
                        this.Hours = stats.HoursPlayed;

                        Program.Log($"Updated Player stats for '{this.Name}'");
                    }
                    catch { }
                    finally
                    {
                        this.FinishAlloc();
                    }
                });
            }
            else // npc
            {
                try
                {
                    this.Name = Memory.ReadUnityString(voiceName);
                    this.CleanAIName();
                }
                catch
                {
                    this.Name = "AI";
                }
                
                this.FinishAlloc();
            }
        }

        private void InitializePlayerProperties(ulong movementContext, ulong inventoryController, ulong inventorySlots, ulong playerBody, ulong name, ulong groupID, int playerSide = 0)
        {
            this.MovementContext = movementContext;
            this.InventoryController = inventoryController;
            this.InventorySlots = inventorySlots;
            this._gearManager = new GearManager(this.InventorySlots);
            this.PlayerBody = playerBody;

            if (name > 0)
            {
                this.Name = Memory.ReadUnityString(name);
                this.Name = Helpers.TransliterateCyrillic(this.Name);
            }

            this.PlayerSide = playerSide;

            if (groupID != 0)
            {
                var group = Memory.ReadUnityString(groupID);
                _groups.TryAdd(group, _groups.Count);
                this.GroupID = _groups[group];
            }
            else
            {
                this.GroupID = -1;
            }

            this.SetupBones();
        }

        /// <summary>
        /// Gets the pointers/transforms of the required bones
        /// </summary>
        private void SetupBones()
        {
            try
            {
                var boneMatrix = Memory.ReadPtrChain(this.PlayerBody, new uint[] { 0x30, 0x30, 0x10 });

                if (boneMatrix == 0)
                    return;

                foreach (var bone in Player.RequiredBones)
                {
                    var boneOffset = 0x20 + ((uint)bone * 0x8);
                    var bonePointer = Memory.ReadPtrChain(boneMatrix, new uint[] { boneOffset, 0x10 });

                    if (bonePointer == 0)
                        continue;

                    this._bones.TryAdd(bone, new Bone(bonePointer));
                }
            }
            catch (Exception ex)
            {
                Program.Log($"ERROR setting up bones for Player '{this.Name}': {ex}");
            }
        }

        public void RefreshBoneTransforms()
        {
            try
            {
                var boneMatrix = Memory.ReadPtrChain(this.PlayerBody, new uint[] { 0x30, 0x30, 0x10 });
                if (boneMatrix == 0)
                    return;

                foreach (var bone in Player.RequiredBones)
                {
                    var boneOffset = 0x20 + ((uint)bone * 0x8);
                    var bonePointer = Memory.ReadPtrChain(boneMatrix, new uint[] { boneOffset, 0x10 });

                    if (bonePointer == 0)
                        continue;

                    if (this._bones.TryGetValue(bone, out var boneTransform))
                        boneTransform.UpdateTransform(bonePointer);
                    else
                        this._bones.TryAdd(bone, new Bone(bonePointer));
                }
            }
            catch (Exception ex)
            {
                Program.Log($"ERROR refreshing bones for Player '{this.Name}': {ex}");
            }
        }

        /// <summary>
        /// Allocation wrap-up.
        /// </summary>
        private void FinishAlloc()
        {
            if (this.IsHumanHostile)
                this.RefreshWatchlistStatus();

            if (this.Type == PlayerType.Zombie)
                this.Name = "Zombie";
        }

        public async void RefreshWatchlistStatus()
        {
            var isOnWatchlist = _watchlistManager.IsOnWatchlist(this.AccountID, out Watchlist.Entry entry);
            var isSpecialPlayer = this.Type == PlayerType.Special;

            if ((!isSpecialPlayer || isSpecialPlayer) && isOnWatchlist)
            {
                var isLive = false;

                if (entry.IsStreamer)
                {
                    isLive = await Watchlist.IsLive(entry);

                    if (isLive)
                        this.Name += " (LIVE)";
                }

                if (!isLive && this.Name.Contains("(LIVE)"))
                {
                    this.Name = this.Name.Substring(0, this.Name.IndexOf("(LIVE)") - 1);
                }

                if (!string.IsNullOrEmpty(entry.Tag))
                {
                    this.Tag = entry.Tag;
                    this.Type = PlayerType.Special;
                }
            }
            else if (isSpecialPlayer && !isOnWatchlist)
            {
                this.Tag = "";
                this.Type = this.isOfflinePlayer ? this.GetOfflinePlayerType(false) : this.GetOnlinePlayerType();
            }
        }

        /// <summary>
        /// Resets/Updates 'static' assets in preparation for a new game/raid instance.
        /// </summary>
        public static void Reset()
        {
            _groups.Clear();
        }

        public void CleanAIName()
        {
            var cleanName = Regex.Replace(this.Name, "^Boss[_]?", "", RegexOptions.IgnoreCase);
            cleanName = cleanName.Replace("_", "");
            cleanName = Regex.Replace(cleanName, @"\d", "");

            switch (cleanName)
            {
                case "BigPipe": cleanName = "Big Pipe"; break;
                case "BirdEye": cleanName = "Birdeye"; break;
                case "Usec": cleanName = "Rogue"; break;
                case "Bully": cleanName = "Reshala"; break;
                case "Sturman": cleanName = "Shturman"; break;
                case "Gluhar": cleanName = "Glukhar"; break;
                case "SectantWarrior": cleanName = "Cultist"; break;
                case "SectantPriest" when this.Gear.Any(g => g.Item.Short == "Zryachiy"):
                    cleanName = "Zryachiy";
                    break;
                case "Scav" when this.Gear.Count == 1 && this.Gear.First().Item.Short == "AVS":
                    cleanName = "BTR";
                    break;
            }

            this.Name = cleanName;

            this.Type = this.GetOnlinePlayerType();
        }
        #endregion
    }
}
