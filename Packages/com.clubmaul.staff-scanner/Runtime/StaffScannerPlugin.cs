// Staff Scanner V2 — plugin asset
// A ScriptableObject dragged into the component's Plugins list. Edit its Contacts in the
// inspector (+/-); each becomes a world-locked sender + menu toggle, like the World Features.

using System.Collections.Generic;
using UnityEngine;

namespace ClubMaul.StaffScanner
{
    public enum PluginControlType
    {
        Toggle, // stays on/off
        Button, // on only while held
    }

    /// <summary>One contact: its menu/object label and the collision tag the world listens for.</summary>
    [System.Serializable]
    public class PluginContact
    {
        [Tooltip("Menu item and object name.")]
        public string Name = "Contact";

        [Tooltip("Collision tag the world listens for (e.g. 'Suburbia/Flicker').")]
        public string CollisionTag = "";

        [Tooltip("Toggle stays on/off; Button is on only while held.")]
        public PluginControlType ControlType = PluginControlType.Toggle;
    }

    [CreateAssetMenu(menuName = "Club Maul/Staff Scanner/Plugin", fileName = "New Plugin")]
    public class StaffScannerPlugin : ScriptableObject
    {
        [Tooltip("Contacts this plugin creates.")]
        public List<PluginContact> Contacts = new List<PluginContact>();

        /// <summary>Label for this plugin's menu sub-folder.</summary>
        public string DisplayName => name;
    }
}
