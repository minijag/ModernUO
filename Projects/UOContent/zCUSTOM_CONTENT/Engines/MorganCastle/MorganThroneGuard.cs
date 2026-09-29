using ModernUO.Serialization;
using Server.Mobiles;

namespace Server.Engines.MorganCastle;

[SerializationGenerator(0)]
public partial class MorganThroneGuard : MorganCastleResident
{
    [SerializableField(0)]
    private bool _facesEast;

    [Constructible]
    public MorganThroneGuard(bool facesEast) : base("swordsman")
    {
        _facesEast = facesEast;
        Title = "the royal guard";
        FightMode = FightMode.None;
        HoldPost();
    }

    private void HoldPost()
    {
        Frozen = true;
        CantWalk = true;
        Direction = _facesEast ? Direction.East : Direction.West;
    }

    public override bool Move(Direction direction) => false;

    [AfterDeserialization]
    private void RestorePost() => HoldPost();

    public override void OnThink()
    {
        base.OnThink();
        HoldPost();
    }
}
