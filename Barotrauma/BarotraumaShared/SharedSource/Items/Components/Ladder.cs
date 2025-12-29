using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Ladder : ItemComponent
    {
        private static readonly ConcurrentDictionary<Ladder, byte> _ladderDict = new ConcurrentDictionary<Ladder, byte>();
        public static IEnumerable<Ladder> List => _ladderDict.Keys;

        public Ladder(Item item, ContentXElement element)
            : base(item, element)
        {
            InitProjSpecific(element);
            _ladderDict.TryAdd(this, 0);
        }

        partial void InitProjSpecific(ContentXElement element);

        public override bool Select(Character character)
        {
            if (character == null || character.LockHands || character.Removed ) { return false; }
            if (!character.CanClimb) { return false; }
            character.AnimController.StartClimbing();
            return true;
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            RemoveProjSpecific();
            _ladderDict.TryRemove(this, out _);
        }

        partial void RemoveProjSpecific();
    }
}
