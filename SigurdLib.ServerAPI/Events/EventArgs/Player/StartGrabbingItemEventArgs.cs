﻿namespace Sigurd.ServerAPI.Events.EventArgs.Player
{
    public class StartGrabbingItemEventArgs : System.EventArgs
    {
        public Features.Player Player { get; }

        public Features.Item Item { get; }

        public bool IsAllowed { get; set; } = true;

        public StartGrabbingItemEventArgs(Features.Player player, Features.Item item)
        {
            Player = player;
            Item = item;
        }
    }
}
