using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class PowerTransfer : Powered
    {
        public List<Connection> PowerConnections { get; private set; }

        private readonly HashSet<Connection> signalConnections = new HashSet<Connection>();

        private readonly ConcurrentDictionary<Connection, bool> connectionDirty = new ConcurrentDictionary<Connection, bool>();

        //a list of connections a given connection is connected to, either directly or via other power transfer components
        //Uses ConcurrentDictionary<Connection, byte> as a thread-safe HashSet replacement
        private readonly ConcurrentDictionary<Connection, ConcurrentDictionary<Connection, byte>> connectedRecipients = 
            new ConcurrentDictionary<Connection, ConcurrentDictionary<Connection, byte>>();

        private float overloadCooldownTimer;
        private const float OverloadCooldown = 5.0f;

        protected float powerLoad;

        protected bool isBroken;

        public float PowerLoad
        {
            get
            {
                if (this is RelayComponent || PowerConnections.Count == 0 || PowerConnections[0].Grid == null)
                {
                    return powerLoad;
                }
                return PowerConnections[0].Grid.Load;
            }
            set { powerLoad = value; }
        }

        [Editable, Serialize(true, IsPropertySaveable.Yes, description: "Can the item be damaged if too much power is supplied to the power grid.")]
        public bool CanBeOverloaded
        {
            get;
            set;
        }

        [Editable(MinValueFloat = 1.0f), Serialize(2.0f, IsPropertySaveable.Yes, description:
            "How much power has to be supplied to the grid relative to the load before item starts taking damage. "
            + "E.g. a value of 2 means that the grid has to be receiving twice as much power as the devices in the grid are consuming.")]
        public float OverloadVoltage
        {
            get;
            set;
        }

        [Serialize(0.15f, IsPropertySaveable.Yes, description: "The probability for a fire to start when the item breaks."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f)]
        public float FireProbability
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "Is the item currently overloaded. Intended to be used by StatusEffect conditionals (setting the value from XML is not recommended).")]
        public bool Overload
        {
            get;
            set;
        }

        private float extraLoad;
        private float extraLoadSetTime;

        /// <summary>
        /// Additional load coming from somewhere else than the devices connected to the junction box (e.g. ballast flora or piezo crystals).
        /// Goes back to zero automatically if you stop setting the value.
        /// </summary>
        public float ExtraLoad
        {
            get { return extraLoad; }
            set 
            {
                extraLoad = value;
                extraLoadSetTime = (float)Timing.TotalTime;
            }
        }

        //can the component transfer power
        private bool canTransfer;
        public bool CanTransfer
        {
            get { return canTransfer; }
            set
            {
                if (canTransfer == value) return;
                canTransfer = value;
                SetAllConnectionsDirty();
            }
        }

        public override bool IsActive
        {
            get
            {
                return base.IsActive;
            }

            set
            {
                if (base.IsActive == value) return;
                base.IsActive = value;
                powerLoad = 0.0f;
                currPowerConsumption = 0.0f;

                SetAllConnectionsDirty();
                if (!base.IsActive)
                {
                    //we need to refresh the connections here because Update won't be called on inactive components
                    RefreshConnections();
                }
            }
        }

        public PowerTransfer(Item item, ContentXElement element)
            : base(item, element)
        {
            IsActive = true;
            canTransfer = true;

            InitProjectSpecific(element);
        }
        
        partial void InitProjectSpecific(XElement element);

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<PowerTransfer, byte> _recipientsToRefresh = new System.Collections.Concurrent.ConcurrentDictionary<PowerTransfer, byte>();
        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            base.UpdateBroken(deltaTime, cam);

            Overload = false;

            if (!isBroken)
            {
                powerLoad = 0.0f;
                currPowerConsumption = 0.0f;
                SetAllConnectionsDirty();
                _recipientsToRefresh.Clear();
                // Take snapshot for thread-safe iteration (no locks needed with ConcurrentDictionary)
                foreach (var recipientDict in connectedRecipients.Values)
                {
                    foreach (Connection c in recipientDict.Keys)
                    {
                        if (c.Item == item) { continue; }
                        var recipientPowerTransfer = c.Item.GetComponent<PowerTransfer>();
                        if (recipientPowerTransfer != null)
                        {
                            _recipientsToRefresh.TryAdd(recipientPowerTransfer, 0);
                        }
                    }
                }
                foreach (PowerTransfer recipientPowerTransfer in _recipientsToRefresh.Keys)
                {
                    recipientPowerTransfer.SetAllConnectionsDirty();
                    recipientPowerTransfer.RefreshConnections();
                }
                RefreshConnections();
                isBroken = true;
            }
        }


        private int prevSentPowerValue;
        private string powerSignal;
        private int prevSentLoadValue;
        private string loadSignal;

        public override void Update(float deltaTime, Camera cam)
        {
            RefreshConnections();

            UpdateExtraLoad(deltaTime);

            if (!CanTransfer) { return; }

            if (isBroken)
            {
                SetAllConnectionsDirty();
                isBroken = false;
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime);

            SendSignals();

            UpdateOvervoltage(deltaTime);
        }

        protected virtual void UpdateExtraLoad(float deltaTime)
        {
            if (Timing.TotalTime <= extraLoadSetTime + 1.0) { return; }

            //Decay the extra load to 0 from either positive or negative
            if (extraLoad > 0)
            {
                extraLoad = Math.Max(extraLoad - 1000.0f * deltaTime, 0);
            }
            else
            {
                extraLoad = Math.Min(extraLoad + 1000.0f * deltaTime, 0);
            }
        }

        protected virtual void SendSignals()
        {
            float powerReadingOut = 0;
            float loadReadingOut = ExtraLoad;
            if (powerLoad < 0)
            {
                powerReadingOut = -powerLoad;
                loadReadingOut = 0;
            }

            if (powerOut != null && powerOut.Grid != null)
            {
                powerReadingOut = powerOut.Grid.Power;
                loadReadingOut = powerOut.Grid.Load;
            }

            if (prevSentPowerValue != (int)powerReadingOut || powerSignal == null)
            {
                prevSentPowerValue = (int)Math.Round(powerReadingOut);
                powerSignal = prevSentPowerValue.ToString();
            }
            if (prevSentLoadValue != (int)loadReadingOut || loadSignal == null)
            {
                prevSentLoadValue = (int)Math.Round(loadReadingOut);
                loadSignal = prevSentLoadValue.ToString();
            }
            item.SendSignal(powerSignal, "power_value_out");
            item.SendSignal(loadSignal, "load_value_out");
        }

        protected virtual void UpdateOvervoltage(float deltaTime)
        {
            //if the item can't be fixed, don't allow it to break
            if (!item.Repairables.Any() || !CanBeOverloaded) { return; }

            float maxOverVoltage = Math.Max(OverloadVoltage, 1.0f);

            Overload = Voltage > maxOverVoltage && GameMain.GameSession is not { RoundDuration: < 5 };

            if (!Overload || GameMain.NetworkMember is { IsClient: true }) { return; }

            if (overloadCooldownTimer > 0.0f)
            {
                overloadCooldownTimer -= deltaTime;
                return;
            }

            //damage the item if voltage is too high (except if running as a client)
            float prevCondition = item.Condition;
            //some randomness to prevent all junction boxes from breaking at the same time
            if (Rand.Range(0.0f, 1.0f) < 0.01f)
            {
                //damaged boxes are more sensitive to overvoltage (also preventing all boxes from breaking at the same time)
                float conditionFactor = MathHelper.Lerp(5.0f, 1.0f, item.Condition / item.MaxCondition);
                item.Condition -= deltaTime * Rand.Range(10.0f, 500.0f) * conditionFactor;
            }

            if (item.Condition > 0.0f || prevCondition <= 0.0f) { return; }

            overloadCooldownTimer = OverloadCooldown;
#if CLIENT
            SoundPlayer.PlaySound("zap", item.WorldPosition, hullGuess: item.CurrentHull);
            Vector2 baseVel = Rand.Vector(300.0f);
            for (int i = 0; i < 10; i++)
            {
                var particle = GameMain.ParticleManager.CreateParticle("spark", item.WorldPosition,
                    baseVel + Rand.Vector(100.0f), 0.0f, item.CurrentHull);
                if (particle != null) particle.Size *= Rand.Range(0.5f, 1.0f);
            }
#endif
            float currentIntensity = GameMain.GameSession?.EventManager != null ?
                GameMain.GameSession.EventManager.CurrentIntensity : 0.5f;

            //higher probability for fires if the current intensity is low
            if (FireProbability > 0.0f &&
                Rand.Range(0.0f, 1.0f) < MathHelper.Lerp(FireProbability, FireProbability * 0.1f, currentIntensity))
            {
                new FireSource(item.WorldPosition);
            }
        }

        public override float GetConnectionPowerOut(Connection conn, float power, PowerRange minMaxPower, float load)
        {
            //not used in the vanilla game (junction boxes or relays don't output power)
            return conn == powerOut ? MathHelper.Max(-(PowerConsumption + ExtraLoad), 0) : 0;
        }

        public override bool Pick(Character picker)
        {
            return picker != null;
        }

        protected void RefreshConnections()
        {
            var connections = item.Connections;
            if (connections == null) { return; }
            
            // Take a snapshot of connections for thread-safe iteration
            var connectionSnapshot = connections.ToList();
            foreach (Connection c in connectionSnapshot)
            {
                if (!connectionDirty.TryGetValue(c, out bool isDirty))
                {
                    connectionDirty[c] = true;
                    isDirty = true;
                }
                
                if (!isDirty)
                {
                    continue;
                }

                //find all connections that are connected to this one (directly or via another PowerTransfer)
                var tempConnected = connectedRecipients.GetOrAdd(c, _ => new ConcurrentDictionary<Connection, byte>());
                
                // Get previous recipients and clear
                var previousRecipients = tempConnected.Keys.ToList();
                tempConnected.Clear();
                
                //mark all previous recipients as dirty
                foreach (Connection recipient in previousRecipients)
                {
                    var pt = recipient.Item.GetComponent<PowerTransfer>();
                    if (pt != null) { pt.connectionDirty[recipient] = true; }
                }

                tempConnected.TryAdd(c, 0);
                if (item.Condition > 0.0f)
                {
                    GetConnected(c, tempConnected);
                    //go through all the PowerTransfers that we're connected to and set their connections to match the ones we just calculated
                    //(no need to go through the recursive GetConnected method again)
                    // Take snapshot for thread-safe iteration (no locks needed)
                    var tempConnectedSnapshot = tempConnected.Keys.ToList();
                    foreach (Connection recipient in tempConnectedSnapshot)
                    {
                        if (recipient == c) { continue; }
                        var recipientPowerTransfer = recipient.Item.GetComponent<PowerTransfer>();
                        if (recipientPowerTransfer == null) { continue; }
                        
                        var recipientSet = recipientPowerTransfer.connectedRecipients.GetOrAdd(recipient, _ => new ConcurrentDictionary<Connection, byte>());
                        recipientSet.Clear();
                        foreach (var connection in tempConnectedSnapshot)
                        {
                            recipientSet.TryAdd(connection, 0);
                        }
                        recipientPowerTransfer.connectionDirty[recipient] = false;
                    }
                }
                connectionDirty[c] = false;
            }
        }

        //Finds all the connections that can receive a signal sent into the given connection and stores them in the concurrent dictionary.
        private void GetConnected(Connection c, ConcurrentDictionary<Connection, byte> connected)
        {
            // Take snapshot for thread-safe iteration
            var recipients = c.Recipients.ToList();

            foreach (Connection recipient in recipients)
            {
                if (recipient == null || connected.ContainsKey(recipient)) { continue; }

                Item it = recipient.Item;
                if (it == null || it.Condition <= 0.0f) { continue; }

                connected.TryAdd(recipient, 0);

                var powerTransfer = it.GetComponent<PowerTransfer>();
                if (powerTransfer != null && powerTransfer.CanTransfer && powerTransfer.IsActive)
                {
                    GetConnected(recipient, connected);
                }
            }
        }

        public void SetAllConnectionsDirty()
        {
            if (item.Connections == null) { return; }
            foreach (Connection c in item.Connections)
            {
                connectionDirty[c] = true;
                if (c.IsPower)
                {
                    MarkConnectionChanged(c);
                    if (connectedRecipients.TryGetValue(c, out var recipients))
                    {
                        // No lock needed - ConcurrentDictionary.Keys is thread-safe
                        foreach (var conn in recipients.Keys.Where(conn => conn.IsPower))
                        {
                            MarkConnectionChanged(conn);
                        }
                    }
                }
            }
        }

        public void SetConnectionDirty(Connection connection)
        {
            var connections = item.Connections;
            if (connections == null || !connections.Contains(connection)) { return; }
            connectionDirty[connection] = true;
            if (connection.IsPower)
            {
                MarkConnectionChanged(connection);
                if (connectedRecipients.TryGetValue(connection, out var recipients))
                {
                    // No lock needed - ConcurrentDictionary.Keys is thread-safe
                    foreach (var conn in recipients.Keys.Where(conn => conn.IsPower))
                    {
                        MarkConnectionChanged(conn);
                    }
                }
            }
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            var connections = Item.Connections;
            PowerConnections = connections == null ? new List<Connection>() : connections.FindAll(c => c.IsPower);
            if (connections == null)
            {
                IsActive = false;
                return;
            }

            foreach (Connection c in connections)
            {
                if (c.Name.Length > 5 && c.Name.Substring(0, 6) == "signal")
                {
                    signalConnections.Add(c);
                }
            }

            if (this is not RelayComponent and not PowerDistributor)
            {
                if (PowerConnections.Any(p => !p.IsOutput) && PowerConnections.Any(p => p.IsOutput))
                {
                    DebugConsole.ThrowError("Error in item \"" + Name + "\" - PowerTransfer components should not have separate power inputs and outputs, but transfer power between wires connected to the same power connection. " +
                        "If you want power to pass from input to output, change the component to a RelayComponent.");
                }
            }

            SetAllConnectionsDirty();
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            if (item.Condition <= 0.0f || connection.IsPower) { return; }
            if (!connectedRecipients.TryGetValue(connection, out var recipients)) { return; }
            if (!signalConnections.Contains(connection)) { return; }

            // No lock needed - ConcurrentDictionary.Keys is thread-safe
            // Use ToList() snapshot for thread-safe iteration
            foreach (Connection recipient in recipients.Keys.ToList())
            {
                if (recipient.Item == item || recipient.Item == signal.source) { continue; }

                signal.source?.LastSentSignalRecipients.Add(recipient);

                // Use ToArray() snapshot for thread-safe iteration
                foreach (ItemComponent ic in recipient.Item.Components.ToArray())
                {
                    //other junction boxes don't need to receive the signal in the pass-through signal connections
                    //because we relay it straight to the connected items without going through the whole chain of junction boxes
                    if (ic is PowerTransfer and not RelayComponent and not PowerDistributor) { continue; }
                    ic.ReceiveSignal(signal, recipient);
                }

                if (recipient.Effects != null && signal.value != "0" && !string.IsNullOrEmpty(signal.value))
                {
                    // Use ToArray() snapshot for thread-safe iteration
                    foreach (StatusEffect effect in recipient.Effects.ToArray())
                    {
                        recipient.Item.ApplyStatusEffect(effect, ActionType.OnUse, 1.0f);
                    }
                }
            }            
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            connectedRecipients?.Clear();
            connectionDirty?.Clear();
            _recipientsToRefresh.Clear();
        }
    }
}
