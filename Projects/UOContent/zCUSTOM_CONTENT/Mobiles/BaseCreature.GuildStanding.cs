namespace Server.Mobiles;

public partial class BaseCreature
{
    // Creature death awards standing once, even if multiple eligible contributors are evaluated.
    private bool _guildStandingKillCredited;

    internal bool TryCreditGuildStanding()
    {
        if (_guildStandingKillCredited)
        {
            return false;
        }

        _guildStandingKillCredited = true;
        return true;
    }
}
