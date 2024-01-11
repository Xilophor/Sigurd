﻿namespace Sigurd.ServerAPI.Events.EventArgs.Player
{
    public class GrabbingItemEventArgs : System.EventArgs
    {
        public Features.Player Player { get; }

        public Features.Item Item { get; }

        public bool IsAllowed { get; set; } = true;

        public GrabbingItemEventArgs(Features.Player player, Features.Item item)
        {
            Player = player;
            Item = item;
        }
    }
}
