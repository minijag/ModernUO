using System;
using Server.Engines.Help;
using Server.Factions;
using Server.Gumps;
using Server.Mobiles;
using Server.Network;

namespace Server.Menus.Questions
{
    public class StuckMenuEntry
    {
        public StuckMenuEntry(int name, Point3D[] locations)
        {
            Name = name;
            Locations = locations;
        }

        public int Name { get; }

        public Point3D[] Locations { get; }
    }

    public class StuckMenu : Gump
    {
        private static readonly StuckMenuEntry[] m_Entries =
        [
            // Britain
            new StuckMenuEntry(
                1011028,
                [
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0)
                ]
            ),

            // Trinsic
            new StuckMenuEntry(
                1011029,
                [
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0)
                ]
            ),

            // Vesper
            new StuckMenuEntry(
                1011030,
                [
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0)
                ]
            ),

            // Minoc
            new StuckMenuEntry(
                1011031,
                [
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0)
                ]
            ),

            // Yew
            new StuckMenuEntry(
                1011032,
                [
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0)
                ]
            ),

            // Cove
            new StuckMenuEntry(
                1011033,
                [
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0),
                    new Point3D(1769, 1055, 0)
                ]
            )
        ];

        private static readonly StuckMenuEntry[] m_T2AEntries =
        [
            // Papua
            new StuckMenuEntry(
                1011057,
                [
                    new Point3D(1778, 1072, -1),
                    new Point3D(5677, 3176, -3),
                    new Point3D(5678, 3227, 0),
                    new Point3D(5769, 3206, -2),
                    new Point3D(5777, 3270, -1)
                ]
            ),

            // Delucia
            new StuckMenuEntry(
                1011058,
                [
                    new Point3D(5216, 4033, 37),
                    new Point3D(5262, 4049, 37),
                    new Point3D(5284, 4006, 37),
                    new Point3D(5189, 3971, 39),
                    new Point3D(5243, 3960, 37)
                ]
            )
        ];

        private readonly bool m_MarkUse;

        private readonly Mobile m_Mobile;
        private readonly Mobile m_Sender;

        private Timer m_Timer;

        public StuckMenu(Mobile beholder, Mobile beheld, bool markUse) : base(150, 50)
        {
            m_Sender = beholder;
            m_Mobile = beheld;
            m_MarkUse = markUse;

            Closable = true;
            Draggable = true;
            Disposable = true;
            var mainTextColor = 0x8E7C5C; // Yellow

            AddBackground(0, 0, 270, 320, 5100);

            AddHtmlLocalized(50, 20, 250, 35, 1011027, 0x420C); // Chose a town:

            var entries = IsInSecondAgeArea(beheld) ? m_T2AEntries : m_Entries;

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];

                AddButton(50, 55 + 35 * i, 1209, 1210, i + 1);
                AddHtmlLocalized(75, 55 + 35 * i, 335, 40, entry.Name, mainTextColor);
            }

            AddButton(55, 263, 4005, 4007, 0);
            AddHtmlLocalized(90, 265, 200, 35, 1011012, mainTextColor); // CANCEL
        }

        private static bool IsInSecondAgeArea(Mobile m) =>
            (m.Map == Map.Trammel || m.Map == Map.Felucca) &&
            (m.X >= 5120 && m.Y >= 2304 || m.Region.IsPartOf("Terathan Keep"));

        public void BeginClose()
        {
            StopClose();

            m_Timer = new CloseTimer(m_Mobile);
            m_Timer.Start();

            m_Mobile.Frozen = true;
        }

        public void StopClose()
        {
            m_Timer?.Stop();

            m_Mobile.Frozen = false;
        }

        public override void OnResponse(NetState state, in RelayInfo info)
        {
            StopClose();

            if (Sigil.ExistsOn(m_Mobile))
            {
                m_Mobile.SendLocalizedMessage(1061632); // You can't do that while carrying the sigil.
            }
            else if (info.ButtonID == 0)
            {
                if (m_Mobile == m_Sender)
                {
                    m_Mobile.SendLocalizedMessage(1010588); // You choose not to go to any city.
                }
            }

            var index = info.ButtonID - 1;
            var entries = IsInSecondAgeArea(m_Mobile) ? m_T2AEntries : m_Entries;

            if (index >= 0 && index < entries.Length)
            {
                Teleport(entries[index]);
            }

            HelpEvents.InvokeStuckMenu(m_Mobile);
        }

        private void Teleport(StuckMenuEntry entry)
        {
            if (m_MarkUse)
            {
                // m_Mobile.SendLocalizedMessage(1010589); // You will be teleported within the next two minutes.
                m_Mobile.SendMessage("You will be teleported within the next 2 seconds");

                new TeleportTimer(m_Mobile, entry, TimeSpan.FromSeconds(1.0 + Utility.RandomDouble() * 1.0)).Start();

                if (m_Mobile is PlayerMobile mobile)
                {
                    mobile.UsedStuckMenu();
                }
            }
            else
            {
                new TeleportTimer(m_Mobile, entry, TimeSpan.Zero).Start();
            }
        }

        private class CloseTimer : Timer
        {
            private readonly DateTime m_End;
            private readonly Mobile m_Mobile;

            public CloseTimer(Mobile m) : base(TimeSpan.Zero, TimeSpan.FromSeconds(1.0))
            {
                m_Mobile = m;
                m_End = Core.Now + TimeSpan.FromMinutes(3.0);
            }

            protected override void OnTick()
            {
                if (m_Mobile.NetState == null || Core.Now > m_End)
                {
                    m_Mobile.Frozen = false;
                    m_Mobile.CloseGump<StuckMenu>();

                    Stop();
                }
                else
                {
                    m_Mobile.Frozen = true;
                }
            }
        }

        private class TeleportTimer : Timer
        {
            private readonly StuckMenuEntry m_Destination;
            private readonly DateTime m_End;
            private readonly Mobile m_Mobile;

            public TeleportTimer(Mobile mobile, StuckMenuEntry destination, TimeSpan delay) : base(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1.0)
            )
            {

                m_Mobile = mobile;
                m_Destination = destination;
                m_End = Core.Now + delay;
            }

            protected override void OnTick()
            {
                if (Core.Now < m_End)
                {
                    m_Mobile.Frozen = true;
                }
                else
                {
                    m_Mobile.Frozen = false;
                    Stop();

                    if (Sigil.ExistsOn(m_Mobile))
                    {
                        m_Mobile.SendLocalizedMessage(1061632); // You can't do that while carrying the sigil.
                        return;
                    }

                    var dest = m_Destination.Locations.RandomElement();

                    Map destMap;
                    if (m_Mobile.Map == Map.Trammel)
                    {
                        destMap = Map.Trammel;
                    }
                    else if (m_Mobile.Map == Map.Felucca)
                    {
                        destMap = Map.Felucca;
                    }
                    else
                    {
                        destMap = m_Mobile.Kills >= 5 ? Map.Felucca : Map.Trammel;
                    }

                    BaseCreature.TeleportPets(m_Mobile, dest, destMap);
                    m_Mobile.MoveToWorld(dest, destMap);
                }
            }
        }
    }
}
