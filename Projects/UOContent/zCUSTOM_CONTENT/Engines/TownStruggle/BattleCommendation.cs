using ModernUO.Serialization;
using Server.Gumps;
using Server.Items;
using Server.Network;

namespace Server.Engines.TownStruggle;

[SerializationGenerator(0)]
public partial class BattleCommendation : Item
{
    public const int GoldPerToken = 2500;

    [Constructible]
    public BattleCommendation() : this(1)
    {
    }

    [Constructible]
    public BattleCommendation(int amount) : base(0x14F0)
    {
        Stackable = true;
        Amount = System.Math.Clamp(amount, 1, 60000);
        Hue = 0x489;
        Weight = 0.1;
        LootType = LootType.Blessed;
    }

    public override string DefaultName => "battle commendation";

    public override void OnDoubleClick(Mobile from) => RedeemGump.DisplayTo(from, this);

    internal bool CanRedeem(Mobile from) => !Deleted && Amount > 0 && from is { Deleted: false, Alive: true } &&
        (from.Backpack != null && IsChildOf(from.Backpack) || from.FindBankNoCreate() is { } bank && IsChildOf(bank));

    internal bool TryRedeem(Mobile from, out string message)
    {
        if (!CanRedeem(from))
        {
            message = "The commendation must be in your backpack or bank, and you must be alive.";
            return false;
        }

        var gold = new Gold(GoldPerToken);
        if (!from.BankBox.TryDropItem(from, gold, false))
        {
            gold.Delete();
            message = "Your bank cannot hold the gold. Your commendation has not been consumed.";
            return false;
        }

        Consume(1);
        message = "One battle commendation was redeemed for 2,500 gold in your bank.";
        return true;
    }

    internal sealed class RedeemGump : DynamicGump
    {
        private readonly Mobile _viewer;
        private readonly BattleCommendation _token;
        private bool _responded;

        private RedeemGump(Mobile viewer, BattleCommendation token) : base(50, 50)
        {
            _viewer = viewer;
            _token = token;
        }

        public override bool Singleton => true;

        public static void DisplayTo(Mobile viewer, BattleCommendation token)
        {
            if (viewer is not { Deleted: false, NetState: not null } || token == null)
            {
                return;
            }
            if (!token.CanRedeem(viewer))
            {
                viewer.SendMessage("Keep the commendation in your backpack or bank to redeem it.");
                return;
            }
            viewer.SendGump(new RedeemGump(viewer, token));
        }

        protected override void BuildLayout(ref DynamicGumpBuilder builder) => BuildConfirmation(ref builder);

        internal static void BuildConfirmation(ref DynamicGumpBuilder builder)
        {
            builder.AddPage();
            CustomGumpStyle.AddPanel(ref builder, 0, 0, 480, 250, "BATTLE COMMENDATION");
            builder.AddHtml(28, 48, 424, 96,
                "Redeem one battle commendation for 2,500 gold deposited into your bank? This consumes one token. Random-item redemption is not available.",
                CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
            CustomGumpStyle.AddOptionButton(ref builder, 28, 188, 1);
            builder.AddHtml(65, 191, 150, 24, "REDEEM", CustomGumpStyle.ActionColor, CustomGumpStyle.StandardFontSize);
            CustomGumpStyle.AddOptionButton(ref builder, 304, 188, 0);
            builder.AddHtml(341, 191, 110, 24, "CANCEL", CustomGumpStyle.ActionColor, CustomGumpStyle.StandardFontSize);
        }

        public override void OnResponse(NetState sender, in RelayInfo info)
        {
            if (_responded || sender?.Mobile != _viewer || info.ButtonID != 1)
            {
                return;
            }
            _responded = true;
            _token.TryRedeem(_viewer, out var message);
            _viewer.SendMessage(message);
        }
    }
}
