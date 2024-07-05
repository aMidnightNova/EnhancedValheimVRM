using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnhancedValheimVRM
{


    public static class GameItem
    {
        public enum SpecialCase
        {
            SkollAndHati,
            FistFenrirClaw
        }
        
        public static readonly Dictionary<SpecialCase, List<string>> Map = new Dictionary<SpecialCase, List<string>>()
        {
            { SpecialCase.SkollAndHati, new List<string> { "KnifeSkollAndHati", "skollandhati" } },
            { SpecialCase.FistFenrirClaw, new List<string> { "FistFenrirClaw", "fenrirclaw" } }
        };

        public static SpecialCase? GetGameItemEnum(string itemName)
        {
            foreach (var entry in Map)
            {
                if (entry.Value.Contains(itemName))
                {
                    return entry.Key;
                }
            }

            return null;
        }

        public static bool TryGetGameItemEnum(string itemName, out SpecialCase gameItemEnum)
        {
            var gameItem = GetGameItemEnum(itemName);
            if (gameItem.HasValue)
            {
                gameItemEnum = gameItem.Value;
                return true;
            }

            gameItemEnum = default;
            return false;
        }
        
        public static bool IsSpecialCase(string itemName)
        {
            var specialCas = GetGameItemEnum(itemName);
            if (specialCas == null)
            {
                return false;
            }

            return Map[specialCas.Value].Contains(itemName);
        }
    }
}