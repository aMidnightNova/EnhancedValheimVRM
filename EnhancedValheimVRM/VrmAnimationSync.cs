using System.Collections.Generic;

namespace EnhancedValheimVRM
{
    public class VrmAnimationSync
    {
        const int FirstTime         = -161139084;
        const int Usually           =  229373857;  // standing idle
        const int FirstRise         = -1536343465; // stand up upon login
        const int RiseUp            = -805461806;
        const int StartToSitDown    =  890925016;
        const int SittingIdle       = -1544306596;
        const int StandingUpFromSit = -805461806;
        const int SittingChair      = -1829310159;
        const int SittingThrone     =  1271596;
        const int SittingShip       = -675369009;
        const int StartSleeping     =  337039637;
        const int Sleeping          = -1603096;
        const int GetUpFromBed      = -496559199;
        const int Crouch            = -2015693266;
        const int HoldingMast       = -2110678410;
        const int HoldingDragon     = -2076823180; // that thing in a front of longship
        
        
        
        
        private static List<int> adjustHipHashes = new List<int>()
        {
            SittingChair,
            SittingThrone,
            SittingShip,
            Sleeping
        };
        
        
        
        
        
        
        
        
        
        
        
        
        
    }
}