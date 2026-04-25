using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Forces a specific character to say a message in chat.
    /// </summary>
    class ForceSayAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character that should say the message.")]
        public Identifier TargetTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "The message that the character should say. Can be the text as-is, or a tag referring to a line in a text file.")]
        public string Message { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should the message that the character says be sent in radio?")]
        public bool SayInRadio { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should the message be stripped of any quotation mark characters?")]
        public bool RemoveQuotes { get; set; }

        public ForceSayAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        private bool isFinished = false;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }

        public override void Reset()
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            var targets = ParentEvent.GetTargets(TargetTag);

            LocalizedString messageToSay = TextManager.Get(Message).Fallback(Message);
            foreach (var target in targets)
            {
                if (target != null && target is Character character)
                {
                    character.ForceSay(messageToSay, SayInRadio, RemoveQuotes);
                }
            }

            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(ForceSayAction)} -> (TargetTag: {TargetTag.ColorizeObject()}, " +
                   $"Message: {Message})";
        }
    }
}