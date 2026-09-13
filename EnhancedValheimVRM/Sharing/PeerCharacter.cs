namespace EnhancedValheimVRM
{
    internal static class PeerCharacter
    {
        // Valheim publishes the character's ZDOID through CharacterID RPC. The
        // stable profile ID is stored in that owned ZDO, just as Player.GetPlayerID
        // reads it. ZNetPeer.m_playerID is not populated by the current game client.
        internal static long GetId(ZNetPeer peer)
        {
            if (peer == null || !peer.IsReady() || peer.m_characterID == ZDOID.None || ZDOMan.instance == null)
                return 0;
            var character = ZDOMan.instance.GetZDO(peer.m_characterID);
            return character != null && character.GetOwner() == peer.m_uid
                ? character.GetLong(ZDOVars.s_playerID, 0L)
                : 0;
        }
    }
}
